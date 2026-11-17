using Minecraft.Shared.Blocks;

namespace Minecraft.Shared.Worlds;

/// <summary>
/// Generates a simple flat world with a few trees. Used by the server today and
/// can be reused by the client for local previews or single-player testing.
/// </summary>
public static class FlatWorldGenerator
{
    public static World Generate(
        int sizeX,
        int sizeZ,
        int surfaceLevel = 8,
        int height = World.DefaultHeight)
    {
        if (surfaceLevel < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceLevel), "Need at least 3 layers.");
        }

        if (surfaceLevel >= height)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceLevel), "Surface must be below the world height.");
        }

        var world = new World(sizeX, height, sizeZ);
        var ground = surfaceLevel - 1;

        for (var x = 0; x < world.SizeX; x++)
        {
            for (var z = 0; z < world.SizeZ; z++)
            {
                // Bedrock-ish layers at the bottom, dirt in the middle, grass on top.
                world.SetBlock(x, 0, z, BlockId.Stone);

                for (var y = 1; y < ground; y++)
                {
                    world.SetBlock(x, y, z, y < ground - 2 ? BlockId.Stone : BlockId.Dirt);
                }

                world.SetBlock(x, ground, z, BlockId.Grass);
            }
        }

        PlantTree(world, sizeX / 4, sizeZ / 4, ground);
        PlantTree(world, sizeX * 3 / 4, sizeZ / 4, ground);
        PlantTree(world, sizeX * 3 / 8, sizeZ / 4, ground);
        PlantTree(world, sizeX / 2, sizeZ - 6, ground);

        return world;
    }

    private static void PlantTree(World world, int x, int z, int groundY)
    {
        if (!world.InBounds(x, groundY + 1, z))
        {
            return;
        }

        const int trunkHeight = 4;

        for (var y = 1; y <= trunkHeight; y++)
        {
            world.SetBlock(x, groundY + y, z, BlockId.Wood);
        }

        var topY = groundY + trunkHeight;

        // Three canopy layers: two wide layers with trimmed corners, then a small cap.
        for (var dy = -1; dy <= 1; dy++)
        {
            var radius = dy == 1 ? 1 : 2;

            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    if (radius == 2 && Math.Abs(dx) == radius && Math.Abs(dz) == radius)
                    {
                        continue;
                    }

                    var leafX = x + dx;
                    var leafY = topY + dy;
                    var leafZ = z + dz;

                    if (world.InBounds(leafX, leafY, leafZ) &&
                        world.GetBlock(leafX, leafY, leafZ) == BlockId.Air)
                    {
                        world.SetBlock(leafX, leafY, leafZ, BlockId.Leaves);
                    }
                }
            }
        }
    }
}
