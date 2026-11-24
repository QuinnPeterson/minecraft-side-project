using System.Collections.Generic;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Net;

namespace Minecraft.Shared.Worlds;

/// <summary>
/// Incremental per-block lighting. Sky light floods down open columns and
/// spreads sideways under overhangs; block light comes from torches,
/// glowstone, jack-o-lanterns and lava. Both are stored in <see cref="World"/>
/// and consumed by the chunk mesher.
///
/// A full pass is only done once when the world arrives. Afterwards a block
/// edit removes the light it used to provide and re-adds light from the
/// surrounding cells, so only the affected area is touched.
/// </summary>
public sealed class LightingEngine
{
    private const byte MaxLight = 15;

    private static readonly (int X, int Y, int Z)[] Directions =
    {
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
    };

    private readonly World _world;
    private readonly Queue<LightNode> _addQueue = new();
    private readonly Queue<LightNode> _removeQueue = new();
    private readonly HashSet<(int X, int Z)> _changedChunks = new();
    private float _currentTimeOfDay;

    private struct LightNode
    {
        public int X;
        public int Y;
        public int Z;
        public byte Level;
    }

    public LightingEngine(World world) => _world = world;

    /// <summary>Chunk columns whose light changed since the last clear.</summary>
    public IReadOnlyCollection<(int X, int Z)> ChangedChunks => _changedChunks;

    public void ClearChangedChunks() => _changedChunks.Clear();

    /// <summary>Recomputes every light source in the world at a given time of day.</summary>
    public void Initialize(float timeOfDay)
    {
        _world.ClearLight();
        _changedChunks.Clear();
        _addQueue.Clear();
        _removeQueue.Clear();
        _currentTimeOfDay = timeOfDay;

        // Sky light: flood each column from the top down until the first opaque
        // block. The surface block itself is lit so its top face is bright. The
        // sky contribution fades into night.
        var skyLevel = SkyLightForTime(timeOfDay);

        for (var x = 0; x < _world.SizeX; x++)
        {
            for (var z = 0; z < _world.SizeZ; z++)
            {
                var columnSky = SkyLightForColumn(x, z);
                var attenuation = 0;
                var open = true;

                for (var y = _world.SizeY - 1; y >= 0 && open; y--)
                {
                    var block = _world.GetBlock(x, y, z);

                    if (BlockRegistry.IsOpaque(block))
                    {
                        if (columnSky > 0)
                        {
                            _world.SetSkyLight(x, y, z, (byte)Math.Max(0, columnSky - attenuation));
                        }

                        open = false;
                        continue;
                    }

                    // Leaves filter daylight, so the ground under a canopy is
                    // dimmer than the surrounding surface.
                    attenuation += BlockRegistry.SkyAttenuation(block);

                    if (columnSky > 0)
                    {
                        _world.SetSkyLight(x, y, z, (byte)Math.Max(0, columnSky - attenuation));
                    }
                }
            }
        }

        // Only cells that are transparent but still unlit need horizontal
        // light (cave mouths and the space under overhangs). Seeding from their
        // lit neighbours is far cheaper than queueing every open-air cell.
        for (var x = 0; x < _world.SizeX; x++)
        {
            for (var z = 0; z < _world.SizeZ; z++)
            {
                for (var y = 0; y < _world.SizeY; y++)
                {
                    if (_world.GetSkyLight(x, y, z) != 0 ||
                        BlockRegistry.IsOpaque(_world.GetBlock(x, y, z)))
                    {
                        continue;
                    }

                    SeedNeighbours(true, x, y, z);
                }
            }
        }

        Propagate(true, skyLevel);

        // Block light sources.
        for (var x = 0; x < _world.SizeX; x++)
        {
            for (var y = 0; y < _world.SizeY; y++)
            {
                for (var z = 0; z < _world.SizeZ; z++)
                {
                    var emission = BlockRegistry.LightEmission(_world.GetBlock(x, y, z));

                    if (emission == 0)
                    {
                        continue;
                    }

                    _world.SetBlockLight(x, y, z, emission);
                    _addQueue.Enqueue(new LightNode { X = x, Y = y, Z = z, Level = emission });
                }
            }
        }

        Propagate(false, 15);
    }

    /// <summary>
    /// Sky light level for a time of day. One full day is twelve in-game hours
    /// long, which drives the day/night cycle and the sun/moon skybox.
    /// </summary>
    private static byte SkyLightForTime(float timeOfDay)
    {
        var hours = Math.Clamp(timeOfDay, 0f, 24f);
        return (byte)(MaxLight * Daylight.Level(hours));
    }

    /// <summary>
    /// Sky light available in a column. The outermost columns are dimmer: the
    /// open edge of the world is not lit by surrounding terrain, so caves and
    /// tunnels dug into the side are properly dark.
    /// </summary>
    private byte SkyLightForColumn(int x, int z)
    {
        var sky = SkyLightForTime(_currentTimeOfDay);

        if (sky == 0)
        {
            return 0;
        }

        var edge = Math.Min(
            Math.Min(x, _world.SizeX - 1 - x),
            Math.Min(z, _world.SizeZ - 1 - z));
        var reduction = edge == 0 ? 3 : edge == 1 ? 1 : 0;
        return (byte)Math.Max(0, sky - reduction);
    }

    /// <summary>Recomputes every light source in the world at a given time of day.</summary>
    public void Compute(float timeOfDay)
    {
        Initialize(timeOfDay);
    }

    /// <summary>Updates the light affected by a single block edit.</summary>
    public void OnBlockChanged(int x, int y, int z, BlockId oldBlock, BlockId newBlock)
    {
        if (!_world.InBounds(x, y, z))
        {
            return;
        }

        var newOpaque = BlockRegistry.IsOpaque(newBlock);
        var emission = BlockRegistry.LightEmission(newBlock);
        var oldEmission = BlockRegistry.LightEmission(oldBlock);
        var newAttenuation = BlockRegistry.SkyAttenuation(newBlock);
        var oldAttenuation = BlockRegistry.SkyAttenuation(oldBlock);
        var columnSky = SkyLightForColumn(x, z);

        // Sky light only depends on opacity and filtering. Removing it from a
        // cell that stays transparent would needlessly darken the column below,
        // so only drop it when the block actually blocks or filters more sky.
        if (newOpaque || oldAttenuation != newAttenuation)
        {
            RemoveLight(true, x, y, z);
        }

        if (!newOpaque)
        {
            if (y == _world.SizeY - 1 ||
                (y > 0 && _world.GetSkyLight(x, y - 1, z) == columnSky))
            {
                // The cell above is fully sky-lit, so this cell continues the
                // shaft all the way down to bedrock without losing intensity.
                SetSource(true, x, y, z, columnSky);
            }
            else
            {
                SeedNeighbours(true, x, y, z);
            }
        }

        Propagate(true, SkyLightForTime(_currentTimeOfDay));

        // Block light: drop the old emitter and any light trapped inside a
        // newly opaque block, then let the neighbours refill the cell.
        if (newOpaque || oldEmission > 0)
        {
            RemoveLight(false, x, y, z);
        }

        if (emission > 0)
        {
            SetSource(false, x, y, z, emission);
        }

        if (!newOpaque)
        {
            SeedNeighbours(false, x, y, z);
        }

        Propagate(false, 15);
    }

    private void RemoveLight(bool sky, int x, int y, int z)
    {
        var level = GetLight(sky, x, y, z);

        if (level == 0)
        {
            return;
        }

        SetLight(sky, x, y, z, 0);
        MarkChanged(x, z);

        _removeQueue.Clear();
        _removeQueue.Enqueue(new LightNode { X = x, Y = y, Z = z, Level = level });

        while (_removeQueue.Count > 0)
        {
            var node = _removeQueue.Dequeue();

            foreach (var (dx, dy, dz) in Directions)
            {
                var nx = node.X + dx;
                var ny = node.Y + dy;
                var nz = node.Z + dz;

                if (!_world.InBounds(nx, ny, nz))
                {
                    continue;
                }

                var neighbour = GetLight(sky, nx, ny, nz);

                if (neighbour == 0)
                {
                    continue;
                }

                // A cell carries our light when it is dimmer than us, or when
                // it is the open-air cell underneath a full sky-light column.
                var child = neighbour < node.Level ||
                            (sky && dy == -1 && neighbour == node.Level &&
                             node.Level == SkyLightForColumn(nx, nz));

                if (child)
                {
                    SetLight(sky, nx, ny, nz, 0);
                    MarkChanged(nx, nz);
                    _removeQueue.Enqueue(new LightNode { X = nx, Y = ny, Z = nz, Level = neighbour });
                }
                else
                {
                    // This cell is lit by something else, so it can refill the
                    // hole we just carved.
                    SeedExisting(sky, nx, ny, nz);
                }
            }
        }
    }

    private void SeedNeighbours(bool sky, int x, int y, int z)
    {
        foreach (var (dx, dy, dz) in Directions)
        {
            SeedExisting(sky, x + dx, y + dy, z + dz);
        }
    }

    private void SeedExisting(bool sky, int x, int y, int z)
    {
        if (!_world.InBounds(x, y, z) ||
            BlockRegistry.IsOpaque(_world.GetBlock(x, y, z)))
        {
            return;
        }

        var level = GetLight(sky, x, y, z);

        if (level > 0)
        {
            _addQueue.Enqueue(new LightNode { X = x, Y = y, Z = z, Level = level });
        }
    }

    private void SetSource(bool sky, int x, int y, int z, byte level)
    {
        if (level <= GetLight(sky, x, y, z))
        {
            return;
        }

        SetLight(sky, x, y, z, level);
        MarkChanged(x, z);
        _addQueue.Enqueue(new LightNode { X = x, Y = y, Z = z, Level = level });
    }

    private void Propagate(bool sky, byte skyLevel)
    {
        while (_addQueue.Count > 0)
        {
            var node = _addQueue.Dequeue();

            if (node.Level <= 1)
            {
                continue;
            }

            foreach (var (dx, dy, dz) in Directions)
            {
                var nx = node.X + dx;
                var ny = node.Y + dy;
                var nz = node.Z + dz;

                if (!_world.InBounds(nx, ny, nz) ||
                    BlockRegistry.IsOpaque(_world.GetBlock(nx, ny, nz)))
                {
                    continue;
                }

                // Sky light travels straight down an open column without losing
                // a level; leaves filter it, and every other step dims by one.
                var attenuation = sky ? BlockRegistry.SkyAttenuation(_world.GetBlock(nx, ny, nz)) : (byte)0;
                var level = sky && dy == -1 && attenuation == 0 && node.Level == SkyLightForColumn(nx, nz)
                    ? node.Level
                    : (byte)Math.Max(0, node.Level - Math.Max((byte)1, attenuation));

                if (level <= GetLight(sky, nx, ny, nz))
                {
                    continue;
                }

                SetLight(sky, nx, ny, nz, level);
                MarkChanged(nx, nz);
                _addQueue.Enqueue(new LightNode { X = nx, Y = ny, Z = nz, Level = level });
            }
        }
    }

    private byte GetLight(bool sky, int x, int y, int z) =>
        sky ? _world.GetSkyLight(x, y, z) : _world.GetBlockLight(x, y, z);

    private void SetLight(bool sky, int x, int y, int z, byte level)
    {
        if (sky)
        {
            _world.SetSkyLight(x, y, z, level);
        }
        else
        {
            _world.SetBlockLight(x, y, z, level);
        }
    }

    private void MarkChanged(int x, int z) =>
        _changedChunks.Add((x / WorldProtocol.ChunkSize, z / WorldProtocol.ChunkSize));
}
