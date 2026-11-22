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
/// </summary>
public sealed class BlockUpdateService : IDisposable
{
    private const int TicksPerSecond = 10;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1.0 / TicksPerSecond);
    private const int MaximumFluidDepletion = 7;
    private const int DropCheckDistance = 2;
    private const int PlantGrowthIntervalTicks = 20;

    private readonly World _world;
    private readonly object _worldSync;
    private readonly Action<BlockChange> _broadcast;
    private readonly Action<string> _log;
    private readonly ConcurrentQueue<BlockChange> _edits = new();
    private readonly Thread _thread;
    private volatile bool _running;
    private int _tickCounter;
    private readonly Random _random = new();

    public BlockUpdateService(
        World world,
        object worldSync,
        Action<BlockChange> broadcast,
        Action<string> log)
    {
        _world = world;
        _worldSync = worldSync;
        _broadcast = broadcast;
        _log = log;
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

        // Snapshot water cells so a block updated this tick is not processed
        // again until the next tick.
        var waterCells = new List<(int X, int Y, int Z)>();

        lock (_worldSync)
        {
            for (var x = 0; x < _world.SizeX; x++)
            for (var y = 0; y < _world.SizeY; y++)
            for (var z = 0; z < _world.SizeZ; z++)
            {
                if (BlockRegistry.IsWater(_world.GetBlock(x, y, z)))
                {
                    waterCells.Add((x, y, z));
                }
            }
        }

        foreach (var (x, y, z) in waterCells)
        {
            DoAutomata(changes, x, y, z);
        }

        if (_tickCounter % PlantGrowthIntervalTicks == 0)
        {
            UpdatePlants(changes);
        }

        foreach (var change in changes)
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

                if (_world.GetBlock(change.X, change.Y, change.Z) == change.Block)
                {
                    continue;
                }

                if (_world.SetBlock(change.X, change.Y, change.Z, change.Block))
                {
                    changes.Add(change);
                }
            }
        }
    }

    private void DoAutomata(List<BlockChange> changes, int x, int y, int z)
    {
        lock (_worldSync)
        {
            if (!_world.InBounds(x, y, z))
            {
                return;
            }

            var current = _world.GetBlock(x, y, z);

            if (!BlockRegistry.IsWater(current))
            {
                return;
            }

            var coords = new Coordinates3D(x, y, z);
            var previousLevel = BlockRegistry.WaterLevel(current);
            if (previousLevel < 0)
            {
                previousLevel = 0;
            }

            var inward = DetermineInwardFlow(coords);
            var outward = DetermineOutwardFlow(coords, previousLevel);

            // Flowing down takes priority for non-source water.
            if (outward.Length == 1 &&
                outward[0].TargetBlock == coords + Coordinates3D.Down &&
                previousLevel != 0)
            {
                FlowOutward(changes, outward[0]);
                return;
            }

            // Inward flow.
            if (inward > MaximumFluidDepletion)
            {
                if (_world.SetBlock(x, y, z, BlockId.Air))
                {
                    changes.Add(new BlockChange(x, y, z, BlockId.Air));
                }

                return;
            }

            if (inward != previousLevel)
            {
                SetWaterLevel(coords, inward);

                if (inward == 0 && previousLevel != 0)
                {
                    // Became a source block.
                    return;
                }
            }

            // Outward flow.
            foreach (var flow in outward)
            {
                FlowOutward(changes, flow);
            }
        }
    }

    private byte DetermineInwardFlow(Coordinates3D coords)
    {
        var currentLevel = BlockRegistry.WaterLevel(_world.GetBlock(coords.X, coords.Y, coords.Z));
        if (currentLevel < 0)
        {
            currentLevel = 0;
        }

        var upBlock = GetBlock(coords + Coordinates3D.Up);

        if (BlockRegistry.IsWater(upBlock))
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

            if (!BlockRegistry.IsWater(neighborBlock))
            {
                continue;
            }

            var neighborLevel = BlockRegistry.WaterLevel(neighborBlock);
            if (neighborLevel < 0)
            {
                neighborLevel = 0;
            }

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

        var below = coords + Coordinates3D.Down;

        if (InBounds(below) && IsPassable(GetBlock(below)) && !BlockRegistry.IsWater(GetBlock(below)))
        {
            outwardFlow.Add(new LiquidFlow(below, 1));

            if (currentLevel != 0)
            {
                return outwardFlow.ToArray();
            }
        }

        if (currentLevel >= MaximumFluidDepletion)
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

                if (!IsPassable(provider) || BlockRegistry.IsWater(provider))
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

            if (IsPassable(xBlock) && !BlockRegistry.IsWater(xBlock))
            {
                if (!outwardFlow.Any(f => f.TargetBlock == xCheck))
                {
                    outwardFlow.Add(new LiquidFlow(xCheck, (byte)(currentLevel + 1)));
                }
            }

            if (IsPassable(zBlock) && !BlockRegistry.IsWater(zBlock))
            {
                if (!outwardFlow.Any(f => f.TargetBlock == zCheck))
                {
                    outwardFlow.Add(new LiquidFlow(zCheck, (byte)(currentLevel + 1)));
                }
            }
        }

        // Occasionally there are scenarios where the nearest candidate hole is
        // not acceptable, but there is space immediately next to the block.
        if (outwardFlow.Count == 0 && !BlockRegistry.IsWater(GetBlock(below)))
        {
            foreach (var neighbor in Neighbors(coords))
            {
                var block = GetBlock(neighbor);

                if (IsPassable(block) && !BlockRegistry.IsWater(block))
                {
                    outwardFlow.Add(new LiquidFlow(neighbor, (byte)(currentLevel + 1)));
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

        if (BlockRegistry.IsWater(targetBlock))
        {
            return;
        }

        if (!IsPassable(targetBlock))
        {
            return;
        }

        var level = flow.Level;

        if (level > MaximumFluidDepletion)
        {
            level = MaximumFluidDepletion;
        }

        SetWaterLevel(flow.TargetBlock, level);

        changes.Add(new BlockChange(
            flow.TargetBlock.X,
            flow.TargetBlock.Y,
            flow.TargetBlock.Z,
            LevelToBlock(level)));
    }

    private void SetWaterLevel(Coordinates3D coords, int level)
    {
        if (level <= 0)
        {
            _world.SetBlock(coords.X, coords.Y, coords.Z, BlockId.Water);
        }
        else if (level <= MaximumFluidDepletion)
        {
            _world.SetBlock(coords.X, coords.Y, coords.Z, (BlockId)((int)BlockId.WaterFlow1 + level - 1));
        }
        else
        {
            _world.SetBlock(coords.X, coords.Y, coords.Z, BlockId.Air);
        }
    }

    private BlockId LevelToBlock(int level)
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

    private bool InBounds(Coordinates3D coords)
    {
        return _world.InBounds(coords.X, coords.Y, coords.Z);
    }

    private static bool IsPassable(BlockId block)
    {
        return block == BlockId.Air || BlockRegistry.IsPlant(block);
    }

    private IEnumerable<Coordinates3D> Neighbors(Coordinates3D coords)
    {
        yield return coords + Coordinates3D.North;
        yield return coords + Coordinates3D.South;
        yield return coords + Coordinates3D.East;
        yield return coords + Coordinates3D.West;
    }

    private void UpdatePlants(List<BlockChange> changes)
    {
        lock (_worldSync)
        {
            for (var x = 0; x < _world.SizeX; x++)
            for (var y = 1; y < _world.SizeY - 1; y++)
            for (var z = 0; z < _world.SizeZ; z++)
            {
                var block = _world.GetBlock(x, y, z);

                if (!BlockRegistry.IsPlant(block) || block == BlockId.Wheat7)
                {
                    continue;
                }

                if (_world.GetBlock(x, y + 1, z) != BlockId.Air)
                {
                    continue;
                }

                var below = _world.GetBlock(x, y - 1, z);

                if (below != BlockId.Grass &&
                    below != BlockId.Dirt &&
                    below != BlockId.Sand)
                {
                    continue;
                }

                if (_random.Next(100) >= 15)
                {
                    continue;
                }

                var next = BlockRegistry.NextPlantStage(block);

                if (next == block)
                {
                    continue;
                }

                if (_world.SetBlock(x, y, z, next))
                {
                    changes.Add(new BlockChange(x, y, z, next));
                }
            }
        }
    }

    private struct LiquidFlow
    {
        public LiquidFlow(Coordinates3D targetBlock, byte level)
        {
            TargetBlock = targetBlock;
            Level = level;
        }

        public Coordinates3D TargetBlock;
        public byte Level;
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
        }

        public int DistanceTo(Coordinates3D other) =>
            Math.Abs(X - other.X) + Math.Abs(Y - other.Y) + Math.Abs(Z - other.Z);

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
