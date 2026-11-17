using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;

namespace Minecraft.Client;

public class Game1 : Game
{
    private const int WorldSize = 32;
    private const float MouseSensitivity = 0.0025f;
    private const float MoveSpeed = 14f;

    private static readonly Color SkyColor = new(135, 206, 235);

    // Cube face corners, in local block space. Each face uses two triangles.
    private static readonly Vector3[] TopCorners =
    {
        new(0, 1, 0), new(1, 1, 0), new(1, 1, 1), new(0, 1, 1),
    };

    private static readonly Vector3[] BottomCorners =
    {
        new(0, 0, 0), new(0, 0, 1), new(1, 0, 1), new(1, 0, 0),
    };

    private static readonly Vector3[] NorthCorners =
    {
        new(0, 0, 0), new(0, 1, 0), new(0, 1, 1), new(0, 0, 1),
    };

    private static readonly Vector3[] SouthCorners =
    {
        new(1, 0, 0), new(1, 0, 1), new(1, 1, 1), new(1, 1, 0),
    };

    private static readonly Vector3[] WestCorners =
    {
        new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
    };

    private static readonly Vector3[] EastCorners =
    {
        new(0, 0, 1), new(0, 1, 1), new(1, 1, 1), new(1, 0, 1),
    };

    private readonly GraphicsDeviceManager _graphics;
    private readonly ServerConnection _connection = new();
    private World _world;
    private bool _worldDirty;

    private BasicEffect _effect;
    private VertexBuffer _vertexBuffer;
    private IndexBuffer _indexBuffer;
    private int _indexCount;

    private Vector3 _position = new(WorldSize / 2f, 14f, WorldSize + 14f);
    private float _yaw;
    private float _pitch = -0.2f;
    private bool _mouseCentered;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = false;

        _world = FlatWorldGenerator.Generate(WorldSize, WorldSize);
    }

    protected override void Initialize()
    {
        _graphics.PreferredBackBufferWidth = 1280;
        _graphics.PreferredBackBufferHeight = 720;
        _graphics.ApplyChanges();

        Window.Title = "Minecraft Client - connecting";
        base.Initialize();

        _connection.Start("127.0.0.1", WorldProtocol.DefaultPort);
    }

    protected override void LoadContent()
    {
        RebuildWorldMesh();

        _effect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
            FogEnabled = true,
            FogColor = SkyColor.ToVector3(),
            FogStart = 30f,
            FogEnd = 100f,
        };
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();

        if (keyboard.IsKeyDown(Keys.Escape))
        {
            Exit();
            return;
        }

        UpdateLook();
        UpdateMovement(keyboard, gameTime);
        _connection.Update();
        ApplyServerMessages();

        base.Update(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(SkyColor);

        var rotation = Matrix.CreateFromYawPitchRoll(_yaw, _pitch, 0);
        var view = Matrix.CreateLookAt(_position, _position + rotation.Forward, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(70f),
            GraphicsDevice.Viewport.AspectRatio,
            0.1f,
            200f);

        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;

        _effect.View = view;
        _effect.Projection = projection;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            GraphicsDevice.SetVertexBuffer(_vertexBuffer);
            GraphicsDevice.Indices = _indexBuffer;
            GraphicsDevice.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                0,
                0,
                _indexCount / 3);
        }

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _effect?.Dispose();
        base.UnloadContent();
    }

    private void UpdateLook()
    {
        if (!IsActive)
        {
            _mouseCentered = false;
            return;
        }

        var center = new Point(
            GraphicsDevice.Viewport.Width / 2,
            GraphicsDevice.Viewport.Height / 2);

        if (!_mouseCentered)
        {
            Mouse.SetPosition(center.X, center.Y);
            _mouseCentered = true;
            return;
        }

        var mouse = Mouse.GetState();
        var deltaX = mouse.X - center.X;
        var deltaY = mouse.Y - center.Y;

        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        _yaw -= deltaX * MouseSensitivity;
        _pitch = MathHelper.Clamp(
            _pitch - deltaY * MouseSensitivity,
            -MathHelper.PiOver2 + 0.01f,
            MathHelper.PiOver2 - 0.01f);

        Mouse.SetPosition(center.X, center.Y);
    }

    private void UpdateMovement(KeyboardState keyboard, GameTime gameTime)
    {
        var deltaTime = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.1f);
        var rotation = Matrix.CreateFromYawPitchRoll(_yaw, _pitch, 0);

        var forward = rotation.Forward;
        forward.Y = 0;

        if (forward.LengthSquared() < 0.0001f)
        {
            forward = Vector3.Forward;
        }

        forward.Normalize();
        var right = Vector3.Cross(forward, Vector3.Up);

        var movement = Vector3.Zero;

        if (keyboard.IsKeyDown(Keys.W)) movement += forward;
        if (keyboard.IsKeyDown(Keys.S)) movement -= forward;
        if (keyboard.IsKeyDown(Keys.D)) movement += right;
        if (keyboard.IsKeyDown(Keys.A)) movement -= right;
        if (keyboard.IsKeyDown(Keys.Space)) movement += Vector3.Up;
        if (keyboard.IsKeyDown(Keys.LeftShift)) movement -= Vector3.Up;

        if (movement.LengthSquared() > 0f)
        {
            _position += Vector3.Normalize(movement) * MoveSpeed * deltaTime;
        }
    }

    private void ApplyServerMessages()
    {
        while (_connection.Snapshots.TryDequeue(out var world))
        {
            _world = world;
            _worldDirty = true;
        }

        while (_connection.Changes.TryDequeue(out var change))
        {
            if (_world.SetBlock(change.X, change.Y, change.Z, change.Block))
            {
                _worldDirty = true;
            }
        }

        while (_connection.Statuses.TryDequeue(out var status))
        {
            Window.Title = $"Minecraft Client - {status}";
        }

        if (_worldDirty)
        {
            RebuildWorldMesh();
            _worldDirty = false;
        }
    }

    private void RebuildWorldMesh()
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();

        for (var x = 0; x < _world.SizeX; x++)
        {
            for (var y = 0; y < _world.SizeY; y++)
            {
                for (var z = 0; z < _world.SizeZ; z++)
                {
                    var block = _world.GetBlock(x, y, z);
                    if (block == BlockId.Air)
                    {
                        continue;
                    }

                    var color = ToColor(BlockRegistry.Get(block).Color);
                    var origin = new Vector3(x, y, z);

                    // Only add faces that touch air so hidden blocks cost nothing.
                    if (IsAir(x, y + 1, z))
                    {
                        AddFace(vertices, indices, origin, TopCorners, Shade(color, 1f));
                    }

                    if (IsAir(x, y - 1, z))
                    {
                        AddFace(vertices, indices, origin, BottomCorners, Shade(color, 0.45f));
                    }

                    if (IsAir(x - 1, y, z))
                    {
                        AddFace(vertices, indices, origin, NorthCorners, Shade(color, 0.7f));
                    }

                    if (IsAir(x + 1, y, z))
                    {
                        AddFace(vertices, indices, origin, SouthCorners, Shade(color, 0.8f));
                    }

                    if (IsAir(x, y, z - 1))
                    {
                        AddFace(vertices, indices, origin, WestCorners, Shade(color, 0.6f));
                    }

                    if (IsAir(x, y, z + 1))
                    {
                        AddFace(vertices, indices, origin, EastCorners, Shade(color, 0.65f));
                    }
                }
            }
        }

        _indexCount = indices.Count;

        _vertexBuffer = new VertexBuffer(
            GraphicsDevice,
            typeof(VertexPositionColor),
            vertices.Count,
            BufferUsage.WriteOnly);
        _vertexBuffer.SetData(vertices.ToArray());

        _indexBuffer = new IndexBuffer(
            GraphicsDevice,
            IndexElementSize.ThirtyTwoBits,
            _indexCount,
            BufferUsage.WriteOnly);
        _indexBuffer.SetData(indices.ToArray());
    }

    private bool IsAir(int x, int y, int z) => _world.GetBlock(x, y, z) == BlockId.Air;

    private static void AddFace(
        List<VertexPositionColor> vertices,
        List<int> indices,
        Vector3 origin,
        Vector3[] corners,
        Color color)
    {
        var start = vertices.Count;

        foreach (var corner in corners)
        {
            vertices.Add(new VertexPositionColor(origin + corner, color));
        }

        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }

    private static Color ToColor(uint rgb) =>
        new(
            (int)((rgb >> 16) & 0xFF),
            (int)((rgb >> 8) & 0xFF),
            (int)(rgb & 0xFF));

    private static Color Shade(Color color, float factor) =>
        new(
            (int)(color.R * factor),
            (int)(color.G * factor),
            (int)(color.B * factor));
}
