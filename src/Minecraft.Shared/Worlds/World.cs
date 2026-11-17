using Minecraft.Shared.Blocks;

namespace Minecraft.Shared.Worlds;

/// <summary>
/// A fixed-size block world. The server owns the authoritative copy;
/// the client can use the same type for rendering and prediction.
/// </summary>
public sealed class World
{
    public const int DefaultHeight = 64;

    private readonly BlockId[,,] _blocks;

    public World(int sizeX, int sizeY, int sizeZ)
    {
        if (sizeX <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX));
        if (sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeY));
        if (sizeZ <= 0) throw new ArgumentOutOfRangeException(nameof(sizeZ));

        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        _blocks = new BlockId[sizeX, sizeY, sizeZ];
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
