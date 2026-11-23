using System;

namespace Minecraft.Shared.Worlds;

/// <summary>
/// Classic Perlin gradient noise with fBm helpers.
///
/// The terrain recipe follows TrueCraft's StandardGenerator (Copyright (c)
/// 2015 Drew DeVault, MIT licensed); see THIRD-PARTY-NOTICES.md. The noise
/// implementation itself is a clean-room version of Ken Perlin's improved
/// noise so it can run without the reference project's dependencies.
/// </summary>
public sealed class PerlinNoise
{
    private readonly int[] _permutation = new int[512];

    public PerlinNoise(int seed)
    {
        var p = new int[256];

        for (var i = 0; i < p.Length; i++)
        {
            p[i] = i;
        }

        var random = new Random(seed);

        for (var i = p.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }

        for (var i = 0; i < 512; i++)
        {
            _permutation[i] = p[i & 255];
        }
    }

    public float Noise2D(float x, float y) => Noise3D(x, y, 0f);

    public float Noise3D(float x, float y, float z)
    {
        var floorX = FastFloor(x);
        var floorY = FastFloor(y);
        var floorZ = FastFloor(z);

        var xf = x - floorX;
        var yf = y - floorY;
        var zf = z - floorZ;

        var u = Fade(xf);
        var v = Fade(yf);
        var w = Fade(zf);

        var aa = _permutation[_permutation[floorX & 255] + (floorY & 255)];
        var ab = _permutation[_permutation[floorX & 255] + ((floorY + 1) & 255)];
        var ba = _permutation[_permutation[(floorX + 1) & 255] + (floorY & 255)];
        var bb = _permutation[_permutation[(floorX + 1) & 255] + ((floorY + 1) & 255)];

        var x1 = Lerp(
            Grad(_permutation[aa + (floorZ & 255)], xf, yf, zf),
            Grad(_permutation[ba + (floorZ & 255)], xf - 1f, yf, zf),
            u);

        var x2 = Lerp(
            Grad(_permutation[ab + (floorZ & 255)], xf, yf - 1f, zf),
            Grad(_permutation[bb + (floorZ & 255)], xf - 1f, yf - 1f, zf),
            u);

        var x3 = Lerp(
            Grad(_permutation[aa + ((floorZ + 1) & 255)], xf, yf, zf - 1f),
            Grad(_permutation[ba + ((floorZ + 1) & 255)], xf - 1f, yf, zf - 1f),
            u);

        var x4 = Lerp(
            Grad(_permutation[ab + ((floorZ + 1) & 255)], xf, yf - 1f, zf - 1f),
            Grad(_permutation[bb + ((floorZ + 1) & 255)], xf - 1f, yf - 1f, zf - 1f),
            u);

        return Lerp(Lerp(x1, x2, v), Lerp(x3, x4, v), w);
    }

    /// <summary>Fractal noise normalized to roughly -1..1.</summary>
    public float Fbm2D(
        float x,
        float y,
        int octaves,
        float frequency = 1f,
        float persistence = 0.5f,
        float lacunarity = 2f)
    {
        var total = 0f;
        var amplitude = 1f;
        var normalizer = 0f;

        for (var i = 0; i < octaves; i++)
        {
            total += Noise2D(x * frequency, y * frequency) * amplitude;
            normalizer += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return normalizer > 0f ? total / normalizer : 0f;
    }

    /// <summary>Fractal 3D noise normalized to roughly -1..1.</summary>
    public float Fbm3D(
        float x,
        float y,
        float z,
        int octaves,
        float frequency = 1f,
        float persistence = 0.5f,
        float lacunarity = 2f)
    {
        var total = 0f;
        var amplitude = 1f;
        var normalizer = 0f;

        for (var i = 0; i < octaves; i++)
        {
            total += Noise3D(x * frequency, y * frequency, z * frequency) * amplitude;
            normalizer += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return normalizer > 0f ? total / normalizer : 0f;
    }

    private static int FastFloor(float value) =>
        value >= 0f ? (int)value : (int)value - 1;

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Grad(int hash, float x, float y, float z)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : h is 12 or 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}
