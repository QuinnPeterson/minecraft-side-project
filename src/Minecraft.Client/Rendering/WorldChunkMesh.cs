using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;

namespace Minecraft.Client.Rendering;

/// <summary>
/// The opaque and transparent geometry for one 16x16 column of the world.
/// Chunks live in their own buffers so edits only rebuild the affected area.
/// </summary>
public sealed class WorldChunkMesh : IDisposable
{
    public WorldChunkMesh(
        GraphicsDevice device,
        MeshData opaque,
        MeshData transparent)
    {
        Opaque = new PrimitiveMesh<VertexPositionColorTexture>(
            device,
            opaque.Vertices,
            opaque.Indices);

        Transparent = new PrimitiveMesh<VertexPositionColorTexture>(
            device,
            transparent.Vertices,
            transparent.Indices);
    }

    public PrimitiveMesh<VertexPositionColorTexture> Opaque { get; }

    public PrimitiveMesh<VertexPositionColorTexture> Transparent { get; }

    public void Dispose()
    {
        Opaque.Dispose();
        Transparent.Dispose();
    }
}

/// <summary>Vertex and index lists used while building a chunk.</summary>
public sealed class MeshData
{
    public List<VertexPositionColorTexture> Vertices { get; } = new();

    public List<int> Indices { get; } = new();
}

/// <summary>
/// Converts blocks into textured geometry. Faces are culled against solid
/// neighbours, plants and water use their own shapes, and every face gets a
/// fixed directional shade so the world reads as cubes without dynamic lights.
/// </summary>
public static class WorldMeshBuilder
{
    private const int ChunkSize = WorldProtocol.ChunkSize;

    private static readonly Vector3[] TopCorners =
    {
        new(0, 1, 0), new(0, 1, 1), new(1, 1, 1), new(1, 1, 0),
    };

    private static readonly Vector3[] BottomCorners =
    {
        new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1),
    };

    private static readonly Vector3[] NorthCorners =
    {
        new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0),
    };

    private static readonly Vector3[] SouthCorners =
    {
        new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1),
    };

    private static readonly Vector3[] WestCorners =
    {
        new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0),
    };

    private static readonly Vector3[] EastCorners =
    {
        new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1),
    };

    // Side textures are upright: bottom-left, top-left, top-right, bottom-right.
    private static readonly Vector2[] SideUvs =
    {
        new(0, 1), new(0, 0), new(1, 0), new(1, 1),
    };

    // Top and bottom textures read left to right, near to far.
    private static readonly Vector2[] HorizontalUvs =
    {
        new(0, 0), new(1, 0), new(1, 1), new(0, 1),
    };

    public static WorldChunkMesh Build(
        GraphicsDevice device,
        World world,
        int chunkX,
        int chunkZ)
    {
        var opaque = new MeshData();
        var transparent = new MeshData();

        var baseX = chunkX * ChunkSize;
        var baseZ = chunkZ * ChunkSize;

        for (var x = baseX; x < baseX + ChunkSize; x++)
        {
            for (var z = baseZ; z < baseZ + ChunkSize; z++)
            {
                for (var y = 0; y < world.SizeY; y++)
                {
                    var block = world.GetBlock(x, y, z);

                    if (block == BlockId.Air)
                    {
                        continue;
                    }

                    if (!BlockRegistry.TryGet(block, out var definition))
                    {
                        continue;
                    }

                    var light = world.GetLight(x, y, z);
                    var target = definition.UsesTransparentPass ? transparent : opaque;
                    switch (definition.Shape)
                    {
                        case BlockShape.Water:
                            AddWater(target, world, x, y, z, definition);
                            break;

                        case BlockShape.Cross:
                            AddCross(
                                target,
                                new Vector3(x, y, z),
                                PlantHeight(block),
                                BlockAtlas.GetUvRect(definition.Texture),
                                Tint(definition, 1f, light));
                            break;

                        case BlockShape.Torch:
                            AddBox(
                                target,
                                new Vector3(x, y, z),
                                new Vector3(0.375f, 0f, 0.375f),
                                new Vector3(0.625f, 0.625f, 0.625f),
                                definition,
                                light);
                            break;

                        case BlockShape.SnowLayer:
                            AddSnowLayer(target, new Vector3(x, y, z), definition, light);
                            break;

                        default:
                            AddCube(target, world, x, y, z, definition);
                            break;
                    }
                }
            }
        }

        return new WorldChunkMesh(device, opaque, transparent);
    }

    private static void AddCube(
        MeshData data,
        World world,
        int x,
        int y,
        int z,
        BlockDefinition definition)
    {
        var origin = new Vector3(x, y, z);
        var sideUv = BlockAtlas.GetUvRect(definition.Texture);
        var topUv = BlockAtlas.GetUvRect(definition.Top);
        var bottomUv = BlockAtlas.GetUvRect(definition.Bottom);
        var block = definition.Id;

        // Shade every face with the light of the air cell in front of it, which
        // is how Minecraft lights block faces (a placed block in the open is
        // therefore bright even though the block cell itself is opaque).
        if (IsFaceVisible(block, world.GetBlock(x, y + 1, z)))
        {
            AddFace(data, origin, TopCorners, topUv, HorizontalUvs,
                Tint(definition, 1f, world.GetLight(x, y + 1, z)));
        }

        if (y > 0 && IsFaceVisible(block, world.GetBlock(x, y - 1, z)))
        {
            AddFace(data, origin, BottomCorners, bottomUv, HorizontalUvs,
                Tint(definition, 0.45f, world.GetLight(x, y - 1, z)));
        }

        if (FaceVisible(world, block, x, y, z, 0, 0, -1))
        {
            AddFace(data, origin, NorthCorners, sideUv, SideUvs,
                Tint(definition, 0.72f, world.GetLight(x, y, z - 1)));
        }

        if (FaceVisible(world, block, x, y, z, 0, 0, 1))
        {
            AddFace(data, origin, SouthCorners, sideUv, SideUvs,
                Tint(definition, 0.80f, world.GetLight(x, y, z + 1)));
        }

        if (FaceVisible(world, block, x, y, z, -1, 0, 0))
        {
            AddFace(data, origin, WestCorners, sideUv, SideUvs,
                Tint(definition, 0.68f, world.GetLight(x - 1, y, z)));
        }

        if (FaceVisible(world, block, x, y, z, 1, 0, 0))
        {
            AddFace(data, origin, EastCorners, sideUv, SideUvs,
                Tint(definition, 0.76f, world.GetLight(x + 1, y, z)));
        }
    }

    /// <summary>
    /// Faces on the outside of the world are never visible to a player inside
    /// it, so skip them instead of building geometry for the world shell.
    /// </summary>
    private static bool FaceVisible(
        World world,
        BlockId current,
        int x,
        int y,
        int z,
        int dx,
        int dy,
        int dz) =>
        world.InBounds(x + dx, y + dy, z + dz) &&
        IsFaceVisible(current, world.GetBlock(x + dx, y + dy, z + dz));

    private static void AddWater(
        MeshData data,
        World world,
        int x,
        int y,
        int z,
        BlockDefinition definition)
    {
        var origin = new Vector3(x, y, z);
        var uv = BlockAtlas.GetUvRect(definition.Texture);
        var fallback = WaterHeight(definition.Id);
        var block = definition.Id;

        var h00 = GetWaterCornerHeight(world, x, y, z, fallback);
        var h10 = GetWaterCornerHeight(world, x + 1, y, z, fallback);
        var h11 = GetWaterCornerHeight(world, x + 1, y, z + 1, fallback);
        var h01 = GetWaterCornerHeight(world, x, y, z + 1, fallback);

        if (y > 0 && IsFaceVisible(block, world.GetBlock(x, y - 1, z)))
        {
            AddFace(data, origin, BottomCorners, uv, HorizontalUvs,
                Tint(definition, 0.42f, world.GetLight(x, y - 1, z)));
        }

        if (IsFaceVisible(block, world.GetBlock(x, y + 1, z)))
        {
            var top = new[]
            {
                new Vector3(0f, h00, 0f),
                new Vector3(0f, h01, 1f),
                new Vector3(1f, h11, 1f),
                new Vector3(1f, h10, 0f),
            };

            AddFace(data, origin, top, uv, HorizontalUvs,
                Tint(definition, 1f, world.GetLight(x, y + 1, z)));
        }

        if (FaceVisible(world, block, x, y, z, 0, 0, -1))
        {
            var north = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, h00, 0f),
                new Vector3(1f, h10, 0f),
                new Vector3(1f, 0f, 0f),
            };

            AddFace(data, origin, north, uv, SideUvs,
                Tint(definition, 0.65f, world.GetLight(x, y, z - 1)));
        }

        if (FaceVisible(world, block, x, y, z, 0, 0, 1))
        {
            var south = new[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, h01, 1f),
                new Vector3(1f, h11, 1f),
                new Vector3(1f, 0f, 1f),
            };

            AddFace(data, origin, south, uv, SideUvs,
                Tint(definition, 0.72f, world.GetLight(x, y, z + 1)));
        }

        if (FaceVisible(world, block, x, y, z, -1, 0, 0))
        {
            var west = new[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, h01, 1f),
                new Vector3(0f, h00, 0f),
                new Vector3(0f, 0f, 0f),
            };

            AddFace(data, origin, west, uv, SideUvs,
                Tint(definition, 0.62f, world.GetLight(x - 1, y, z)));
        }

        if (FaceVisible(world, block, x, y, z, 1, 0, 0))
        {
            var east = new[]
            {
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, h10, 0f),
                new Vector3(1f, h11, 1f),
                new Vector3(1f, 0f, 1f),
            };

            AddFace(data, origin, east, uv, SideUvs,
                Tint(definition, 0.70f, world.GetLight(x + 1, y, z)));
        }
    }

    private static void AddSnowLayer(
        MeshData data,
        Vector3 origin,
        BlockDefinition definition,
        byte light)
    {
        var uv = BlockAtlas.GetUvRect(definition.Texture);
        var color = Tint(definition, 1f, light);

        var top = new[]
        {
            new Vector3(0f, 0.125f, 0f),
            new Vector3(0f, 0.125f, 1f),
            new Vector3(1f, 0.125f, 1f),
            new Vector3(1f, 0.125f, 0f),
        };

        AddFace(data, origin, top, uv, HorizontalUvs, color);

        var north = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0.125f, 0f),
            new Vector3(1f, 0.125f, 0f),
            new Vector3(1f, 0f, 0f),
        };

        var south = new[]
        {
            new Vector3(1f, 0f, 1f),
            new Vector3(1f, 0.125f, 1f),
            new Vector3(0f, 0.125f, 1f),
            new Vector3(0f, 0f, 1f),
        };

        var west = new[]
        {
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0.125f, 1f),
            new Vector3(0f, 0.125f, 0f),
            new Vector3(0f, 0f, 0f),
        };

        var east = new[]
        {
            new Vector3(1f, 0f, 0f),
            new Vector3(1f, 0.125f, 0f),
            new Vector3(1f, 0.125f, 1f),
            new Vector3(1f, 0f, 1f),
        };

        AddFace(data, origin, north, uv, SideUvs, Tint(definition, 0.72f, light));
        AddFace(data, origin, south, uv, SideUvs, Tint(definition, 0.80f, light));
        AddFace(data, origin, west, uv, SideUvs, Tint(definition, 0.68f, light));
        AddFace(data, origin, east, uv, SideUvs, Tint(definition, 0.76f, light));
    }

    private static void AddBox(
        MeshData data,
        Vector3 origin,
        Vector3 min,
        Vector3 max,
        BlockDefinition definition,
        byte light)
    {
        var sideUv = BlockAtlas.GetUvRect(definition.Texture);
        var topUv = BlockAtlas.GetUvRect(definition.Top);
        var bottomUv = BlockAtlas.GetUvRect(definition.Bottom);

        Vector3 Corner(Vector3 unit) => min + unit * (max - min);

        AddFace(data, origin,
            new[] { Corner(TopCorners[0]), Corner(TopCorners[1]), Corner(TopCorners[2]), Corner(TopCorners[3]) },
            topUv, HorizontalUvs, Tint(definition, 1f, light));

        AddFace(data, origin,
            new[] { Corner(BottomCorners[0]), Corner(BottomCorners[1]), Corner(BottomCorners[2]), Corner(BottomCorners[3]) },
            bottomUv, HorizontalUvs, Tint(definition, 0.45f, light));

        AddFace(data, origin,
            new[] { Corner(NorthCorners[0]), Corner(NorthCorners[1]), Corner(NorthCorners[2]), Corner(NorthCorners[3]) },
            sideUv, SideUvs, Tint(definition, 0.72f, light));

        AddFace(data, origin,
            new[] { Corner(SouthCorners[0]), Corner(SouthCorners[1]), Corner(SouthCorners[2]), Corner(SouthCorners[3]) },
            sideUv, SideUvs, Tint(definition, 0.80f, light));

        AddFace(data, origin,
            new[] { Corner(WestCorners[0]), Corner(WestCorners[1]), Corner(WestCorners[2]), Corner(WestCorners[3]) },
            sideUv, SideUvs, Tint(definition, 0.68f, light));

        AddFace(data, origin,
            new[] { Corner(EastCorners[0]), Corner(EastCorners[1]), Corner(EastCorners[2]), Corner(EastCorners[3]) },
            sideUv, SideUvs, Tint(definition, 0.76f, light));
    }

    private static void AddCross(
        MeshData data,
        Vector3 origin,
        float height,
        Vector4 uv,
        Color color)
    {
        AddQuad(data, origin,
            new Vector3(0.15f, 0f, 0.15f),
            new Vector3(0.15f, height, 0.15f),
            new Vector3(0.85f, height, 0.85f),
            new Vector3(0.85f, 0f, 0.85f),
            uv, color);

        AddQuad(data, origin,
            new Vector3(0.85f, 0f, 0.15f),
            new Vector3(0.85f, height, 0.15f),
            new Vector3(0.15f, height, 0.85f),
            new Vector3(0.15f, 0f, 0.85f),
            uv, color);
    }

    private static void AddQuad(
        MeshData data,
        Vector3 origin,
        Vector3 bottomLeft,
        Vector3 topLeft,
        Vector3 topRight,
        Vector3 bottomRight,
        Vector4 uv,
        Color color)
    {
        var start = data.Vertices.Count;

        data.Vertices.Add(new VertexPositionColorTexture(origin + bottomLeft, color, Uv(uv, 0f, 1f)));
        data.Vertices.Add(new VertexPositionColorTexture(origin + topLeft, color, Uv(uv, 0f, 0f)));
        data.Vertices.Add(new VertexPositionColorTexture(origin + topRight, color, Uv(uv, 1f, 0f)));
        data.Vertices.Add(new VertexPositionColorTexture(origin + bottomRight, color, Uv(uv, 1f, 1f)));

        data.Indices.Add(start);
        data.Indices.Add(start + 1);
        data.Indices.Add(start + 2);
        data.Indices.Add(start);
        data.Indices.Add(start + 2);
        data.Indices.Add(start + 3);
    }

    private static void AddFace(
        MeshData data,
        Vector3 origin,
        Vector3[] corners,
        Vector4 uv,
        Vector2[] pattern,
        Color color)
    {
        var start = data.Vertices.Count;

        for (var i = 0; i < 4; i++)
        {
            data.Vertices.Add(new VertexPositionColorTexture(
                origin + corners[i],
                color,
                Uv(uv, pattern[i].X, pattern[i].Y)));
        }

        data.Indices.Add(start);
        data.Indices.Add(start + 1);
        data.Indices.Add(start + 2);
        data.Indices.Add(start);
        data.Indices.Add(start + 2);
        data.Indices.Add(start + 3);
    }

    private static Vector2 Uv(Vector4 rect, float u, float v) =>
        new(
            MathHelper.Lerp(rect.X, rect.Z, u),
            MathHelper.Lerp(rect.Y, rect.W, v));

    private static bool IsFaceVisible(BlockId current, BlockId neighbor)
    {
        if (neighbor == BlockId.Air)
        {
            return true;
        }

        if (neighbor == current)
        {
            return false;
        }

        if (BlockRegistry.IsWater(current) && BlockRegistry.IsWater(neighbor))
        {
            return false;
        }

        return BlockRegistry.TryGet(neighbor, out var definition) && definition.IsTransparent;
    }

    /// <summary>
    /// Corner height for smooth water. A corner is shared by up to four water
    /// blocks; sources dominate so pools settle flat.
    /// </summary>
    private static float GetWaterCornerHeight(
        World world,
        int cornerX,
        int y,
        int cornerZ,
        float fallback)
    {
        var totalHeight = 0f;
        var totalWeight = 0f;

        for (var dx = -1; dx <= 0; dx++)
        {
            for (var dz = -1; dz <= 0; dz++)
            {
                var water = world.GetBlock(cornerX + dx, y, cornerZ + dz);

                if (!BlockRegistry.IsWater(water))
                {
                    continue;
                }

                if (BlockRegistry.IsWater(world.GetBlock(cornerX + dx, y + 1, cornerZ + dz)))
                {
                    return 1f;
                }

                var level = BlockRegistry.WaterLevel(water);
                var weight = level <= 0 ? 10f : 1f;
                totalHeight += WaterHeight(water) * weight;
                totalWeight += weight;
            }
        }

        return totalWeight <= 0f ? fallback : totalHeight / totalWeight;
    }

    private static float WaterHeight(BlockId block)
    {
        var level = BlockRegistry.WaterLevel(block);

        if (level <= 0)
        {
            return 0.92f;
        }

        return Math.Max(0.18f, 0.92f - level * 0.10f);
    }

    private static float PlantHeight(BlockId block)
    {
        if (BlockRegistry.IsCrop(block))
        {
            return 0.35f + 0.1f * ((int)block - (int)BlockId.Wheat1);
        }

        return block switch
        {
            BlockId.Sapling => 0.7f,
            BlockId.SugarCane => 1f,
            _ => 0.95f,
        };
    }

    private static Color Tint(BlockDefinition definition, float shade, byte light)
    {
        var rgb = definition.Tint;
        var factor = shade * LightFactor(light);
        var red = (byte)Math.Clamp((int)(((rgb >> 16) & 0xFF) * factor), 0, 255);
        var green = (byte)Math.Clamp((int)(((rgb >> 8) & 0xFF) * factor), 0, 255);
        var blue = (byte)Math.Clamp((int)((rgb & 0xFF) * factor), 0, 255);
        var alpha = (byte)Math.Clamp((int)(definition.Alpha * 255f), 0, 255);
        return new Color(red, green, blue, alpha);
    }

    /// <summary>
    /// Maps a 0-15 light level to a brightness multiplier. Even unlit caves
    /// keep a little ambient light so they stay readable.
    /// </summary>
    private static float LightFactor(byte light)
    {
        var level = Math.Clamp((int)light, 0, 15) / 15f;
        return 0.16f + 0.84f * level;
    }
}
