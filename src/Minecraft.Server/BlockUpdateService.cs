using System.Collections.Concurrent;
using System.Diagnostics;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;

namespace LetsCreateNetworkGame.Server;

/// <summary>
/// Runs block simulation on a dedicated thread: queued block edits, water flow,
/// and plant growth. Applying edits through this service keeps the network loop
/// and console commands from blocking on world mutation.
/// </summary>
public sealed class BlockUpdateService : IDisposable
{
    private const int TicksPerSecond = 10;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1.0 / TicksPerSecond);
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
        UpdateWater(changes);

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

    private void UpdateWater(List<BlockChange> changes)
    {
        lock (_worldSync)
        {
            var sizeX = _world.SizeX;
            var sizeY = _world.SizeY;
            var sizeZ = _world.SizeZ;

            // Snapshot the water that exists right now. New water created this
            // tick is not propagated until the next tick, which gives the flow
            // its cellular-automaton, one-cell-per-tick character.
            var sources = new List<(int X, int Y, int Z)>();
            var flows = new List<(int X, int Y, int Z, int Level)>();

            for (var x = 0; x < sizeX; x++)
            for (var y = 0; y < sizeY; y++)
            for (var z = 0; z < sizeZ; z++)
            {
                var block = _world.GetBlock(x, y, z);

                if (block == BlockId.Water)
                {
                    sources.Add((x, y, z));
                }
                else if (block == BlockId.WaterFalling)
                {
                    flows.Add((x, y, z, 0));
                }
                else if (BlockRegistry.IsWaterFlow(block))
                {
                    flows.Add((x, y, z, BlockRegistry.WaterLevel(block)));
                }
            }

            // Collect candidate placements, keeping the strongest (lowest) level
            // when several sources compete for the same cell.
            var placementLevels = new Dictionary<(int X, int Y, int Z), (int Level, bool Falling)>();

            foreach (var (x, y, z) in sources)
            {
                TrySpreadWater(x, y, z, 0, sizeX, sizeY, sizeZ, placementLevels);
            }

            foreach (var (x, y, z, level) in flows)
            {
                TrySpreadWater(x, y, z, level, sizeX, sizeY, sizeZ, placementLevels);
            }

            foreach (var (cell, info) in placementLevels)
            {
                var block = info.Falling
                    ? BlockId.WaterFalling
                    : BlockRegistry.WaterBlockForLevel(info.Level);

                if (_world.SetBlock(cell.X, cell.Y, cell.Z, block))
                {
                    changes.Add(new BlockChange(cell.X, cell.Y, cell.Z, block));
                }
            }

            // Decay: a flow cell survives only while it has a valid inflow.
            // Removing invalid cells cascades over subsequent ticks.
            var removals = new List<(int X, int Y, int Z)>();

            for (var x = 0; x < sizeX; x++)
            for (var y = 0; y < sizeY; y++)
            for (var z = 0; z < sizeZ; z++)
            {
                var block = _world.GetBlock(x, y, z);

                if (!BlockRegistry.IsWaterFlow(block) && block != BlockId.WaterFalling)
                {
                    continue;
                }

                if (!HasWaterInflow(x, y, z, block, sizeX, sizeY, sizeZ))
                {
                    removals.Add((x, y, z));
                }
            }

            foreach (var (x, y, z) in removals)
            {
                if (_world.SetBlock(x, y, z, BlockId.Air))
                {
                    changes.Add(new BlockChange(x, y, z, BlockId.Air));
                }
            }
        }
    }

    private void TrySpreadWater(
        int x,
        int y,
        int z,
        int level,
        int sizeX,
        int sizeY,
        int sizeZ,
        Dictionary<(int X, int Y, int Z), (int Level, bool Falling)> placements)
    {
        // Water always tries to fall first. Falling water resets to level 0.
        TryPlaceWater(x, y - 1, z, 0, true, sizeX, sizeY, sizeZ, placements);

        // Horizontal spread only happens when the cell below is solid or water,
        // so water never floats in mid air. Each step weakens the flow.
        if (level < 7)
        {
            TryPlaceWater(x + 1, y, z, level + 1, false, sizeX, sizeY, sizeZ, placements);
            TryPlaceWater(x - 1, y, z, level + 1, false, sizeX, sizeY, sizeZ, placements);
            TryPlaceWater(x, y, z + 1, level + 1, false, sizeX, sizeY, sizeZ, placements);
            TryPlaceWater(x, y, z - 1, level + 1, false, sizeX, sizeY, sizeZ, placements);
        }
    }

    private void TryPlaceWater(
        int x,
        int y,
        int z,
        int newLevel,
        bool falling,
        int sizeX,
        int sizeY,
        int sizeZ,
        Dictionary<(int X, int Y, int Z), (int Level, bool Falling)> placements)
    {
        if (x < 0 || y < 0 || z < 0 || x >= sizeX || y >= sizeY || z >= sizeZ)
        {
            return;
        }

        var current = _world.GetBlock(x, y, z);

        if (current == BlockId.Water)
        {
            return;
        }

        // Only air and plants can be replaced by flowing water. Existing flow
        // can be upgraded to a stronger (lower) level.
        if (current != BlockId.Air && !BlockRegistry.IsPlant(current))
        {
            if (!BlockRegistry.IsWaterFlow(current) && current != BlockId.WaterFalling)
            {
                return;
            }

            if (BlockRegistry.WaterLevel(current) <= newLevel)
            {
                return;
            }
        }

        // Horizontal flow needs support below it.
        if (!falling && newLevel > 0)
        {
            if (y == 0)
            {
                return;
            }

            var below = _world.GetBlock(x, y - 1, z);

            if (!BlockRegistry.Get(below).IsSolid && !BlockRegistry.IsWater(below))
            {
                return;
            }
        }

        var key = (x, y, z);

        if (placements.TryGetValue(key, out var existing) && existing.Level <= newLevel && existing.Falling == falling)
        {
            return;
        }

        placements[key] = (newLevel, falling);
    }

    private bool HasWaterInflow(
        int x,
        int y,
        int z,
        BlockId block,
        int sizeX,
        int sizeY,
        int sizeZ)
    {
        var level = BlockRegistry.WaterLevel(block);

        // A stronger neighbor can feed this cell.
        if (x > 0)
        {
            var neighbor = _world.GetBlock(x - 1, y, z);
            if (neighbor == BlockId.Water || (BlockRegistry.IsWaterFlow(neighbor) && BlockRegistry.WaterLevel(neighbor) < level))
            {
                return true;
            }
        }

        if (x < sizeX - 1)
        {
            var neighbor = _world.GetBlock(x + 1, y, z);
            if (neighbor == BlockId.Water || (BlockRegistry.IsWaterFlow(neighbor) && BlockRegistry.WaterLevel(neighbor) < level))
            {
                return true;
            }
        }

        if (z > 0)
        {
            var neighbor = _world.GetBlock(x, y, z - 1);
            if (neighbor == BlockId.Water || (BlockRegistry.IsWaterFlow(neighbor) && BlockRegistry.WaterLevel(neighbor) < level))
            {
                return true;
            }
        }

        if (z < sizeZ - 1)
        {
            var neighbor = _world.GetBlock(x, y, z + 1);
            if (neighbor == BlockId.Water || (BlockRegistry.IsWaterFlow(neighbor) && BlockRegistry.WaterLevel(neighbor) < level))
            {
                return true;
            }
        }

        // Water falling from above feeds this cell.
        if (y < sizeY - 1 && BlockRegistry.IsWater(_world.GetBlock(x, y + 1, z)))
        {
            return true;
        }

        return false;
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
}
