using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Minecraft.Shared.Blocks;

namespace Minecraft.Client.Rendering;

/// <summary>
/// Layout and procedural generation for the block texture atlas.
///
/// The atlas is generated at runtime so the project stays asset-free. Each
/// tile is a 16x16 pixel-art pattern in the spirit of the beta textures.
/// </summary>
public static class BlockAtlas
{
    public const int TileSize = 16;
    public const int Columns = 16;
    public const int Rows = 16;
    public const int Width = TileSize * Columns;
    public const int Height = TileSize * Rows;

    public static Rectangle GetSourceRect(BlockTexture tile)
    {
        var index = (int)tile;
        return new Rectangle(
            index % Columns * TileSize,
            index / Columns * TileSize,
            TileSize,
            TileSize);
    }

    public static Vector4 GetUvRect(BlockTexture tile)
    {
        var index = (int)tile;
        var x = index % Columns * TileSize;
        var y = index / Columns * TileSize;

        // Half-texel inset keeps point sampling away from the neighboring tile.
        const float inset = 0.5f;

        return new Vector4(
            (x + inset) / Width,
            (y + inset) / Height,
            (x + TileSize - inset) / Width,
            (y + TileSize - inset) / Height);
    }

    public static Texture2D CreateTexture(GraphicsDevice device)
    {
        var pixels = new Color[Width * Height];
        var tiles = Enum.GetValues<BlockTexture>();

        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.Transparent;
        }

        foreach (var tile in tiles)
        {
            DrawTile(pixels, tile);
        }

        DrawCrackTiles(pixels);

        var texture = new Texture2D(device, Width, Height, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        return texture;
    }

    public static void UpdateWaterAnimation(Texture2D atlas, float time)
    {
        var rect = GetSourceRect(BlockTexture.Water);
        var pixels = new Color[TileSize * TileSize];

        for (var y = 0; y < TileSize; y++)
        {
            for (var x = 0; x < TileSize; x++)
            {
                var wave = (int)(MathF.Sin((x + y * 0.6f + time * 2f) * 0.8f) * 12f);
                pixels[y * TileSize + x] = new Color(44 + wave, 94 + wave, 196 + wave);
            }
        }

        atlas.SetData(0, rect, pixels, 0, pixels.Length);
    }
    public static Rectangle GetCrackSourceRect(int stage)
    {
        stage = Math.Clamp(stage, 0, 9);
        return new Rectangle(stage * TileSize, 15 * TileSize, TileSize, TileSize);
    }

    public static Vector4 GetCrackUvRect(int stage)
    {
        var rect = GetCrackSourceRect(stage);
        const float inset = 0.5f;
        return new Vector4(
            (rect.X + inset) / Width,
            (rect.Y + inset) / Height,
            (rect.X + TileSize - inset) / Width,
            (rect.Y + TileSize - inset) / Height);
    }

    private static void DrawCrackTiles(Color[] pixels)
    {
        var random = new Random(42);

        for (var stage = 0; stage < 10; stage++)
        {
            var rect = new Rectangle(stage * TileSize, 15 * TileSize, TileSize, TileSize);

            // White background.
            for (var y = 0; y < TileSize; y++)
            {
                for (var x = 0; x < TileSize; x++)
                {
                    pixels[(rect.Y + y) * Width + rect.X + x] = Color.White;
                }
            }

            // Draw crack lines radiating from the center, increasing with stage.
            var crackCount = stage + 1;
            var centerX = TileSize / 2;
            var centerY = TileSize / 2;

            for (var c = 0; c < crackCount; c++)
            {
                // Random direction from center.
                var angle = (c / (float)crackCount) * MathF.PI * 2f + (float)(random.NextDouble() * 0.5);
                var length = 4 + random.Next(6);
                var x = centerX;
                var y = centerY;

                for (var s = 0; s < length; s++)
                {
                    x = Math.Clamp(x + (int)MathF.Round(MathF.Cos(angle)), 0, TileSize - 1);
                    y = Math.Clamp(y + (int)MathF.Round(MathF.Sin(angle)), 0, TileSize - 1);

                    // Darken the pixel and its neighbors (crack thickness).
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var px = Math.Clamp(x + dx, 0, TileSize - 1);
                            var py = Math.Clamp(y + dy, 0, TileSize - 1);
                            var idx = (rect.Y + py) * Width + rect.X + px;
                            var darkness = (byte)(60 + random.Next(40));
                            pixels[idx] = new Color(darkness, darkness, darkness);
                        }
                    }

                    // Branch occasionally.
                    if (s == length / 2 && stage > 2)
                    {
                        var branchAngle = angle + (float)(random.NextDouble() * 1.2 - 0.6);
                        var bx = x;
                        var by = y;
                        var branchLen = 2 + random.Next(3);

                        for (var bs = 0; bs < branchLen; bs++)
                        {
                            bx = Math.Clamp(bx + (int)MathF.Round(MathF.Cos(branchAngle)), 0, TileSize - 1);
                            by = Math.Clamp(by + (int)MathF.Round(MathF.Sin(branchAngle)), 0, TileSize - 1);

                            for (var dy = -1; dy <= 1; dy++)
                            {
                                for (var dx = -1; dx <= 1; dx++)
                                {
                                    var px = Math.Clamp(bx + dx, 0, TileSize - 1);
                                    var py = Math.Clamp(by + dy, 0, TileSize - 1);
                                    var idx = (rect.Y + py) * Width + rect.X + px;
                                    pixels[idx] = new Color(90, 90, 90);
                                }
                            }
                        }
                    }

                    // Jitter the angle slightly.
                    angle += (float)(random.NextDouble() * 0.4 - 0.2);
                }
            }
        }
    }

    private static void DrawTile(Color[] pixels, BlockTexture tile)
    {
        var rect = GetSourceRect(tile);
        var salt = (int)tile * 7919;

        switch (tile)
        {
            case BlockTexture.Air:
                break;

            case BlockTexture.Stone:
                Fill(pixels, rect, (x, y) => Noise(128, 128, 128, x, y, salt, 15));
                break;

            case BlockTexture.Dirt:
                Fill(pixels, rect, (x, y) => Noise(134, 96, 67, x, y, salt, 14));
                break;

            case BlockTexture.GrassTop:
                Fill(pixels, rect, (x, y) => Noise(92, 158, 54, x, y, salt, 16));
                break;

            case BlockTexture.GrassSide:
                Fill(pixels, rect, (x, y) =>
                {
                    var edge = 3 + (Rand(x, 3, salt) < 0.45f ? 1 : 0);
                    return y <= edge
                        ? Noise(92, 158, 54, x, y, salt + 1, 18)
                        : Noise(134, 96, 67, x, y, salt + 2, 12);
                });
                break;

            case BlockTexture.Sand:
                Fill(pixels, rect, (x, y) => Noise(219, 208, 164, x, y, salt, 9));
                break;

            case BlockTexture.Gravel:
                Fill(pixels, rect, (x, y) =>
                {
                    var cell = (int)(Rand(x / 3, y / 3, salt) * 40) + 95;
                    return Noise(cell, cell, cell, x, y, salt + 1, 18);
                });
                break;

            case BlockTexture.Cobblestone:
                Fill(pixels, rect, (x, y) =>
                {
                    var mortar = x % 5 == 0 || y % 5 == 0;
                    var shade = mortar ? 68 : 100 + (int)(Rand(x / 5, y / 5, salt) * 45);
                    return Noise(shade, shade, shade, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.Planks:
                Fill(pixels, rect, (x, y) =>
                {
                    var seam = y % 4 == 3 ||
                               (y / 4 % 2 == 0 ? x is 0 or 8 : x is 4 or 12);
                    var value = seam ? 118 : 168;
                    return Noise(value, (int)(value * 0.75f), (int)(value * 0.44f), x, y, salt, 12);
                });
                break;

            case BlockTexture.LogSide:
                Fill(pixels, rect, (x, y) =>
                {
                    var stripe = x % 4 == 0 || x % 7 == 3;
                    return stripe
                        ? Noise(78, 56, 35, x, y, salt, 10)
                        : Noise(110, 80, 50, x, y, salt + 1, 16);
                });
                break;

            case BlockTexture.LogTop:
                Fill(pixels, rect, (x, y) =>
                {
                    var dx = x - 7.5f;
                    var dy = y - 7.5f;
                    var distance = MathF.Sqrt(dx * dx + dy * dy);
                    var ring = MathF.Sin(distance * 2.2f) > 0f;
                    var baseValue = ring ? 176 : 140;
                    return Noise(baseValue, (int)(baseValue * 0.76f), (int)(baseValue * 0.47f), x, y, salt, 8);
                });
                break;

            case BlockTexture.Leaves:
                Fill(pixels, rect, (x, y) =>
                {
                    var cluster = Rand(x / 2, y / 2, salt);
                    var green = 82 + (int)(cluster * 60);
                    return Noise(38, green, 28, x, y, salt + 1, 18);
                });
                break;

            case BlockTexture.Water:
                Fill(pixels, rect, (x, y) =>
                {
                    var wave = (int)(MathF.Sin((x + y * 0.6f) * 0.8f) * 12f);
                    return new Color(44 + wave, 94 + wave, 196 + wave);
                });
                break;

            case BlockTexture.Lava:
                Fill(pixels, rect, (x, y) =>
                {
                    var hot = Rand(x / 2, y / 2, salt) > 0.72f;
                    return hot
                        ? new Color(255, 208, 62)
                        : Noise(214, 88, 22, x, y, salt, 26);
                });
                break;

            case BlockTexture.Bedrock:
                Fill(pixels, rect, (x, y) =>
                {
                    var value = Rand(x / 2, y / 2, salt) > 0.55f ? 74 : 42;
                    return Noise(value, value, value, x, y, salt + 1, 14);
                });
                break;

            case BlockTexture.CoalOre:
                DrawOre(pixels, rect, new Color(38, 38, 38), new Color(82, 82, 82), salt);
                break;

            case BlockTexture.IronOre:
                DrawOre(pixels, rect, new Color(198, 170, 140), new Color(238, 212, 180), salt);
                break;

            case BlockTexture.GoldOre:
                DrawOre(pixels, rect, new Color(232, 194, 58), new Color(255, 235, 128), salt);
                break;

            case BlockTexture.DiamondOre:
                DrawOre(pixels, rect, new Color(88, 214, 214), new Color(178, 255, 255), salt);
                break;

            case BlockTexture.LapisOre:
                DrawOre(pixels, rect, new Color(46, 74, 190), new Color(104, 138, 240), salt);
                break;

            case BlockTexture.GoldBlock:
                DrawMetal(pixels, rect, new Color(238, 200, 54), new Color(255, 238, 132), salt);
                break;

            case BlockTexture.IronBlock:
                DrawMetal(pixels, rect, new Color(198, 198, 198), new Color(238, 238, 238), salt);
                break;

            case BlockTexture.DiamondBlock:
                DrawMetal(pixels, rect, new Color(92, 222, 222), new Color(186, 255, 255), salt);
                break;

            case BlockTexture.LapisBlock:
                DrawMetal(pixels, rect, new Color(40, 66, 168), new Color(92, 126, 230), salt);
                break;

            case BlockTexture.Bricks:
                Fill(pixels, rect, (x, y) =>
                {
                    var row = y / 4;
                    var mortar = y % 4 == 0 || (row % 2 == 0 ? x % 8 == 0 : x % 8 == 4);
                    var value = mortar ? 178 : 150;
                    return mortar
                        ? Noise(value, value - 8, value - 16, x, y, salt, 8)
                        : Noise(value, 72, 56, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.TNT:
                Fill(pixels, rect, (x, y) =>
                {
                    if (y is >= 5 and <= 10)
                    {
                        return IsTntLetter(x, y - 5)
                            ? new Color(92, 30, 26)
                            : new Color(236, 232, 220);
                    }

                    return Noise(190, 52, 42, x, y, salt, 16);
                });
                break;

            case BlockTexture.Bookshelf:
                Fill(pixels, rect, (x, y) =>
                {
                    if (y < 2 || y > 13)
                    {
                        return Noise(160, 120, 70, x, y, salt, 10);
                    }

                    if (y is 2 or 13)
                    {
                        return new Color(92, 68, 42);
                    }

                    var book = (x / 2) % 4;
                    return book switch
                    {
                        0 => new Color(148, 48, 44),
                        1 => new Color(48, 78, 148),
                        2 => new Color(62, 122, 58),
                        _ => new Color(154, 118, 52),
                    };
                });
                break;

            case BlockTexture.MossStone:
                Fill(pixels, rect, (x, y) =>
                {
                    var mortar = x % 5 == 0 || y % 5 == 0;
                    var moss = Rand(x / 2, y / 2, salt + 4) > 0.55f;
                    var shade = mortar ? 68 : 104;
                    return moss
                        ? Noise(72, 122, 52, x, y, salt, 18)
                        : Noise(shade, shade, shade, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.Obsidian:
                Fill(pixels, rect, (x, y) =>
                {
                    var highlight = Rand(x / 2, y / 2, salt) > 0.82f;
                    return highlight
                        ? new Color(78, 48, 106)
                        : Noise(28, 18, 46, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.Glass:
                Fill(pixels, rect, (x, y) =>
                {
                    var border = x == 0 || y == 0 || x == 15 || y == 15;
                    var streak = x == y + 3 || x == y + 4;
                    return border
                        ? new Color(214, 238, 244, 200)
                        : streak
                            ? new Color(238, 252, 255, 80)
                            : new Color(210, 236, 244, 28);
                });
                break;

            case BlockTexture.Snow:
                Fill(pixels, rect, (x, y) => Noise(244, 248, 250, x, y, salt, 6));
                break;

            case BlockTexture.Ice:
                Fill(pixels, rect, (x, y) =>
                {
                    var crack = (x * 3 + y * 5) % 17 == 0;
                    return crack
                        ? new Color(190, 224, 246)
                        : Noise(148, 196, 236, x, y, salt, 10);
                });
                break;

            case BlockTexture.CactusSide:
                Fill(pixels, rect, (x, y) =>
                {
                    var rib = x is 1 or 2 or 13 or 14;
                    var spike = Rand(x, y, salt) > 0.9f;
                    if (spike)
                    {
                        return new Color(206, 222, 166);
                    }

                    return rib
                        ? Noise(42, 92, 36, x, y, salt, 12)
                        : Noise(66, 126, 54, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.CactusTop:
                Fill(pixels, rect, (x, y) =>
                {
                    var dx = x - 7.5f;
                    var dy = y - 7.5f;
                    var center = dx * dx + dy * dy < 18f;
                    return center
                        ? Noise(92, 152, 66, x, y, salt, 12)
                        : Noise(58, 116, 48, x, y, salt + 1, 10);
                });
                break;

            case BlockTexture.Clay:
                Fill(pixels, rect, (x, y) => Noise(158, 166, 178, x, y, salt, 10));
                break;

            case BlockTexture.SandstoneTop:
                Fill(pixels, rect, (x, y) => Noise(226, 214, 166, x, y, salt, 7));
                break;

            case BlockTexture.SandstoneSide:
                Fill(pixels, rect, (x, y) =>
                {
                    var band = y switch
                    {
                        < 3 => 232,
                        < 5 => 204,
                        < 12 => 220,
                        < 14 => 202,
                        _ => 228,
                    };

                    return Noise(band, band - 12, band - 58, x, y, salt, 7);
                });
                break;

            case BlockTexture.Wool:
                Fill(pixels, rect, (x, y) => Noise(236, 236, 236, x, y, salt, 8));
                break;

            case BlockTexture.Sponge:
                Fill(pixels, rect, (x, y) =>
                {
                    var hole = Rand(x / 2, y / 2, salt) > 0.6f;
                    return hole
                        ? new Color(116, 110, 34)
                        : Noise(198, 190, 64, x, y, salt + 1, 16);
                });
                break;

            case BlockTexture.CraftingTableTop:
                Fill(pixels, rect, (x, y) =>
                {
                    var grid = x is 0 or 5 or 10 or 15 || y is 0 or 5 or 10 or 15;
                    return grid
                        ? new Color(86, 64, 38)
                        : Noise(172, 132, 78, x, y, salt, 10);
                });
                break;

            case BlockTexture.CraftingTableSide:
                Fill(pixels, rect, (x, y) =>
                {
                    if (y is < 2 or > 13)
                    {
                        return Noise(160, 120, 70, x, y, salt, 10);
                    }

                    // Simple saw/hammer silhouettes on the side panel.
                    var tool = (x is 3 or 4 && y is >= 4 and <= 12) ||
                               (x is 10 or 11 && y is >= 5 and <= 12) ||
                               (x is >= 2 and <= 5 && y == 4) ||
                               (x is >= 9 and <= 12 && y == 5);

                    return tool
                        ? new Color(80, 80, 84)
                        : Noise(150, 112, 64, x, y, salt + 1, 8);
                });
                break;

            case BlockTexture.FurnaceSide:
                Fill(pixels, rect, (x, y) =>
                {
                    var border = x == 0 || y == 0 || x == 15 || y == 15;
                    return border
                        ? Noise(82, 82, 82, x, y, salt, 8)
                        : Noise(122, 122, 122, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.FurnaceFront:
                Fill(pixels, rect, (x, y) =>
                {
                    var border = x == 0 || y == 0 || x == 15 || y == 15;
                    var opening = x is >= 4 and <= 11 && y is >= 6 and <= 13;

                    if (opening)
                    {
                        var ember = y >= 10;
                        return ember
                            ? new Color(244, 150, 48)
                            : new Color(36, 32, 30);
                    }

                    return border
                        ? Noise(82, 82, 82, x, y, salt, 8)
                        : Noise(126, 126, 126, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.PumpkinSide:
                Fill(pixels, rect, (x, y) =>
                {
                    var rib = x % 5 == 0;
                    return rib
                        ? Noise(168, 88, 18, x, y, salt, 12)
                        : Noise(216, 128, 32, x, y, salt + 1, 14);
                });
                break;

            case BlockTexture.PumpkinTop:
                Fill(pixels, rect, (x, y) =>
                {
                    var dx = x - 7.5f;
                    var dy = y - 7.5f;

                    if (dx * dx + dy * dy < 4f)
                    {
                        return new Color(104, 72, 32);
                    }

                    return Noise(202, 118, 28, x, y, salt, 12);
                });
                break;

            case BlockTexture.JackOLantern:
                Fill(pixels, rect, (x, y) =>
                {
                    var eye = (x is 2 or 3 or 12 or 13) && y is >= 4 and <= 7;
                    var mouth = y is >= 9 and <= 12 && x >= 4 && x <= 11 &&
                                !(y == 12 && x % 3 == 0);
                    var glow = eye || mouth;

                    if (glow)
                    {
                        return new Color(255, 218, 88);
                    }

                    var rib = x % 5 == 0;
                    return rib
                        ? Noise(168, 88, 18, x, y, salt, 12)
                        : Noise(216, 128, 32, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.Netherrack:
                Fill(pixels, rect, (x, y) =>
                {
                    var dark = Rand(x / 2, y / 2, salt) > 0.65f;
                    return dark
                        ? Noise(88, 26, 26, x, y, salt, 14)
                        : Noise(126, 42, 42, x, y, salt + 1, 16);
                });
                break;

            case BlockTexture.SoulSand:
                Fill(pixels, rect, (x, y) =>
                {
                    var hole = Rand(x / 3, y / 3, salt) > 0.68f;
                    return hole
                        ? new Color(58, 44, 32)
                        : Noise(118, 92, 70, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.Glowstone:
                Fill(pixels, rect, (x, y) =>
                {
                    var glow = Rand(x, y, salt) > 0.45f;
                    return glow
                        ? new Color(252, 232, 142)
                        : Noise(206, 168, 74, x, y, salt + 1, 16);
                });
                break;

            case BlockTexture.Torch:
                FillTransparent(pixels, rect, (x, y) =>
                {
                    var stick = x is 7 or 8 && y is >= 6 and <= 15;
                    var tip = x is >= 6 and <= 9 && y is >= 2 and <= 5;
                    var glow = x is >= 5 and <= 10 && y is >= 3 and <= 5;

                    if (stick)
                    {
                        return new Color(128, 92, 50);
                    }

                    if (tip)
                    {
                        return new Color(255, 238, 150);
                    }

                    return glow ? new Color(255, 186, 58, 120) : Color.Transparent;
                });
                break;

            case BlockTexture.FarmlandTop:
                Fill(pixels, rect, (x, y) =>
                {
                    var furrow = x % 4 == 0;
                    return furrow
                        ? Noise(84, 58, 36, x, y, salt, 8)
                        : Noise(124, 88, 56, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.FarmlandSide:
                Fill(pixels, rect, (x, y) =>
                {
                    var top = y < 2;
                    return top
                        ? Noise(112, 76, 46, x, y, salt, 8)
                        : Noise(134, 96, 66, x, y, salt + 1, 12);
                });
                break;

            case BlockTexture.Wheat:
                FillTransparent(pixels, rect, (x, y) =>
                {
                    var stalk = x is 2 or 3 or 7 or 8 or 12 or 13;
                    var head = y is >= 2 and <= 7 && (x is 1 or 2 or 3 or 4 ||
                                                       x is 6 or 7 or 8 or 9 ||
                                                       x is 11 or 12 or 13 or 14);

                    if (stalk)
                    {
                        return new Color(156, 156, 142);
                    }

                    return head ? new Color(228, 228, 210) : Color.Transparent;
                });
                break;

            case BlockTexture.TallGrass:
                FillTransparent(pixels, rect, (x, y) =>
                {
                    var blade = y >= 3 && y <= 15 &&
                                ((x == 2 && y > 5) ||
                                 (x == 4 && y > 8) ||
                                 (x == 7 && y > 3) ||
                                 (x == 9 && y > 7) ||
                                 (x == 12 && y > 4) ||
                                 (x == 14 && y > 9) ||
                                 (y == 15 && x is >= 2 and <= 14) ||
                                 (y == 12 && x is 5 or 6) ||
                                 (y == 10 && x is 10 or 11));

                    return blade
                        ? Noise(76, 140, 50, x, y, salt, 20)
                        : Color.Transparent;
                });
                break;

            case BlockTexture.Sapling:
                FillTransparent(pixels, rect, (x, y) =>
                {
                    var stem = x == 7 && y is >= 9 and <= 15;
                    var leaves = y is >= 3 and <= 10 &&
                                 (x is >= 5 and <= 9) &&
                                 !(x is 5 or 9 && y is 3 or 10);

                    if (stem)
                    {
                        return new Color(102, 74, 42);
                    }

                    return leaves ? Noise(58, 130, 46, x, y, salt, 20) : Color.Transparent;
                });
                break;

            case BlockTexture.Dandelion:
                FillTransparent(pixels, rect, (x, y) =>
                {
                    var stem = x == 7 && y is >= 8 and <= 15;
                    var flower = (x - 8) * (x - 8) + (y - 5) * (y - 5) <= 10;
                    var leaf = y is 12 or 13 && x is 5 or 6 or 9 or 10;

                    if (stem)
                    {
                        return new Color(74, 132, 46);
                    }

                    if (flower)
                    {
                        return Noise(238, 196, 48, x, y, salt, 18);
                    }

                    return leaf ? new Color(68, 122, 42) : Color.Transparent;
                });
                break;

            case BlockTexture.Rose:
                FillTransparent(pixels, rect, (x, y) =>
                {
                    var stem = x == 7 && y is >= 7 and <= 15;
                    var flower = (x - 8) * (x - 8) + (y - 5) * (y - 5) <= 11;
                    var leaf = y is 11 or 12 && x is 5 or 6 or 9 or 10;

                    if (stem)
                    {
                        return new Color(74, 132, 46);
                    }

                    if (flower)
                    {
                        return Noise(196, 46, 46, x, y, salt, 20);
                    }

                    return leaf ? new Color(68, 122, 42) : Color.Transparent;
                });
                break;

            case BlockTexture.BrownMushroom:
                FillTransparent(pixels, rect, (x, y) =>
                {
                    var stem = x is 7 or 8 && y is >= 9 and <= 14;
                    var cap = y is >= 4 and <= 9 && x is >= 4 and <= 11 &&
                              Math.Abs(x - 7.5f) + Math.Abs(y - 8) < 7f;

                    if (stem)
                    {
                        return new Color(214, 200, 176);
                    }

                    return cap ? Noise(132, 98, 74, x, y, salt, 16) : Color.Transparent;
                });
                break;

            case BlockTexture.RedMushroom:
                FillTransparent(pixels, rect, (x, y) =>
                {
                    var stem = x is 7 or 8 && y is >= 9 and <= 14;
                    var cap = y is >= 4 and <= 9 && x is >= 4 and <= 11 &&
                              Math.Abs(x - 7.5f) + Math.Abs(y - 8) < 7f;
                    var spot = cap && Rand(x / 2, y / 2, salt) > 0.78f;

                    if (stem)
                    {
                        return new Color(226, 214, 198);
                    }

                    if (cap)
                    {
                        return spot
                            ? new Color(238, 232, 220)
                            : Noise(198, 48, 42, x, y, salt, 16);
                    }

                    return Color.Transparent;
                });
                break;

            case BlockTexture.DeadBush:
                FillTransparent(pixels, rect, (x, y) =>
                {
                    var twig = (x == 7 && y >= 6) ||
                               (y == 11 && x is >= 4 and <= 10) ||
                               (y == 8 && x is >= 5 and <= 11) ||
                               (y == 13 && x is >= 5 and <= 9) ||
                               (x == 4 && y is >= 8 and <= 12) ||
                               (x == 11 && y is >= 9 and <= 13);

                    return twig
                        ? Noise(126, 94, 48, x, y, salt, 18)
                        : Color.Transparent;
                });
                break;

            case BlockTexture.SugarCane:
                FillTransparent(pixels, rect, (x, y) =>
                {
                    var stalk = x is 3 or 4 or 8 or 9 or 13 or 14;
                    var node = y is 5 or 10;

                    if (!stalk)
                    {
                        return Color.Transparent;
                    }

                    return node
                        ? new Color(86, 142, 62)
                        : Noise(112, 180, 78, x, y, salt, 14);
                });
                break;

            default:
                Fill(pixels, rect, (x, y) => Noise(255, 0, 255, x, y, salt, 0));
                break;
        }
    }

    private static void DrawOre(Color[] pixels, Rectangle rect, Color ore, Color highlight, int salt)
    {
        var centers = new (int X, int Y)[6];

        for (var i = 0; i < centers.Length; i++)
        {
            centers[i] = (
                (int)(Rand(i, 1, salt) * 14) + 1,
                (int)(Rand(i, 2, salt) * 14) + 1);
        }

        Fill(pixels, rect, (x, y) =>
        {
            var baseColor = Noise(128, 128, 128, x, y, salt, 15);

            foreach (var (cx, cy) in centers)
            {
                var dx = x - cx;
                var dy = y - cy;

                if (dx * dx + dy * dy <= 3)
                {
                    return Rand(x, y, salt + 9) > 0.72f
                        ? highlight
                        : Noise(ore.R, ore.G, ore.B, x, y, salt + 4, 12);
                }
            }

            return baseColor;
        });
    }

    private static void DrawMetal(Color[] pixels, Rectangle rect, Color baseColor, Color highlight, int salt)
    {
        Fill(pixels, rect, (x, y) =>
        {
            var border = x == 0 || y == 0 || x == 15 || y == 15;
            var shine = x == y || x == y + 1;
            var color = border
                ? Scale(baseColor, 0.68f)
                : shine
                    ? highlight
                    : baseColor;

            return Noise(color.R, color.G, color.B, x, y, salt, 10);
        });
    }

    private static bool IsTntLetter(int x, int letterY)
    {
        // 3x5 letters "TNT" starting at x=2, 6, and 10.
        var letter = (x - 2) / 4;
        var localX = (x - 2) % 4;

        if (letter is < 0 or > 2 || localX < 0 || localX > 2)
        {
            return false;
        }

        return letter switch
        {
            0 => localX == 1 || letterY == 0,
            1 => localX == 0 || localX == 2 || letterY == 0 || letterY == 2 || letterY == 4,
            _ => localX == 1 || letterY == 0,
        };
    }

    private static void Fill(Color[] pixels, Rectangle rect, Func<int, int, Color> pattern)
    {
        FillTransparent(pixels, rect, pattern);
    }

    private static void FillTransparent(Color[] pixels, Rectangle rect, Func<int, int, Color> pattern)
    {
        for (var y = 0; y < TileSize; y++)
        {
            for (var x = 0; x < TileSize; x++)
            {
                pixels[(rect.Y + y) * Width + rect.X + x] = pattern(x, y);
            }
        }
    }

    private static Color Noise(int r, int g, int b, int x, int y, int salt, int amount)
    {
        var offset = amount == 0 ? 0 : (int)((Rand(x, y, salt) - 0.5f) * amount * 2f);
        return new Color(
            Math.Clamp(r + offset, 0, 255),
            Math.Clamp(g + offset, 0, 255),
            Math.Clamp(b + offset, 0, 255));
    }

    private static Color Scale(Color color, float factor) =>
        new(
            (int)(color.R * factor),
            (int)(color.G * factor),
            (int)(color.B * factor),
            color.A);

    private static float Rand(int x, int y, int salt)
    {
        unchecked
        {
            var hash = (uint)(x * 374761393 + y * 668265263 + salt * 1274126177);
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            hash ^= hash >> 16;
            return (hash & 0xFFFF) / 65535f;
        }
    }
}
