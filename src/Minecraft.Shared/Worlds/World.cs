using Minecraft.Shared.Blocks;

namespace Minecraft.Shared.Worlds;

/// <summary>
/// A fixed-size block world. The server owns the authoritative copy;
/// the client can use the same type for rendering and prediction.
/// </summary>
public sealed class World
{
    public const int DefaultHeight = 128;

    private readonly BlockId[,,] _blocks;
    private readonly byte[] _skyLight;
    private readonly byte[] _blockLight;

    public World(int sizeX, int sizeY, int sizeZ)
    {
        if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX));
        if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY));
        if (sizeZ <= 0) throw new ArgumentOutOfRangeException(nameof(sizeZ));

        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        _blocks = new BlockId[sizeX, sizeY, sizeZ];
        _skyLight = new byte[sizeX * sizeY * sizeZ];
        _blockLight = new byte[sizeX * sizeY * sizeZ];
    }

    public int SizeX { get; }

    public int SizeY { get; }

    public int SizeZ { get; }

    public bool InBounds(int x, int y, int z) =>
        x >= 0 && x < SizeX &&
        y >= 0 && y < SizeY &&
        z >= 0 && z < SizeZ;

    /// <summary>Returns out-of-bounds coordinates as air instead of throwing.</summary>
    public BlockId GetBlock(int x, int y, int z) =>
        InBounds(x, y, z) ? _blocks[x, y, z] : BlockId.Air;

    /// <summary>Places a block. Returns false when the coordinates are out of bounds.</summary>
    public bool SetBlock(int x, int y, int z, BlockId id)
    {
        if (!InBounds(x, y, z))
        {
            return false;
        }

        _blocks[x, y, z] = id;
        return true;
    }

    /// <summary>Combined light level (0-15) used for rendering, or 0 out of bounds.</summary>
    public byte GetLight(int x, int y, int z)
    {
        if (!InBounds(x, y, z))
        {
            return 0;
        }

        var index = (x * SizeZ + z) * SizeY + y;
        var sky = _skyLight[index];
        var block = _blockLight[index];
        return sky >= block ? sky : block;
    }

    /// <summary>Sky light level (0-15) at a cell, or 0 out of bounds.</summary>
    public byte GetSkyLight(int x, int y, int z) =>
        InBounds(x, y, z) ? _skyLight[(x * SizeZ + z) * SizeY + y] : (byte)0;

    /// <summary>Block light level (0-15) at a cell, or 0 out of bounds.</summary>
    public byte GetBlockLight(int x, int y, int z) =>
        InBounds(x, y, z) ? _blockLight[(x * SizeZ + z) * SizeY + y] : (byte)0;

    /// <summary>Sets the sky light level at a cell.</summary>
    public void SetSkyLight(int x, int y, int z, byte value)
    {
        if (InBounds(x, y, z))
        {
            _skyLight[(x * SizeZ + z) * SizeY + y] = value;
        }
    }

    /// <summary>Sets the block light level at a cell.</summary>
    public void SetBlockLight(int x, int y, int z, byte value)
    {
        if (InBounds(x, y, z))
        {
            _blockLight[(x * SizeZ + z) * SizeY + y] = value;
        }
    }

    /// <summary>Clears every light value, used before a full relight.</summary>
    public void ClearLight()
    {
        Array.Clear(_skyLight);
        Array.Clear(_blockLight);
    }

    public void Fill(BlockId id)
    {
        for (var x = 0; x < SizeX; x++)
        {
            for (var y = 0; y < SizeY; y++)
            {
                for (var z = 0; z < SizeZ; z++)
                {
                    _blocks[x, y, z] = id;
                }
            }
        }
    }

    /// <summary>Counts how many blocks of each type exist in the world.</summary>
    public IReadOnlyDictionary<BlockId, int> CountBlocks()
    {
        var counts = new Dictionary<BlockId, int>();

        foreach (var block in _blocks)
        {
            counts[block] = counts.GetValueOrDefault(block) + 1;
        }

        return counts;
    }
}
