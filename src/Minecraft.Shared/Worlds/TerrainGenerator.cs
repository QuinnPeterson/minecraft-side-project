using Minecraft.Shared.Blocks;

namespace Minecraft.Shared.Worlds;

public enum BiomeId : byte
{
    Plains,
    Forest,
    Desert,
    Snowy,
}

public sealed record TerrainGenerationResult(World World, int Seed, int SpawnX, int SpawnY, int SpawnZ);

/// <summary>
/// Overworld-style terrain inspired by TrueCraft's StandardGenerator:
/// layered Perlin noise for continents and hills, temperature/humidity biomes,
/// 3D-noise cave tunnels, ore veins, beaches, oceans, snow, and vegetation.
///
/// Everything is generated from a seed, so the server and any future
/// single-player client produce the same world.
/// </summary>
public static class TerrainGenerator
{
    public const int SeaLevel = 62;

    private const int SpawnFlattenRadius = 26;

    public static TerrainGenerationResult Generate(
        int sizeX,
        int sizeZ,
        int height = World.DefaultHeight,
        int seed = 0)
    {
        if (seed == 0)
        {
            seed = Environment.TickCount;
        }

        var world = new World(sizeX, height, sizeZ);
        var heights = new int[sizeX, sizeZ];
        var biomes = new BiomeId[sizeX, sizeZ];

        var continent = new PerlinNoise(seed);
        var detail = new PerlinNoise(seed + 101);
        var ridges = new PerlinNoise(seed + 202);
        var temperature = new PerlinNoise(seed + 303);
        var humidity = new PerlinNoise(seed + 404);
        var caveA = new PerlinNoise(seed + 505);
        var caveB = new PerlinNoise(seed + 606);

        var centerX = sizeX / 2;
        var centerZ = sizeZ / 2;

        // Column pass: height map, biome map, terrain layers, oceans.
        for (var x = 0; x < sizeX; x++)
        {
            for (var z = 0; z < sizeZ; z++)
            {
                var columnHeight = GetHeight(
                    continent, detail, ridges, x, z, centerX, centerZ, sizeX, sizeZ, height);

                var temp = temperature.Fbm2D(x * 0.0032f, z * 0.0032f, 3);
                var humid = humidity.Fbm2D(x * 0.0041f + 500f, z * 0.0041f - 250f, 3);
                var biome = GetBiome(columnHeight, temp, humid);

                heights[x, z] = columnHeight;
                biomes[x, z] = biome;

                FillColumn(world, caveA, caveB, x, z, columnHeight, biome);

                // Ocean / lake water down to sea level.
                for (var y = columnHeight + 1; y <= SeaLevel && y < height; y++)
                {
                    world.SetBlock(x, y, z, BlockId.Water);
                }

                if (biome == BiomeId.Snowy && columnHeight < SeaLevel)
                {
                    world.SetBlock(x, SeaLevel, z, BlockId.Ice);
                }
            }
        }

        var random = new Random(seed ^ 0x5F3759DF);

        // Ore and stone pockets.
        var areaScale = sizeX * sizeZ / (128f * 128f);
        ScatterVeins(world, random, BlockId.CoalOre, Scale(900, areaScale), 5, 96, 4, 14);
        ScatterVeins(world, random, BlockId.IronOre, Scale(650, areaScale), 5, 68, 3, 10);
        ScatterVeins(world, random, BlockId.GoldOre, Scale(150, areaScale), 5, 34, 2, 6);
        ScatterVeins(world, random, BlockId.LapisOre, Scale(95, areaScale), 5, 28, 2, 6);
        ScatterVeins(world, random, BlockId.DiamondOre, Scale(55, areaScale), 5, 18, 2, 5);
        ScatterVeins(world, random, BlockId.Gravel, Scale(320, areaScale), 5, 72, 4, 12);
        ScatterVeins(world, random, BlockId.Dirt, Scale(280, areaScale), 5, 72, 4, 12);

        // Trees, cacti, grass, flowers, sugar cane, and snow cover.
        for (var x = 2; x < sizeX - 2; x++)
        {
            for (var z = 2; z < sizeZ - 2; z++)
            {
                var surfaceY = heights[x, z];
                var biome = biomes[x, z];

                if (Math.Abs(x - centerX) <= 7 && Math.Abs(z - centerZ) <= 7)
                {
                    continue;
                }

                if (surfaceY < SeaLevel - 1 || surfaceY >= height - 8)
                {
                    continue;
                }

                var ground = world.GetBlock(x, surfaceY, z);

                if (ground == BlockId.Grass)
                {
                    var treeChance = biome switch
                    {
                        BiomeId.Forest => 0.055f,
                        BiomeId.Plains => 0.012f,
                        BiomeId.Snowy => 0.010f,
                        _ => 0f,
                    };

                    if (random.NextDouble() < treeChance &&
                        world.GetBlock(x, surfaceY + 1, z) == BlockId.Air)
                    {
                        PlaceTree(world, random, x, surfaceY, z);
                        continue;
                    }

                    if (biome == BiomeId.Snowy)
                    {
                        // Thin snow layers look like floating sheets next to
                        // full snow blocks, so snowy terrain uses solid blocks.
                        world.SetBlock(x, surfaceY, z, BlockId.SnowBlock);
                        continue;
                    }

                    var plantRoll = random.NextDouble();

                    if (plantRoll < 0.010)
                    {
                        world.SetBlock(
                            x, surfaceY + 1, z,
                            random.Next(2) == 0 ? BlockId.Dandelion : BlockId.Rose);
                    }
                    else if (plantRoll < 0.095)
                    {
                        world.SetBlock(x, surfaceY + 1, z, BlockId.TallGrass);
                    }
                    else if (plantRoll < 0.20 && HasWaterNeighbor(world, x, surfaceY, z))
                    {
                        var caneHeight = 1 + random.Next(3);

                        for (var i = 1; i <= caneHeight; i++)
                        {
                            if (world.GetBlock(x, surfaceY + i, z) == BlockId.Air)
                            {
                                world.SetBlock(x, surfaceY + i, z, BlockId.SugarCane);
                            }
                        }
                    }
                    else if (biome == BiomeId.Plains && random.NextDouble() < 0.0015)
                    {
                        world.SetBlock(x, surfaceY + 1, z, BlockId.Pumpkin);
                    }
                }
                else if (ground == BlockId.Sand && biome == BiomeId.Desert)
                {
                    if (random.NextDouble() < 0.006 &&
                        world.GetBlock(x, surfaceY + 1, z) == BlockId.Air)
                    {
                        var cactusHeight = 1 + random.Next(3);

                        for (var i = 1; i <= cactusHeight; i++)
                        {
                            if (world.GetBlock(x, surfaceY + i, z) == BlockId.Air)
                            {
                                world.SetBlock(x, surfaceY + i, z, BlockId.Cactus);
                            }
                        }
                    }
                    else if (random.NextDouble() < 0.012 &&
                             world.GetBlock(x, surfaceY + 1, z) == BlockId.Air)
                    {
                        world.SetBlock(x, surfaceY + 1, z, BlockId.DeadBush);
                    }
                    else if (random.NextDouble() < 0.12 && HasWaterNeighbor(world, x, surfaceY, z))
                    {
                        var caneHeight = 1 + random.Next(3);
                        for (var i = 1; i <= caneHeight; i++)
                        {
                            if (world.GetBlock(x, surfaceY + i, z) == BlockId.Air)
                            {
                                world.SetBlock(x, surfaceY + i, z, BlockId.SugarCane);
                            }
                        }
                    }
                }
                else if (ground == BlockId.Water && surfaceY == SeaLevel - 1 &&
                         biome == BiomeId.Snowy)
                {
                    world.SetBlock(x, SeaLevel, z, BlockId.Ice);
                }
            }
        }

        // Clear a small area above the spawn so the camera is not inside a tree.
        var spawnY = Math.Max(SeaLevel + 1, heights[centerX, centerZ] + 1);
        for (var dx = -3; dx <= 3; dx++)
        {
            for (var dz = -3; dz <= 3; dz++)
            {
                for (var dy = 0; dy <= 5; dy++)
                {
                    var bx = centerX + dx;
                    var bz = centerZ + dz;
                    var by = spawnY + dy;

                    if (world.InBounds(bx, by, bz) &&
                        world.GetBlock(bx, by, bz) != BlockId.Air)
                    {
                        world.SetBlock(bx, by, bz, BlockId.Air);
                    }
                }
            }
        }

        return new TerrainGenerationResult(world, seed, centerX, spawnY, centerZ);
    }

    private static int GetHeight(
        PerlinNoise continent,
        PerlinNoise detail,
        PerlinNoise ridges,
        int x,
        int z,
        int centerX,
        int centerZ,
        int sizeX,
        int sizeZ,
        int height)
    {
        var continentValue = continent.Fbm2D(x * 0.0045f, z * 0.0045f, 4);
        var detailValue = detail.Fbm2D(x * 0.021f, z * 0.021f, 3);
        var ridgeValue = 1f - MathF.Abs(ridges.Fbm2D(x * 0.008f, z * 0.008f, 3));

        var value = SeaLevel +
                    continentValue * 17f +
                    detailValue * 5f +
                    (ridgeValue * ridgeValue - 0.55f) * 20f;

        // Keep a walkable landmass around the spawn point.
        var dx = x - centerX;
        var dz = z - centerZ;
        var distance = MathF.Sqrt(dx * dx + dz * dz);

        if (distance < SpawnFlattenRadius)
        {
            var blend = 1f - distance / SpawnFlattenRadius;
            value = Lerp(value, 66f, blend * blend);
        }
        else if (distance < SpawnFlattenRadius * 2.5f)
        {
            // Gradually blend the flattened spawn into the surrounding terrain.
            var blend = 1f - (distance - SpawnFlattenRadius) / (SpawnFlattenRadius * 1.5f);
            value = Lerp(value, 66f, blend * 0.35f);
        }

        var maxHeight = height - 5;
        return Math.Clamp((int)value, 6, maxHeight);
    }

    private static BiomeId GetBiome(int height, float temperature, float humidity)
    {
        if (height >= 94 || temperature < -0.24f)
        {
            return BiomeId.Snowy;
        }

        if (temperature > 0.26f && humidity < 0.06f)
        {
            return BiomeId.Desert;
        }

        return humidity > 0.10f ? BiomeId.Forest : BiomeId.Plains;
    }

    private static void FillColumn(
        World world,
        PerlinNoise caveA,
        PerlinNoise caveB,
        int x,
        int z,
        int surfaceY,
        BiomeId biome)
    {
        var beach = surfaceY <= SeaLevel + 2 && surfaceY >= SeaLevel - 3;
        var mountain = surfaceY > 98;

        var topBlock = BlockId.Grass;
        var fillerBlock = BlockId.Dirt;

        if (biome == BiomeId.Desert)
        {
            topBlock = BlockId.Sand;
            fillerBlock = BlockId.Sand;
        }
        else if (beach)
        {
            topBlock = BlockId.Sand;
            fillerBlock = BlockId.Sand;
        }
        else if (biome == BiomeId.Snowy)
        {
            topBlock = mountain ? BlockId.SnowBlock : BlockId.Grass;
            fillerBlock = BlockId.Dirt;
        }

        if (mountain)
        {
            topBlock = BlockId.SnowBlock;
            fillerBlock = BlockId.Stone;
        }

        for (var y = 0; y <= surfaceY; y++)
        {
            if (y == 0)
            {
                world.SetBlock(x, y, z, BlockId.Bedrock);
                continue;
            }

            if (y < surfaceY - 5 && IsCave(caveA, caveB, x, y, z))
            {
                continue;
            }

            BlockId block;

            if (y == surfaceY)
            {
                block = topBlock;
            }
            else if (y > surfaceY - 4)
            {
                if (biome == BiomeId.Desert || beach)
                {
                    block = y > surfaceY - 2 ? BlockId.Sand : BlockId.Sandstone;
                }
                else
                {
                    block = fillerBlock;
                }
            }
            else
            {
                block = BlockId.Stone;
            }

            world.SetBlock(x, y, z, block);
        }
    }

    private static bool IsCave(PerlinNoise caveA, PerlinNoise caveB, int x, int y, int z)
    {
        if (y < 5)
        {
            return false;
        }

        var a = caveA.Noise3D(x * 0.045f, y * 0.075f, z * 0.045f);
        var b = caveB.Noise3D(x * 0.045f + 31.7f, y * 0.075f - 17.3f, z * 0.045f + 53.1f);

        return a * a + b * b < 0.0018f;
    }

    private static int Scale(int count, float areaScale) =>
        Math.Max(1, (int)(count * areaScale));

    private static void ScatterVeins(
        World world,
        Random random,
        BlockId ore,
        int veins,
        int minY,
        int maxY,
        int minSize,
        int maxSize)
    {
        for (var vein = 0; vein < veins; vein++)
        {
            var x = random.Next(1, world.SizeX - 1);
            var y = random.Next(minY, Math.Min(maxY, world.SizeY - 2));
            var z = random.Next(1, world.SizeZ - 1);
            var size = random.Next(minSize, maxSize + 1);

            for (var i = 0; i < size; i++)
            {
                if (world.InBounds(x, y, z) && world.GetBlock(x, y, z) == BlockId.Stone)
                {
                    world.SetBlock(x, y, z, ore);
                }

                x += random.Next(-1, 2);
                y += random.Next(-1, 2);
                z += random.Next(-1, 2);

                if (y < minY || y > maxY)
                {
                    break;
                }
            }
        }
    }

    private static void PlaceTree(World world, Random random, int x, int groundY, int z)
    {
        var trunkHeight = 4 + random.Next(3);
        var topY = groundY + trunkHeight;

        if (!world.InBounds(x, topY + 1, z))
        {
            return;
        }

        for (var y = groundY + 1; y <= topY; y++)
        {
            if (world.GetBlock(x, y, z) != BlockId.Air)
            {
                return;
            }
        }

        for (var y = groundY + 1; y <= topY; y++)
        {
            world.SetBlock(x, y, z, BlockId.Wood);
        }

        for (var dy = -2; dy <= 1; dy++)
        {
            var radius = dy <= 0 ? 2 : 1;
            var leafY = topY + dy;

            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    if (radius == 2 && Math.Abs(dx) == radius && Math.Abs(dz) == radius)
                    {
                        continue;
                    }

                    if (dx == 0 && dz == 0 && dy <= 0)
                    {
                        continue;
                    }

                    var leafX = x + dx;
                    var leafZ = z + dz;

                    if (world.InBounds(leafX, leafY, leafZ) &&
                        world.GetBlock(leafX, leafY, leafZ) == BlockId.Air)
                    {
                        world.SetBlock(leafX, leafY, leafZ, BlockId.Leaves);
                    }
                }
            }
        }

        world.SetBlock(x, topY + 1, z, BlockId.Leaves);
    }

    private static bool HasWaterNeighbor(World world, int x, int y, int z)
    {
        return BlockRegistry.IsWater(world.GetBlock(x + 1, y, z)) ||
               BlockRegistry.IsWater(world.GetBlock(x - 1, y, z)) ||
               BlockRegistry.IsWater(world.GetBlock(x, y, z + 1)) ||
               BlockRegistry.IsWater(world.GetBlock(x, y, z - 1));
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// A separate nether dimension: a roofed netherrack cavern with lava seas,
    /// soul sand, glowstone pockets, and a return portal at the spawn point.
    /// </summary>
    public static TerrainGenerationResult GenerateNether(
        int sizeX,
        int sizeZ,
        int height = World.DefaultHeight,
        int seed = 0)
    {
        if (seed == 0)
        {
            seed = Environment.TickCount;
        }

        var world = new World(sizeX, height, sizeZ);
        var random = new Random(seed ^ 0x4E455448);
        var caves = new PerlinNoise(seed + 777);
        var detail = new PerlinNoise(seed + 878);
        var patches = new PerlinNoise(seed + 979);
        const int lavaSeaLevel = 31;

        for (var x = 0; x < sizeX; x++)
        {
            for (var z = 0; z < sizeZ; z++)
            {
                for (var y = 0; y < height; y++)
                {
                    if (y == 0 || y == height - 1)
                    {
                        world.SetBlock(x, y, z, BlockId.Bedrock);
                        continue;
                    }

                    // Two overlapping noise fields make big open caverns while keeping
                    // some netherrack pillars/walls for a Minecraft-like nether feel.
                    var cavern = caves.Fbm3D(x * 0.035f, y * 0.045f, z * 0.035f, 3);
                    var tunnel = detail.Fbm3D(x * 0.06f, y * 0.08f, z * 0.06f, 2);
                    var open = cavern > 0.08f || tunnel > 0.32f;

                    // Keep a rough floor/ceiling shell so the nether isn't a void.
                    var edgeDistance = Math.Min(y - 1, height - 2 - y);
                    if (edgeDistance < 6)
                    {
                        open &= random.NextDouble() < edgeDistance / 8.0;
                    }

                    if (open)
                    {
                        if (y <= lavaSeaLevel)
                        {
                            world.SetBlock(x, y, z, BlockId.Lava);
                        }

                        continue;
                    }

                    var block = BlockId.Netherrack;

                    // Gravel and soulsand pockets give surface variety.
                    var patch = patches.Fbm3D(x * 0.09f, y * 0.09f, z * 0.09f, 2);
                    if (patch > 0.45f)
                    {
                        block = BlockId.Gravel;
                    }
                    else if (patch < -0.42f && y < 70)
                    {
                        block = BlockId.SoulSand;
                    }

                    // Glowstone clusters near ceilings and in pockets.
                    if (random.NextDouble() < 0.004 && y > 40)
                    {
                        block = BlockId.Glowstone;
                    }

                    world.SetBlock(x, y, z, block);
                }
            }
        }

        // Second pass: lava lakes, surface patches, and fire.
        for (var x = 1; x < sizeX - 1; x++)
        {
            for (var z = 1; z < sizeZ - 1; z++)
            {
                var lakeNoise = detail.Fbm2D(x * 0.03f, z * 0.03f, 3);
                var lakeCenter = lakeNoise > 0.55f;

                for (var y = height - 3; y > 1; y--)
                {
                    var block = world.GetBlock(x, y, z);
                    if (block == BlockId.Air || block == BlockId.Fire || BlockRegistry.IsLava(block))
                    {
                        continue;
                    }

                    var above = world.GetBlock(x, y + 1, z);
                    var aboveAir = above == BlockId.Air || above == BlockId.Fire;
                    var surface = aboveAir;

                    // Carve shallow lava lakes into open ground.
                    if (lakeCenter && surface && y <= lavaSeaLevel + 2 && y >= lavaSeaLevel - 6)
                    {
                        for (var dy = 0; dy >= -2; dy--)
                        {
                            var yy = y + dy;
                            if (yy <= 1) break;
                            var current = world.GetBlock(x, yy, z);
                            if (current == BlockId.Netherrack || current == BlockId.Gravel || current == BlockId.SoulSand)
                            {
                                world.SetBlock(x, yy, z, yy <= lavaSeaLevel ? BlockId.Lava : BlockId.Air);
                            }
                        }

                        continue;
                    }

                    if (!surface)
                    {
                        continue;
                    }

                    // Soul sand / gravel patches on exposed floors.
                    var surfacePatch = patches.Fbm2D(x * 0.08f, z * 0.08f, 2);
                    if (surfacePatch > 0.5f && block == BlockId.Netherrack)
                    {
                        world.SetBlock(x, y, z, BlockId.Gravel);
                        block = BlockId.Gravel;
                    }
                    else if (surfacePatch < -0.55f && y < 72 && block == BlockId.Netherrack)
                    {
                        world.SetBlock(x, y, z, BlockId.SoulSand);
                        block = BlockId.SoulSand;
                    }

                    // Fire scattered around like grass on netherrack.
                    if (block == BlockId.Netherrack && above == BlockId.Air && random.NextDouble() < 0.015)
                    {
                        world.SetBlock(x, y + 1, z, BlockId.Fire);
                    }

                    break;
                }
            }
        }

        var spawnX = sizeX / 2;
        var spawnZ = sizeZ / 2;

        // Carve and floor a small safe cavern, then build the return portal.
        for (var x = spawnX - 9; x <= spawnX + 9; x++)
        {
            for (var z = spawnZ - 9; z <= spawnZ + 9; z++)
            {
                for (var y = lavaSeaLevel - 2; y <= lavaSeaLevel + 18; y++)
                {
                    world.SetBlock(x, y, z, BlockId.Air);
                }

                for (var y = lavaSeaLevel - 6; y <= lavaSeaLevel - 3; y++)
                {
                    world.SetBlock(x, y, z, y == lavaSeaLevel - 3 ? BlockId.Netherrack : BlockId.SoulSand);
                }
            }
        }

        BuildPortal(world, spawnX, lavaSeaLevel - 2, spawnZ + 2, alongX: true);

        return new TerrainGenerationResult(world, seed, spawnX, lavaSeaLevel - 2, spawnZ);
    }

    /// <summary>Builds a 4x5 obsidian frame with a 2x3 portal interior.</summary>
    public static void BuildPortal(World world, int x, int y, int z, bool alongX)
    {
        for (var offset = -1; offset <= 2; offset++)
        {
            for (var height = 0; height < 5; height++)
            {
                var frame = offset is -1 or 2 || height is 0 or 4;
                var block = frame ? BlockId.Obsidian : BlockId.NetherPortal;
                var px = alongX ? x + offset : x;
                var pz = alongX ? z : z + offset;
                world.SetBlock(px, y + height, pz, block);
            }
        }
    }
}
