using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;

namespace Minecraft.Client.Rendering;

/// <summary>
/// A static vertex/index buffer pair. The generic parameter picks the vertex
/// layout so the world, player models, and debug lines can all reuse this.
/// </summary>
public sealed class PrimitiveMesh<T> : IDisposable
    where T : struct, IVertexType
{
    public PrimitiveMesh(
        GraphicsDevice device,
        IReadOnlyList<T> vertices,
        IReadOnlyList<int> indices)
    {
        IndexCount = indices.Count;

        if (IndexCount == 0 || vertices.Count == 0)
        {
            return;
        }

        Vertices = new VertexBuffer(
            device,
            typeof(T),
            vertices.Count,
            BufferUsage.WriteOnly);
        Vertices.SetData(vertices.ToArray());

        Indices = new IndexBuffer(
            device,
            IndexElementSize.ThirtyTwoBits,
            IndexCount,
            BufferUsage.WriteOnly);
        Indices.SetData(indices.ToArray());
    }

    public VertexBuffer Vertices { get; } = null!;

    public IndexBuffer Indices { get; } = null!;

    public int IndexCount { get; }

    public void Dispose()
    {
        Vertices?.Dispose();
        Indices?.Dispose();
    }
}
