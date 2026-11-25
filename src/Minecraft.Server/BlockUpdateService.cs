using System.Collections.Concurrent;
using System.Diagnostics;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;

namespace LetsCreateNetworkGame.Server;

/// <summary>
/// Runs block simulation on a dedicated thread. Water uses a TrueCraft-style
/// cellular automaton: each fluid block examines its neighbors and nearest
/// drop-offs, then flows outward or settles.
///
/// Unlike the old full-world scan, only cells near an edit are active. Oceans
/// generated at world creation therefore cost nothing, while digging next to
/// water still makes it flow into the hole.
/// </summary>
public sealed class BlockUpdateService : IDisposable
{
    private const int TicksPerSecond = 10;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1.0 / TicksPerSecond);
    private const int MaximumFluidDepletion = 7;
    private const int LavaMaximumFluidDepletion = 3;
    private const int DropCheckDistance = 4;
    private const int PlantGrowthIntervalTicks = 20;
    private const int PlantGrowthSamples = 1024;
    private const int MaximumActiveCells = 24000;
    private const int MaximumCellsPerTick = 3500;

    private readonly World _world;
    private readonly object _worldSync;
    private readonly Action<BlockChange> _broadcast;
    private readonly Action<string> _log;
    private readonly Action<Minecraft.Shared.Entities.EntityType, float, float, float, int, byte>? _spawnEntity;
    private readonly ConcurrentQueue<BlockChange> _edits = new();
    private readonly HashSet<(int X, int Y, int Z)> _active = new();
    private readonly object _activeSync = new();
    private readonly Thread _thread;
    private volatile bool _running;
    private int _tickCounter;
    private readonly Random _random = new();

    public BlockUpdateService(
        World world,
        object worldSync,
        Action<BlockChange> broadcast,
        Action<string> log,
        Action<Minecraft.Shared.Entities.EntityType, float, float, float, int, byte>? spawnEntity = null)
    {
        _world = world;
        _worldSync = worldSync;
        _broadcast = broadcast;
        _log = log;
        _spawnEntity = spawnEntity;
        _running = true;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Block updates",
        };
        _thread.Start();
    }

    public bool Enqueue(BlockChange change)
    {
        if (!_world.InBounds(change.X, change.Y, change.Z))
        {
            return false;
        }

        if (!BlockRegistry.TryGet(change.Block, out _))
        {
            return false;
        }

        _edits.Enqueue(change);
        return true;
    }

    public void Dispose()
    {
        _running = false;

        if (_thread.IsAlive)
        {
            _thread.Join(2000);
        }
    }

    private void Run()
    {
        var stopwatch = Stopwatch.StartNew();
        var nextTick = TickInterval;

        while (_running)
        {
            Tick();

            _tickCounter++;
            nextTick += TickInterval;
            var remaining = nextTick - stopwatch.Elapsed;

            if (remaining > TimeSpan.Zero)
            {
                Thread.Sleep(remaining);
            }
            else
            {
                nextTick = stopwatch.Elapsed + TickInterval;
            }
        }
    }

    private void Tick()
    {
        var changes = new List<BlockChange>();

        DrainEdits(changes);
        ProcessActiveCells(changes);

        if (_tickCounter % PlantGrowthIntervalTicks == 0)
        {
            GrowPlants(changes);
        }

        // Collapse repeated edits so clients receive the final state of a cell.
        var final = new Dictionary<(int X, int Y, int Z), BlockChange>();

        foreach (var change in changes)
        {
            final[(change.X, change.Y, change.Z)] = change;
        }

        foreach (var change in final.Values)
        {
            _broadcast(change);
        }
    }

    private void DrainEdits(List<BlockChange> changes)
    {
        while (_edits.TryDequeue(out var change))
        {
            lock (_worldSync)
            {
                if (!_world.InBounds(change.X, change.Y, change.Z))
                {
                    continue;
                }

                var old = _world.GetBlock(change.X, change.Y, change.Z);
                if (old == change.Block)
                {
                    continue;
                }

                if (_world.SetBlock(change.X, change.Y, change.Z, change.Block))
                {
                    changes.Add(change);
                    Activate(change.X, change.Y, change.Z);

                    if (change.Block == BlockId.Fire)
                    {
                        TryIgnitePortal(changes, change.X, change.Y, change.Z);
                    }

                    if (change.Block == BlockId.Air && (old == BlockId.Obsidian || old == BlockId.NetherPortal))
                    {
                        BreakInvalidPortals(changes, change.X, change.Y, change.Z);
                    }
                }
            }
        }
    }

    private void ProcessActiveCells(List<BlockChange> changes)
    {
        List<(int X, int Y, int Z)> batch;

        lock (_activeSync)
        {
            if (_active.Count == 0)
            {
                return;
            }

            batch = new List<(int X, int Y, int Z)>(Math.Min(_active.Count, MaximumCellsPerTick));

            foreach (var cell in _active)
            {
                batch.Add(cell);

                if (batch.Count >= MaximumCellsPerTick)
                {
                    break;
                }
            }

            foreach (var cell in batch)
            {
                _active.Remove(cell);
            }
        }

        foreach (var (x, y, z) in batch)
        {
            lock (_worldSync)
            {
                UpdateCell(changes, x, y, z);
            }
        }
    }

    private void UpdateCell(List<BlockChange> changes, int x, int y, int z)
    {
        if (!_world.InBounds(x, y, z))
        {
            return;
        }

        var block = _world.GetBlock(x, y, z);

        if (BlockRegistry.IsFluid(block))
        {
            DoAutomata(changes, x, y, z);
            return;
        }

        if (block is BlockId.Sand or BlockId.Gravel)
        {
            DoFallingBlock(changes, x, y, z, block);
            return;
        }

        if (BlockRegistry.IsPlant(block))
        {
            DoPlantSupport(changes, x, y, z, block);
        }
    }

    private void DoFallingBlock(List<BlockChange> changes, int x, int y, int z, BlockId block)
    {
        var below = y - 1;

        if (below < 0)
        {
            return;
        }

        var target = _world.GetBlock(x, below, z);

        if (target != BlockId.Air && !BlockRegistry.IsPlant(target) && !BlockRegistry.IsWater(target) && !BlockRegistry.IsLava(target))
        {
            return;
        }

        SetBlock(changes, x, y, z, BlockId.Air);

        _spawnEntity?.Invoke(
            Minecraft.Shared.Entities.EntityType.FallingBlock,
            x + 0.5f,
            y + 0.5f,
            z + 0.5f,
            (int)block,
            0);
    }

    private void DoPlantSupport(List<BlockChange> changes, int x, int y, int z, BlockId block)
    {
        if (y == 0)
        {
            SetBlock(changes, x, y, z, BlockId.Air);
            return;
        }

        var below = _world.GetBlock(x, y - 1, z);

        if (BlockRegistry.IsCrop(block))
        {
            if (below is BlockId.Farmland or BlockId.Dirt or BlockId.Grass)
            {
                return;
            }

            SetBlock(changes, x, y, z, BlockId.Air);
            return;
        }

        if (block == BlockId.SugarCane)
        {
            if (below == BlockId.SugarCane)
            {
                return;
            }

            var supported = below is BlockId.Grass or BlockId.Dirt or BlockId.Sand;
            var wet = BlockRegistry.IsWater(_world.GetBlock(x + 1, y - 1, z)) ||
                      BlockRegistry.IsWater(_world.GetBlock(x - 1, y - 1, z)) ||
                      BlockRegistry.IsWater(_world.GetBlock(x, y - 1, z + 1)) ||
                      BlockRegistry.IsWater(_world.GetBlock(x, y - 1, z - 1));

            if (supported && wet)
            {
                return;
            }

            SetBlock(changes, x, y, z, BlockId.Air);
            return;
        }

        if (below is BlockId.Grass or BlockId.Dirt or BlockId.Sand or BlockId.Farmland)
        {
            return;
        }

        SetBlock(changes, x, y, z, BlockId.Air);
    }

    private void DoAutomata(List<BlockChange> changes, int x, int y, int z)
    {
        if (!_world.InBounds(x, y, z))
        {
            return;
        }

        var current = _world.GetBlock(x, y, z);

        if (!BlockRegistry.IsFluid(current))
        {
            return;
        }

        var coords = new Coordinates3D(x, y, z);
        var previousLevel = Math.Max(BlockRegistry.FluidLevel(current), 0);

        var inward = DetermineInwardFlow(coords);
        var outward = DetermineOutwardFlow(coords, previousLevel);

        // Flowing down takes priority for non-source fluid.
        if (outward.Length == 1 &&
            outward[0].TargetBlock == coords + Coordinates3D.Down &&
            previousLevel != 0)
        {
            FlowOutward(changes, outward[0]);
            return;
        }

        var maxDepletion = BlockRegistry.IsLava(current) ? LavaMaximumFluidDepletion : MaximumFluidDepletion;

        // Inward flow (neighboring fluid levels feeding this cell).
        if (inward > maxDepletion)
        {
            SetBlock(changes, x, y, z, BlockId.Air);
            return;
        }

        if (inward != previousLevel)
        {
            SetBlock(
                changes,
                x,
                y,
                z,
                BlockRegistry.IsLava(current)
                    ? BlockRegistry.LavaBlockForLevel(inward)
                    : BlockRegistry.WaterBlockForLevel(inward));

            if (inward == 0 && previousLevel != 0)
            {
                // Became a source block; neighbors should re-evaluate next tick.
                ActivateNeighbors(x, y, z);
                return;
            }
        }

        // Outward flow.
        foreach (var flow in outward)
        {
            FlowOutward(changes, flow);
        }
    }

    private byte DetermineInwardFlow(Coordinates3D coords)
    {
        var currentLevel = Math.Max(BlockRegistry.FluidLevel(_world.GetBlock(coords.X, coords.Y, coords.Z)), 0);

        var upBlock = GetBlock(coords + Coordinates3D.Up);

        if (BlockRegistry.IsFluid(upBlock))
        {
            return (byte)currentLevel;
        }

        if (currentLevel == 0)
        {
            return 0;
        }

        byte highestNeighboringFluid = 15;
        var neighboringSourceBlocks = 0;

        foreach (var neighbor in Neighbors(coords))
        {
            var neighborBlock = GetBlock(neighbor);

            if (!BlockRegistry.IsFluid(neighborBlock))
            {
                continue;
            }

            var neighborLevel = Math.Max(BlockRegistry.FluidLevel(neighborBlock), 0);

            if (neighborLevel < highestNeighboringFluid)
            {
                highestNeighboringFluid = (byte)neighborLevel;
            }

            if (neighborLevel == 0)
            {
                neighboringSourceBlocks++;
            }
        }

        if (neighboringSourceBlocks >= 2)
        {
            return 0;
        }

        if (highestNeighboringFluid > 0)
        {
            return (byte)(highestNeighboringFluid + 1);
        }

        return (byte)currentLevel;
    }

    private LiquidFlow[] DetermineOutwardFlow(Coordinates3D coords, int currentLevel)
    {
        var outwardFlow = new List<LiquidFlow>(5);

        var water = !BlockRegistry.IsLava(_world.GetBlock(coords.X, coords.Y, coords.Z));

        var below = coords + Coordinates3D.Down;

        if (InBounds(below) && IsPassable(GetBlock(below)) && !BlockRegistry.IsFluid(GetBlock(below)))
        {
            outwardFlow.Add(new LiquidFlow(below, 1, water));

            if (currentLevel != 0)
            {
                return outwardFlow.ToArray();
            }
        }

        var maxDepletion = water ? MaximumFluidDepletion : LavaMaximumFluidDepletion;
        if (currentLevel >= maxDepletion)
        {
            return outwardFlow.ToArray();
        }

        // Find the nearest drop-off.
        var candidateFlowPoints = new List<Coordinates3D>(4);
        var furthest = new Coordinates3D(DropCheckDistance + 1, DropCheckDistance + 1, 0) + Coordinates3D.Down;
        var nearestCandidate = furthest;

        for (var dx = -DropCheckDistance; dx <= DropCheckDistance; dx++)
        {
            for (var dz = -DropCheckDistance; dz <= DropCheckDistance; dz++)
            {
                if (Math.Abs(dx) + Math.Abs(dz) > DropCheckDistance)
                {
                    continue;
                }

                var check = new Coordinates3D(dx, 0, dz) + Coordinates3D.Down;
                var target = coords + check;

                if (!InBounds(target))
                {
                    continue;
                }

                var provider = GetBlock(target);

                if (!IsPassable(provider) || BlockRegistry.IsFluid(provider))
                {
                    continue;
                }

                if (!LineOfSight(coords + Coordinates3D.Up, target))
                {
                    continue;
                }

                var distance = Math.Abs(dx) + Math.Abs(dz);

                if (distance < Math.Abs(nearestCandidate.X) + Math.Abs(nearestCandidate.Z))
                {
                    candidateFlowPoints.Clear();
                    nearestCandidate = check;
                    candidateFlowPoints.Add(check);
                }
                else if (distance == Math.Abs(nearestCandidate.X) + Math.Abs(nearestCandidate.Z))
                {
                    candidateFlowPoints.Add(check);
                }
            }
        }

        if (nearestCandidate == furthest)
        {
            candidateFlowPoints.Add(new Coordinates3D(-DropCheckDistance - 1, 0, DropCheckDistance + 1) + Coordinates3D.Down);
            candidateFlowPoints.Add(new Coordinates3D(DropCheckDistance + 1, 0, -DropCheckDistance - 1) + Coordinates3D.Down);
            candidateFlowPoints.Add(new Coordinates3D(-DropCheckDistance - 1, 0, -DropCheckDistance - 1) + Coordinates3D.Down);
        }

        candidateFlowPoints.Add(nearestCandidate);

        foreach (var location in candidateFlowPoints)
        {
            var clamped = new Coordinates3D(
                Math.Clamp(location.X, -1, 1),
                0,
                Math.Clamp(location.Z, -1, 1));

            var xCheck = coords + new Coordinates3D(clamped.X, 0, 0);
            var zCheck = coords + new Coordinates3D(0, 0, clamped.Z);

            var xBlock = GetBlock(xCheck);
            var zBlock = GetBlock(zCheck);

            if (IsPassable(xBlock) && !BlockRegistry.IsFluid(xBlock))
            {
                if (!outwardFlow.Any(f => f.TargetBlock == xCheck))
                {
                    outwardFlow.Add(new LiquidFlow(xCheck, (byte)(currentLevel + 1), water));
                }
            }

            if (IsPassable(zBlock) && !BlockRegistry.IsFluid(zBlock))
            {
                if (!outwardFlow.Any(f => f.TargetBlock == zCheck))
                {
                    outwardFlow.Add(new LiquidFlow(zCheck, (byte)(currentLevel + 1), water));
                }
            }
        }

        // Occasionally the nearest candidate hole is not acceptable, but there
        // is space immediately next to the block.
        if (outwardFlow.Count == 0 && !BlockRegistry.IsFluid(GetBlock(below)))
        {
            foreach (var neighbor in Neighbors(coords))
            {
                var block = GetBlock(neighbor);

                if (IsPassable(block) && !BlockRegistry.IsFluid(block))
                {
                    outwardFlow.Add(new LiquidFlow(neighbor, (byte)(currentLevel + 1), water));
                }
            }
        }

        return outwardFlow.ToArray();
    }

    private void FlowOutward(List<BlockChange> changes, LiquidFlow flow)
    {
        if (!_world.InBounds(flow.TargetBlock.X, flow.TargetBlock.Y, flow.TargetBlock.Z))
        {
            return;
        }

        var targetBlock = GetBlock(flow.TargetBlock);

        // Water-lava interactions (Minecraft): water + lava source = obsidian,
        // lava + water = stone/cobblestone.
        if (BlockRegistry.IsFluid(targetBlock))
        {
            if (flow.Water && BlockRegistry.IsLava(targetBlock))
            {
                var result = targetBlock == BlockId.Lava ? BlockId.Obsidian : BlockId.Cobblestone;
                SetBlock(changes, flow.TargetBlock.X, flow.TargetBlock.Y, flow.TargetBlock.Z, result);
                ActivateNeighbors(flow.TargetBlock.X, flow.TargetBlock.Y, flow.TargetBlock.Z);
            }
            else if (!flow.Water && BlockRegistry.IsWater(targetBlock))
            {
                SetBlock(changes, flow.TargetBlock.X, flow.TargetBlock.Y, flow.TargetBlock.Z, BlockId.Stone);
                ActivateNeighbors(flow.TargetBlock.X, flow.TargetBlock.Y, flow.TargetBlock.Z);
            }

            return;
        }

        if (!IsPassable(targetBlock))
        {
            return;
        }

        var maxDepletion = flow.Water ? MaximumFluidDepletion : LavaMaximumFluidDepletion;
        var level = Math.Min((int)flow.Level, maxDepletion);
        var block = flow.Water
            ? BlockRegistry.WaterBlockForLevel(level)
            : BlockRegistry.LavaBlockForLevel(level);

        SetBlock(
            changes,
            flow.TargetBlock.X,
            flow.TargetBlock.Y,
            flow.TargetBlock.Z,
            block);

        Activate(flow.TargetBlock.X, flow.TargetBlock.Y, flow.TargetBlock.Z);
    }

    private void SetBlock(List<BlockChange> changes, int x, int y, int z, BlockId block)
    {
        if (!_world.InBounds(x, y, z))
        {
            return;
        }

        if (_world.GetBlock(x, y, z) == block)
        {
            return;
        }

        if (_world.SetBlock(x, y, z, block))
        {
            changes.Add(new BlockChange(x, y, z, block));
            ActivateNeighbors(x, y, z);
        }
    }

    private static BlockId LevelToBlock(int level)
    {
        if (level <= 0)
        {
            return BlockId.Water;
        }

        if (level > MaximumFluidDepletion)
        {
            return BlockId.Air;
        }

        return (BlockId)((int)BlockId.WaterFlow1 + level - 1);
    }

    private bool LineOfSight(Coordinates3D from, Coordinates3D to)
    {
        var candidate = from;
        var direction = to - from;
        direction.Clamp(1);

        do
        {
            var z = candidate.Z;

            do
            {
                var block = GetBlock(candidate);

                if (!IsPassable(block))
                {
                    return false;
                }

                candidate.Z += direction.Z;
            }
            while (to.Z != candidate.Z);

            candidate.Z = z;
            candidate.X += direction.X;
        }
        while (to.X != candidate.X);

        return true;
    }

    private BlockId GetBlock(Coordinates3D coords)
    {
        if (!_world.InBounds(coords.X, coords.Y, coords.Z))
        {
            return BlockId.Stone;
        }

        return _world.GetBlock(coords.X, coords.Y, coords.Z);
    }

    private bool InBounds(Coordinates3D coords) =>
        _world.InBounds(coords.X, coords.Y, coords.Z);

    private static bool IsPassable(BlockId block) =>
        block == BlockId.Air || BlockRegistry.IsPlant(block);

    private IEnumerable<Coordinates3D> Neighbors(Coordinates3D coords)
    {
        yield return coords + Coordinates3D.North;
        yield return coords + Coordinates3D.South;
        yield return coords + Coordinates3D.East;
        yield return coords + Coordinates3D.West;
    }

    private void Activate(int x, int y, int z)
    {
        if (!_world.InBounds(x, y, z))
        {
            return;
        }

        lock (_activeSync)
        {
            if (_active.Count >= MaximumActiveCells)
            {
                return;
            }

            _active.Add((x, y, z));
        }
    }

    private void ActivateNeighbors(int x, int y, int z)
    {
        Activate(x, y, z);

        Activate(x + 1, y, z);
        Activate(x - 1, y, z);
        Activate(x, y + 1, z);
        Activate(x, y - 1, z);
        Activate(x, y, z + 1);
        Activate(x, y, z - 1);
    }

    private void GrowPlants(List<BlockChange> changes)
    {
        lock (_worldSync)
        {
            for (var sample = 0; sample < PlantGrowthSamples; sample++)
            {
                var x = _random.Next(1, _world.SizeX - 1);
                var z = _random.Next(1, _world.SizeZ - 1);
                var y = _random.Next(1, _world.SizeY - 1);
                var block = _world.GetBlock(x, y, z);

                if (!BlockRegistry.IsCrop(block) || block == BlockId.Wheat7)
                {
                    continue;
                }

                if (_world.GetBlock(x, y + 1, z) != BlockId.Air)
                {
                    continue;
                }

                var below = _world.GetBlock(x, y - 1, z);

                if (below is not (BlockId.Grass or BlockId.Dirt or BlockId.Sand or BlockId.Farmland))
                {
                    continue;
                }

                if (_random.Next(100) >= 15)
                {
                    continue;
                }

                var next = BlockRegistry.NextPlantStage(block);
                SetBlock(changes, x, y, z, next);
            }
        }
    }

    private struct LiquidFlow
    {
        public LiquidFlow(Coordinates3D targetBlock, byte level, bool water)
        {
            TargetBlock = targetBlock;
            Level = level;
            Water = water;
        }

        public Coordinates3D TargetBlock;
        public byte Level;
        public bool Water;
    }

    private void TryIgnitePortal(List<BlockChange> changes, int x, int y, int z)
    {
        TryIgnitePortalFrame(changes, x, y, z, alongX: true);
        TryIgnitePortalFrame(changes, x, y, z, alongX: false);
    }

    private void TryIgnitePortalFrame(List<BlockChange> changes, int x, int y, int z, bool alongX)
    {
        var minOffset = 0;
        while (true)
        {
            var nx = alongX ? x + minOffset - 1 : x;
            var nz = alongX ? z : z + minOffset - 1;
            if (!_world.InBounds(nx, y, nz) || !IsPortalInterior(_world.GetBlock(nx, y, nz))) break;
            minOffset--;
            if (minOffset < -22) return;
        }

        var baseX = alongX ? x + minOffset : x;
        var baseZ = alongX ? z : z + minOffset;
        var baseY = y;
        while (baseY > 0 && _world.InBounds(baseX, baseY - 1, baseZ) &&
               IsPortalInterior(_world.GetBlock(baseX, baseY - 1, baseZ)))
        {
            baseY--;
        }

        for (var width = 2; width <= 21; width++)
        {
            for (var height = 3; height <= 21; height++)
            {
                if (IsValidPortalFrame(baseX, baseY, baseZ, width, height, alongX))
                {
                    FillPortalFrame(changes, baseX, baseY, baseZ, width, height, alongX);
                    return;
                }
            }
        }
    }

    private bool IsPortalInterior(BlockId block) =>
        block == BlockId.Air || block == BlockId.Fire || block == BlockId.NetherPortal;

    private bool IsValidPortalFrame(int x, int y, int z, int width, int height, bool alongX)
    {
        var hasFire = false;

        for (var dx = -1; dx <= width; dx++)
        {
            for (var dy = -1; dy <= height; dy++)
            {
                var px = alongX ? x + dx : x;
                var py = y + dy;
                var pz = alongX ? z : z + dx;

                if (!_world.InBounds(px, py, pz)) return false;

                var frame = dx == -1 || dx == width || dy == -1 || dy == height;
                var corner = (dx == -1 || dx == width) && (dy == -1 || dy == height);
                var block = _world.GetBlock(px, py, pz);
                if (frame)
                {
                    if (!corner && block != BlockId.Obsidian) return false;
                }
                else
                {
                    if (!IsPortalInterior(block)) return false;
                    if (block == BlockId.Fire) hasFire = true;
                }
            }
        }

        return hasFire;
    }

    private void FillPortalFrame(List<BlockChange> changes, int x, int y, int z, int width, int height, bool alongX)
    {
        for (var dx = 0; dx < width; dx++)
        {
            for (var dy = 0; dy < height; dy++)
            {
                var px = alongX ? x + dx : x;
                var py = y + dy;
                var pz = alongX ? z : z + dx;

                if (_world.GetBlock(px, py, pz) == BlockId.NetherPortal)
                {
                    continue;
                }

                if (_world.SetBlock(px, py, pz, BlockId.NetherPortal))
                {
                    changes.Add(new BlockChange(px, py, pz, BlockId.NetherPortal));
                    ActivateNeighbors(px, py, pz);
                }
            }
        }
    }

    private void BreakInvalidPortals(List<BlockChange> changes, int x, int y, int z)
    {
        Span<(int dx, int dy, int dz)> neighbors = stackalloc (int, int, int)[]
        {
            (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
        };

        for (var i = 0; i < neighbors.Length; i++)
        {
            var nx = x + neighbors[i].dx;
            var ny = y + neighbors[i].dy;
            var nz = z + neighbors[i].dz;
            if (!_world.InBounds(nx, ny, nz)) continue;
            if (_world.GetBlock(nx, ny, nz) != BlockId.NetherPortal) continue;

            CollapsePortalIfInvalid(changes, nx, ny, nz);
        }
    }

    private void CollapsePortalIfInvalid(List<BlockChange> changes, int x, int y, int z)
    {
        if (_world.GetBlock(x, y, z) != BlockId.NetherPortal)
        {
            return;
        }

        var hasX = _world.GetBlock(x + 1, y, z) == BlockId.NetherPortal ||
                   _world.GetBlock(x - 1, y, z) == BlockId.NetherPortal;
        var hasZ = _world.GetBlock(x, y, z + 1) == BlockId.NetherPortal ||
                   _world.GetBlock(x, y, z - 1) == BlockId.NetherPortal;

        var alongX = hasX || !hasZ;

        if (IsPortalRegionValid(x, y, z, alongX))
        {
            return;
        }

        var other = !alongX;
        if (IsPortalRegionValid(x, y, z, other))
        {
            return;
        }

        // Remove the detected orientation first; removing the wrong one would
        // delete only the start block and leave the real portal behind.
        RemovePortalRegion(changes, x, y, z, alongX);
        if (_world.InBounds(x, y, z) && _world.GetBlock(x, y, z) == BlockId.NetherPortal)
        {
            RemovePortalRegion(changes, x, y, z, other);
        }
    }

    private bool IsPortalRegionValid(int x, int y, int z, bool alongX)
    {
        if (!TryGetPortalBounds(x, y, z, alongX, out var minX, out var minY, out var minZ, out var maxX, out var maxY, out var maxZ))
        {
            return false;
        }

        var width = alongX ? maxX - minX + 1 : maxZ - minZ + 1;
        var height = maxY - minY + 1;
        if (width < 2 || height < 3 || width > 21 || height > 21)
        {
            return false;
        }

        for (var dx = -1; dx <= width; dx++)
        {
            for (var dy = -1; dy <= height; dy++)
            {
                var px = alongX ? minX + dx : minX;
                var py = minY + dy;
                var pz = alongX ? minZ : minZ + dx;
                if (!_world.InBounds(px, py, pz)) return false;

                var frame = dx == -1 || dx == width || dy == -1 || dy == height;
                var corner = (dx == -1 || dx == width) && (dy == -1 || dy == height);
                var block = _world.GetBlock(px, py, pz);
                if (frame)
                {
                    if (!corner && block != BlockId.Obsidian) return false;
                }
                else
                {
                    if (block != BlockId.NetherPortal) return false;
                }
            }
        }

        return true;
    }

    private bool TryGetPortalBounds(int x, int y, int z, bool alongX, out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ)
    {
        minX = maxX = x;
        minY = maxY = y;
        minZ = maxZ = z;

        if (_world.GetBlock(x, y, z) != BlockId.NetherPortal)
        {
            return false;
        }

        var queue = new Queue<(int x, int y, int z)>();
        var visited = new HashSet<(int x, int y, int z)>();
        queue.Enqueue((x, y, z));
        visited.Add((x, y, z));

        while (queue.Count > 0)
        {
            var (cx, cy, cz) = queue.Dequeue();
            minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
            minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
            minZ = Math.Min(minZ, cz); maxZ = Math.Max(maxZ, cz);

            var dirs = alongX
                ? new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0) }
                : new[] { (0, 0, 1), (0, 0, -1), (0, 1, 0), (0, -1, 0) };

            foreach (var (dx, dy, dz) in dirs)
            {
                var n = (cx + dx, cy + dy, cz + dz);
                if (visited.Contains(n)) continue;
                if (!_world.InBounds(n.Item1, n.Item2, n.Item3)) continue;
                if (_world.GetBlock(n.Item1, n.Item2, n.Item3) != BlockId.NetherPortal) continue;
                visited.Add(n);
                queue.Enqueue(n);
            }
        }

        // For an alongX portal, Z should be constant; for alongZ, X should be constant.
        if (alongX && minZ != maxZ) return false;
        if (!alongX && minX != maxX) return false;
        return true;
    }

    private void RemovePortalRegion(List<BlockChange> changes, int x, int y, int z, bool alongX)
    {
        if (_world.GetBlock(x, y, z) != BlockId.NetherPortal)
        {
            return;
        }

        var queue = new Queue<(int x, int y, int z)>();
        var visited = new HashSet<(int x, int y, int z)>();
        queue.Enqueue((x, y, z));
        visited.Add((x, y, z));

        while (queue.Count > 0)
        {
            var (cx, cy, cz) = queue.Dequeue();
            if (_world.SetBlock(cx, cy, cz, BlockId.Air))
            {
                changes.Add(new BlockChange(cx, cy, cz, BlockId.Air));
                ActivateNeighbors(cx, cy, cz);
            }

            var dirs = alongX
                ? new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0) }
                : new[] { (0, 0, 1), (0, 0, -1), (0, 1, 0), (0, -1, 0) };

            foreach (var (dx, dy, dz) in dirs)
            {
                var n = (cx + dx, cy + dy, cz + dz);
                if (visited.Contains(n)) continue;
                if (!_world.InBounds(n.Item1, n.Item2, n.Item3)) continue;
                if (_world.GetBlock(n.Item1, n.Item2, n.Item3) != BlockId.NetherPortal) continue;
                visited.Add(n);
                queue.Enqueue(n);
            }
        }
    }

    private struct Coordinates3D
    {
        public Coordinates3D(int x, int y = 0, int z = 0)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

        public static Coordinates3D Down => new(0, -1, 0);
        public static Coordinates3D Up => new(0, 1, 0);
        public static Coordinates3D North => new(0, 0, -1);
        public static Coordinates3D South => new(0, 0, 1);
        public static Coordinates3D East => new(1, 0, 0);
        public static Coordinates3D West => new(-1, 0, 0);

        public static Coordinates3D operator +(Coordinates3D a, Coordinates3D b) =>
            new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Coordinates3D operator -(Coordinates3D a, Coordinates3D b) =>
            new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public void Clamp(int value)
        {
            X = Math.Sign(X) * Math.Min(Math.Abs(X), value);
            Y = Math.Sign(Y) * Math.Min(Math.Abs(Y), value);
            Z = Math.Sign(Z) * Math.Min(Math.Abs(Z), value);
        }

        public static bool operator ==(Coordinates3D left, Coordinates3D right) =>
            left.X == right.X && left.Y == right.Y && left.Z == right.Z;

        public static bool operator !=(Coordinates3D left, Coordinates3D right) =>
            !(left == right);

        public override bool Equals(object? obj) =>
            obj is Coordinates3D other && this == other;

        public override int GetHashCode() =>
            HashCode.Combine(X, Y, Z);
    }
}
