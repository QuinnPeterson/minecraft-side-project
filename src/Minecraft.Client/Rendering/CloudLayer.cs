using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Minecraft.Client.Rendering;

/// <summary>
/// A flat, slowly drifting cloud plane above the world. The texture is generated
/// from two octaves of value noise so the project still needs no art assets.
/// </summary>
public sealed class CloudLayer : IDisposable
{
    private const int TextureSize = 128;
    private const int Cells = 16;
    private const float CloudHeight = 140f;
    private const float HalfSize = 512f;

    private readonly Texture2D _texture;
    private readonly PrimitiveMesh<VertexPositionColorTexture> _mesh;

    public CloudLayer(GraphicsDevice device)
    {
        _texture = CreateTexture(device);

        var vertices = new[]
        {
            new VertexPositionColorTexture(new Vector3(-HalfSize, CloudHeight, -HalfSize), Color.White, new Vector2(0f, 0f)),
            new VertexPositionColorTexture(new Vector3(-HalfSize, CloudHeight, HalfSize), Color.White, new Vector2(0f, 4f)),
            new VertexPositionColorTexture(new Vector3(HalfSize, CloudHeight, HalfSize), Color.White, new Vector2(4f, 4f)),
            new VertexPositionColorTexture(new Vector3(HalfSize, CloudHeight, -HalfSize), Color.White, new Vector2(4f, 0f)),
        };

        var indices = new[] { 0, 1, 2, 0, 2, 3 };

        _mesh = new PrimitiveMesh<VertexPositionColorTexture>(device, vertices, indices);
    }

    public void Draw(GraphicsDevice device, BasicEffect effect, float offset)
    {
        if (_mesh.IndexCount == 0)
        {
            return;
        }

        var previousTexture = effect.Texture;

        effect.Texture = _texture;
        effect.World = Matrix.CreateTranslation(offset, 0f, offset * 0.55f);
        effect.CurrentTechnique.Passes[0].Apply();

        device.SamplerStates[0] = SamplerState.PointWrap;
        device.SetVertexBuffer(_mesh.Vertices);
        device.Indices = _mesh.Indices;
        device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);
        device.SamplerStates[0] = SamplerState.PointClamp;

        effect.Texture = previousTexture;
    }

    public void Dispose()
    {
        _mesh.Dispose();
        _texture.Dispose();
    }

    private static Texture2D CreateTexture(GraphicsDevice device)
    {
        var random = new Random(90210);
        var coarse = new float[Cells, Cells];
        var fine = new float[Cells * 2, Cells * 2];

        for (var y = 0; y < Cells; y++)
        {
            for (var x = 0; x < Cells; x++)
            {
                coarse[x, y] = (float)random.NextDouble();
            }
        }

        for (var y = 0; y < Cells * 2; y++)
        {
            for (var x = 0; x < Cells * 2; x++)
            {
                fine[x, y] = (float)random.NextDouble();
            }
        }

        var pixels = new Color[TextureSize * TextureSize];

        for (var y = 0; y < TextureSize; y++)
        {
            for (var x = 0; x < TextureSize; x++)
            {
                var u = x / (float)TextureSize;
                var v = y / (float)TextureSize;
                var value = Sample(coarse, Cells, u, v) * 0.68f +
                            Sample(fine, Cells * 2, u, v) * 0.32f;

                pixels[y * TextureSize + x] = value > 0.56f
                    ? new Color(255, 255, 255, (int)(150 + value * 100f))
                    : Color.Transparent;
            }
        }

        var texture = new Texture2D(device, TextureSize, TextureSize, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        return texture;
    }

    private static float Sample(float[,] values, int size, float u, float v)
    {
        var x = u * size;
        var y = v * size;
        var x0 = (int)MathF.Floor(x) % size;
        var y0 = (int)MathF.Floor(y) % size;
        var x1 = (x0 + 1) % size;
        var y1 = (y0 + 1) % size;
        var fx = x - MathF.Floor(x);
        var fy = y - MathF.Floor(y);

        var top = MathHelper.Lerp(values[x0, y0], values[x1, y0], fx);
        var bottom = MathHelper.Lerp(values[x0, y1], values[x1, y1], fx);
        return MathHelper.Lerp(top, bottom, fy);
    }
}
