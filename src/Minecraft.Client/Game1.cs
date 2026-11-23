using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using LetsCreateNetworkGame.OpenGL.Manager;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Minecraft.Client.Rendering;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Entities;
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;
using NetworkPlayer = LetsCreateNetworkGame.OpenGL.Library.Player;
using NetworkPosition = LetsCreateNetworkGame.OpenGL.Library.Position;

namespace Minecraft.Client;

/// <summary>
/// The playable client: connects to the Lidgren server, streams chunks from it,
/// simulates a first-person player with Minecraft-style physics, and renders the
/// world from a procedurally generated texture atlas.
/// </summary>
public class Game1 : Game
{
    private const string Host = "127.0.0.1";
    private const int Port = 14241;
    private const string GroupId = "world";

    private const int ChunkSize = WorldProtocol.ChunkSize;
    private const float MouseSensitivity = 0.0025f;
    private const float Reach = 6f;
    private const float InterpolationDelay = 0.1f;

    private const float PlayerWidth = 0.6f;
    private const float PlayerHeight = 1.8f;
    private const float EyeHeight = 1.62f;
    private const float StepHeight = 0.6f;
    private const float Gravity = 28f;
    private const float JumpVelocity = 8.4f;
    private const float WalkSpeed = 4.3f;
    private const float SprintSpeed = 5.8f;
    private const float FlySpeed = 11f;
    private const float FlySprintSpeed = 20f;

    private const int HotbarSlots = 9;
    private const int ItemEmptyBucket = -1;
    private const int ItemWaterBucket = -2;
    private const int HotbarCell = 46;
    private const int InventoryColumns = 9;
    private const int InventoryCell = 48;

    private static readonly Color SkyColor = new(135, 206, 235);
    private static readonly Color UnderwaterColor = new(22, 48, 104);

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

    private static readonly Vector3[] UnitCorners =
    {
        new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1),
        new(0, 1, 0), new(1, 1, 0), new(1, 1, 1), new(0, 1, 1),
    };

    private static readonly int[,] BoxEdges =
    {
        { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
        { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
        { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
    };

    private const float HeadPivotY = 1.45f;
    private const float ShoulderY = 1.40f;
    private const float ShoulderX = 0.375f;
    private const float HipY = 0.75f;
    private const float HipX = 0.175f;
    private const float PlayerHeadTop = HeadPivotY + 0.5f;
    private const float NameTagHeight = PlayerHeadTop + 0.15f;

    private readonly GraphicsDeviceManager _graphics;
    private readonly ManagerNetwork _network;
    private readonly Dictionary<string, RemotePlayerVisual> _visuals =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, WorldChunkMesh> _chunkMeshes = new();
    private readonly HashSet<long> _loadedChunks = new();
    private readonly HashSet<long> _dirtyChunks = new();
    private readonly int[] _hotbar =
    {
        (int)BlockId.Grass,
        (int)BlockId.Dirt,
        (int)BlockId.Stone,
        (int)BlockId.Cobblestone,
        (int)BlockId.WoodenPlanks,
        (int)BlockId.Wood,
        (int)BlockId.Leaves,
        (int)BlockId.Sand,
        ItemEmptyBucket,
    };

    private World _world = new(16, World.DefaultHeight, 16);
    private WorldInfo? _worldInfo;
    private bool _hasSpawn;
    private Vector3 _spawnPoint;

    private bool _worldReady;
    private double _worldStartTime;
    private LightingEngine _lighting = null!;
    private int _fps;
    private int _frameCount;
    private double _fpsTimer;
    private double _networkTime;

    private BasicEffect _texturedEffect = null!;
    private BasicEffect _colorEffect = null!;
    private Texture2D _atlasTexture = null!;
    private CloudLayer _clouds = null!;

    private Vector3 _position = new(8f, 80f, 8f);
    private Vector3 _velocity;
    private float _yaw;
    private float _pitch = -0.35f;
    private bool _onGround;
    private bool _flying;
    private bool _inWater;

    private bool _mouseCentered;
    private bool _kicked;
    private bool _paused;
    private bool _inventoryOpen;
    private GameState _state = GameState.MainMenu;
    private GameMode _mode = GameMode.Creative;
    private int _menuIndex;
    private string _worldName = "New World";
    private string _worldSeed = string.Empty;
    private int _worldCreateField; // 0=name,1=seed,2=mode
    private Process _serverProcess;
    private bool _networkStarted;

    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;

    private bool _hasTarget;
    private int _targetX;
    private int _targetY;
    private int _targetZ;
    private int _targetFaceX;
    private int _targetFaceY;
    private int _targetFaceZ;

    private int _selectedSlot;
    private float _breakCooldown;
    private int _breakTargetX, _breakTargetY, _breakTargetZ;
    private float _breakProgress;
    private int _crackStage = -1;
    private VertexBuffer _crackVb;
    private IndexBuffer _crackIb;
    private int _crackIndexCount;

    // Survival health (in half-hearts, 0..20).
    private int _health = 20;
    private bool _wasOnGround;
    private float _fallStartY;

    // Dropped items (survival).
    // (removed)

    // Separate survival inventory (hotbar counts + main).
    private readonly int[] _survivalHotbarCount = new int[HotbarSlots];
    private readonly List<InventoryStack> _survivalInventory = new();
    private readonly HashSet<int> _pendingPickups = new();
    private readonly List<ClientEntity> _entities = new();
    private string _toast = string.Empty;
    private float _toastTimer;

    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;
    private Texture2D _pixel = null!;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = false;

        _network = new ManagerNetwork(Host, Port, GroupId, CreateUsername());
        _lighting = new LightingEngine(_world);
    }

    protected override void Initialize()
    {
        _graphics.SynchronizeWithVerticalRetrace = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

        _graphics.PreferredBackBufferWidth = 1152;
        _graphics.PreferredBackBufferHeight = 648;
        _graphics.ApplyChanges();

        Window.AllowUserResizing = true;
        Window.Title = "Minecraft Client - connecting";

        base.Initialize();

        // Network is started when entering the Playing state.
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("UiFont");

        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _atlasTexture = BlockAtlas.CreateTexture(GraphicsDevice);
        _clouds = new CloudLayer(GraphicsDevice);

        _texturedEffect = new BasicEffect(GraphicsDevice)
        {
            TextureEnabled = true,
            Texture = _atlasTexture,
            VertexColorEnabled = true,
            LightingEnabled = false,
            FogEnabled = true,
            FogColor = SkyColor.ToVector3(),
            FogStart = 42f,
            FogEnd = 118f,
        };

        _colorEffect = new BasicEffect(GraphicsDevice)
        {
            TextureEnabled = false,
            VertexColorEnabled = true,
            LightingEnabled = false,
            FogEnabled = false,
        };

        SelectSlot(0, announce: false);
    }

    protected override void Update(GameTime gameTime)
    {
        var deltaTime = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.1f);
        _networkTime = gameTime.TotalGameTime.TotalSeconds;

        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();

        if (_state == GameState.MainMenu)
        {
            UpdateMainMenu(keyboard);
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            base.Update(gameTime);
            return;
        }

        if (_state == GameState.WorldCreate)
        {
            UpdateWorldCreate(keyboard);
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            base.Update(gameTime);
            return;
        }

        // Playing state below (existing logic)

        HandleGlobalKeys(keyboard);
        ApplyServerMessages();

        // Decay entity hurt timers.
        foreach (var e in _entities)
        {
            if (e.HurtTimer > 0) e.HurtTimer -= deltaTime;
        }

        // Item pickup.
        if (_mode == GameMode.Survival)
        {
            foreach (var e in _entities)
            {
                if (e.Type != EntityType.Item) continue;
                if (_pendingPickups.Contains(e.Id)) continue;
                var d = Vector3.Distance(e.Position, _position + new Vector3(0, 1f, 0));
                if (d < 1.5f)
                {
                    _pendingPickups.Add(e.Id);
                    _network.SendEntityPickup(e.Id);
                }
            }
        }

        var captured = !_paused && !_inventoryOpen && !_kicked;

        if (captured)
        {
            UpdateLook();
            UpdatePlayer(deltaTime, keyboard);
            UpdateHotbarKeys(keyboard);
            UpdateTarget();
            UpdateBlockActions(mouse, deltaTime);

            _network.SetLocalPosition(
                new NetworkPosition(_position.X, _position.Y, _position.Z, _yaw, _pitch));
        }
        else if (_inventoryOpen)
        {
            UpdateInventoryClick(mouse);
        }

        if (_toastTimer > 0f)
        {
            _toastTimer -= deltaTime;
        }

        _previousMouse = mouse;
        _previousKeyboard = keyboard;

        UpdateRemotePlayers(deltaTime);

        if (_state == GameState.Playing)
        {
            UpdateLighting();
        }

        if (_worldReady)
        {
            RebuildDirtyChunks(2);
        }

        if (_state == GameState.Playing)
        {
            BlockAtlas.UpdateWaterAnimation(_atlasTexture, (float)gameTime.TotalGameTime.TotalSeconds);
        }

        base.Update(gameTime);
    }

    private class ClientEntity
    {
        public int Id;
        public EntityType Type;
        public Vector3 Position;
        public int Data;
        public float HeadYaw;
        public float HurtTimer;
    }

    private class InventoryStack
    {
        public int BlockId;
        public int Count;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _network.Dispose();

            try
            {
                if (_serverProcess is { HasExited: false })
                {
                    _serverProcess.Kill();
                    _serverProcess.WaitForExit(2000);
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }

        base.Dispose(disposing);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_state == GameState.MainMenu)
        {
            DrawMainMenu();
            base.Draw(gameTime);
            return;
        }

        if (_state == GameState.WorldCreate)
        {
            DrawWorldCreate();
            base.Draw(gameTime);
            return;
        }

        // Playing state (existing)
        _fpsTimer += gameTime.ElapsedGameTime.TotalSeconds;

        if (_fpsTimer >= 1.0)
        {
            _fps = _frameCount;
            _frameCount = 0;
            _fpsTimer -= 1.0;
        }

        var rotation = Matrix.CreateFromYawPitchRoll(_yaw, _pitch, 0);
        var eye = _position + new Vector3(0f, EyeHeight, 0f);
        var view = Matrix.CreateLookAt(eye, eye + rotation.Forward, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(70f),
            GraphicsDevice.Viewport.AspectRatio,
            0.1f,
            220f);

        var underwater = IsEyeInWater();
        var fogColor = underwater ? UnderwaterColor : SkyColor;

        GraphicsDevice.Clear(fogColor);
        GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;

        _texturedEffect.View = view;
        _texturedEffect.Projection = projection;
        _texturedEffect.World = Matrix.Identity;
        _texturedEffect.FogColor = fogColor.ToVector3();
        _texturedEffect.FogStart = underwater ? 1f : 42f;
        _texturedEffect.FogEnd = underwater ? 20f : 118f;

        _colorEffect.View = view;
        _colorEffect.Projection = projection;

        // Opaque world.
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;

        foreach (var chunk in _chunkMeshes.Values)
        {
            DrawWorldMesh(chunk.Opaque);
        }

        DrawRemotePlayers();
        DrawEntities();

        // Cloud plane: translucent, depth-tested, but never writes depth.
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
        var cloudOffset = (float)(gameTime.TotalGameTime.TotalSeconds * 1.5 % 512.0);
        _clouds.Draw(GraphicsDevice, _texturedEffect, cloudOffset);

        // Transparent world, far chunks first so water blends over distant water.
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;

        foreach (var chunk in TransparentChunks(eye))
        {
            DrawWorldMesh(chunk);
        }

        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;

        DrawSelectionOutline();
        DrawCrackOverlay();
        DrawOverlay(view, projection, rotation.Forward, underwater);

        base.Draw(gameTime);
    }

    private void DrawMainMenu()
    {
        GraphicsDevice.Clear(new Color(15, 15, 24));

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        var viewport = GraphicsDevice.Viewport;
        var title = "Minecraft";
        var titleSize = _font.MeasureString(title) * 4f;
        _spriteBatch.DrawString(_font, title, new Vector2((viewport.Width - titleSize.X) / 2f, viewport.Height * 0.18f), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0f);

        var options = new[] { "Play Creative", "Play Survival", "Quit" };
        for (var i = 0; i < options.Length; i++)
        {
            var color = i == _menuIndex ? Color.Yellow : Color.White;
            var size = _font.MeasureString(options[i]);
            var pos = new Vector2((viewport.Width - size.X) / 2f, viewport.Height * 0.45f + i * 44f);
            _spriteBatch.DrawString(_font, options[i], pos + Vector2.One, Color.Black);
            _spriteBatch.DrawString(_font, options[i], pos, color);
        }

        var hint = "Up/Down select   Enter confirm";
        var hintSize = _font.MeasureString(hint);
        _spriteBatch.DrawString(_font, hint, new Vector2((viewport.Width - hintSize.X) / 2f, viewport.Height * 0.85f), Color.Gray);

        _spriteBatch.End();
    }

    private void DrawWorldCreate()
    {
        GraphicsDevice.Clear(new Color(15, 15, 24));

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        var viewport = GraphicsDevice.Viewport;

        DrawLabel("Create World", new Vector2(viewport.Width / 2f - 60f, viewport.Height * 0.15f));

        var fields = new[] { "Name: " + _worldName, "Seed: " + _worldSeed, "Mode: " + _mode };
        for (var i = 0; i < fields.Length; i++)
        {
            var color = i == _worldCreateField ? Color.Yellow : Color.White;
            var pos = new Vector2(viewport.Width * 0.25f, viewport.Height * 0.35f + i * 48f);
            _spriteBatch.DrawString(_font, fields[i], pos + Vector2.One, Color.Black);
            _spriteBatch.DrawString(_font, fields[i], pos, color);
        }

        var hint = "Tab: next field   Enter: create   Esc: back";
        var hintSize = _font.MeasureString(hint);
        _spriteBatch.DrawString(_font, hint, new Vector2((viewport.Width - hintSize.X) / 2f, viewport.Height * 0.85f), Color.Gray);

        _spriteBatch.End();
    }

    protected override void UnloadContent()
    {
        ClearRemotePlayers();
        ClearChunkMeshes();

        _crackVb?.Dispose();
        _crackIb?.Dispose();
        _texturedEffect?.Dispose();
        _colorEffect?.Dispose();
        _pixel?.Dispose();
        _spriteBatch?.Dispose();

        base.UnloadContent();
    }

    private List<PrimitiveMesh<VertexPositionColorTexture>> TransparentChunks(Vector3 eye)
    {
        var chunks = new List<(PrimitiveMesh<VertexPositionColorTexture> Mesh, float Distance)>();

        foreach (var pair in _chunkMeshes)
        {
            var chunkX = (int)(pair.Key >> 32);
            var chunkZ = (int)(uint)pair.Key;
            var center = new Vector3(
                chunkX * ChunkSize + ChunkSize * 0.5f,
                eye.Y,
                chunkZ * ChunkSize + ChunkSize * 0.5f);

            chunks.Add((pair.Value.Transparent, Vector3.DistanceSquared(eye, center)));
        }

        chunks.Sort((a, b) => b.Distance.CompareTo(a.Distance));
        return chunks.Select(entry => entry.Mesh).ToList();
    }

    private void DrawWorldMesh(PrimitiveMesh<VertexPositionColorTexture> mesh)
    {
        if (mesh.IndexCount == 0 || mesh.Vertices is null || mesh.Indices is null)
        {
            return;
        }

        _texturedEffect.World = Matrix.Identity;
        _texturedEffect.CurrentTechnique.Passes[0].Apply();

        GraphicsDevice.SetVertexBuffer(mesh.Vertices);
        GraphicsDevice.Indices = mesh.Indices;
        GraphicsDevice.DrawIndexedPrimitives(
            PrimitiveType.TriangleList,
            0,
            0,
            mesh.IndexCount / 3);
    }

    private void DrawColorMesh(
        PrimitiveMesh<VertexPositionColor> mesh,
        Matrix world)
    {
        if (mesh.IndexCount == 0 || mesh.Vertices is null || mesh.Indices is null)
        {
            return;
        }

        _colorEffect.World = world;
        _colorEffect.CurrentTechnique.Passes[0].Apply();

        GraphicsDevice.SetVertexBuffer(mesh.Vertices);
        GraphicsDevice.Indices = mesh.Indices;
        GraphicsDevice.DrawIndexedPrimitives(
            PrimitiveType.TriangleList,
            0,
            0,
            mesh.IndexCount / 3);
    }

    private void EnterPlaying()
    {
        try
        {
            EnsureServerRunning();
        }
        catch (Exception)
        {
            // Ignore server startup errors; the client will still attempt to connect.
        }

        if (!_networkStarted)
        {
            _network.Start();
            _networkStarted = true;
        }

        _state = GameState.Playing;
        _paused = false;
        _flying = _mode == GameMode.Creative;

        if (_mode == GameMode.Survival)
        {
            _hotbar[0] = (int)BlockId.Dirt;
            _survivalHotbarCount[0] = 64;
            _hotbar[1] = (int)BlockId.Cobblestone;
            _survivalHotbarCount[1] = 64;
            _hotbar[2] = (int)BlockId.WoodenPlanks;
            _survivalHotbarCount[2] = 32;
            _health = 20;
        }

        CaptureMouse();
    }

    private void EnsureServerRunning()
    {
        var serverPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "Minecraft.Server", "bin", "Debug", "net9.0", "Minecraft.Server.exe");

        if (!File.Exists(serverPath))
        {
            serverPath = Path.Combine(AppContext.BaseDirectory, "Minecraft.Server.exe");
        }

        if (!File.Exists(serverPath))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = serverPath,
            WorkingDirectory = Path.GetDirectoryName(serverPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        if (!string.IsNullOrWhiteSpace(_worldSeed) && int.TryParse(_worldSeed, out var seed))
        {
            startInfo.Environment["MINECRAFT_SEED"] = seed.ToString();
        }

        startInfo.Environment["MINECRAFT_HEADLESS"] = "1";

        _serverProcess = Process.Start(startInfo);

        // Wait for the server to generate the world and start listening.
        // If a server was already running, this process exits quickly.
        var waited = 0;

        while (waited < 4000 && _serverProcess is { HasExited: false })
        {
            Thread.Sleep(250);
            waited += 250;
        }
    }

    private void UpdateMainMenu(KeyboardState keyboard)
    {
        if (keyboard.IsKeyDown(Keys.Up) && !_previousKeyboard.IsKeyDown(Keys.Up))
        {
            _menuIndex = Math.Max(0, _menuIndex - 1);
        }

        if (keyboard.IsKeyDown(Keys.Down) && !_previousKeyboard.IsKeyDown(Keys.Down))
        {
            _menuIndex = Math.Min(2, _menuIndex + 1);
        }

        if (keyboard.IsKeyDown(Keys.Enter) && !_previousKeyboard.IsKeyDown(Keys.Enter))
        {
            switch (_menuIndex)
            {
                case 0:
                    _mode = GameMode.Creative;
                    _state = GameState.WorldCreate;
                    break;
                case 1:
                    _mode = GameMode.Survival;
                    _state = GameState.WorldCreate;
                    break;
                case 2:
                    Exit();
                    break;
            }
        }
    }

    private void UpdateWorldCreate(KeyboardState keyboard)
    {
        // Tab cycles fields
        if (keyboard.IsKeyDown(Keys.Tab) && !_previousKeyboard.IsKeyDown(Keys.Tab))
        {
            _worldCreateField = (_worldCreateField + 1) % 3;
        }

        // Backspace
        if (keyboard.IsKeyDown(Keys.Back) && !_previousKeyboard.IsKeyDown(Keys.Back))
        {
            if (_worldCreateField == 0 && _worldName.Length > 0)
            {
                _worldName = _worldName[..^1];
            }
            else if (_worldCreateField == 1 && _worldSeed.Length > 0)
            {
                _worldSeed = _worldSeed[..^1];
            }
        }

        // Mode toggle with left/right
        if (_worldCreateField == 2)
        {
            if (keyboard.IsKeyDown(Keys.Left) && !_previousKeyboard.IsKeyDown(Keys.Left))
            {
                _mode = GameMode.Creative;
            }

            if (keyboard.IsKeyDown(Keys.Right) && !_previousKeyboard.IsKeyDown(Keys.Right))
            {
                _mode = GameMode.Survival;
            }
        }

        // Text input for name/seed
        foreach (var key in keyboard.GetPressedKeys())
        {
            if (!_previousKeyboard.IsKeyDown(key) && key >= Keys.A && key <= Keys.Z)
            {
                var ch = (char)('a' + (key - Keys.A));

                if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift))
                {
                    ch = char.ToUpperInvariant(ch);
                }

                if (_worldCreateField == 0)
                {
                    _worldName += ch;
                }
                else if (_worldCreateField == 1)
                {
                    _worldSeed += ch;
                }
            }

            if (!_previousKeyboard.IsKeyDown(key) && key >= Keys.D0 && key <= Keys.D9)
            {
                var ch = (char)('0' + (key - Keys.D0));

                if (_worldCreateField == 0)
                {
                    _worldName += ch;
                }
                else if (_worldCreateField == 1)
                {
                    _worldSeed += ch;
                }
            }

            if (!_previousKeyboard.IsKeyDown(key) && key == Keys.Space)
            {
                if (_worldCreateField == 0)
                {
                    _worldName += ' ';
                }
                else if (_worldCreateField == 1)
                {
                    _worldSeed += ' ';
                }
            }
        }

        // Enter creates the world
        if (keyboard.IsKeyDown(Keys.Enter) && !_previousKeyboard.IsKeyDown(Keys.Enter))
        {
            // Pass seed via env var for the (already running or to-be-started) server.
            if (!string.IsNullOrWhiteSpace(_worldSeed) && int.TryParse(_worldSeed, out var seed))
            {
                Environment.SetEnvironmentVariable("MINECRAFT_SEED", seed.ToString());
            }

            EnterPlaying();
        }

        // Escape back to menu
        if (keyboard.IsKeyDown(Keys.Escape) && !_previousKeyboard.IsKeyDown(Keys.Escape))
        {
            _state = GameState.MainMenu;
        }
    }

    private void HandleGlobalKeys(KeyboardState keyboard)
    {
        var escapePressed = keyboard.IsKeyDown(Keys.Escape) &&
                            !_previousKeyboard.IsKeyDown(Keys.Escape);
        var inventoryPressed = keyboard.IsKeyDown(Keys.E) &&
                               !_previousKeyboard.IsKeyDown(Keys.E);
        var quitPressed = keyboard.IsKeyDown(Keys.Q) &&
                          !_previousKeyboard.IsKeyDown(Keys.Q);

        if (escapePressed)
        {
            if (_inventoryOpen)
            {
                SetInventoryOpen(false);
            }
            else if (!_kicked)
            {
                SetPaused(!_paused);
            }
        }

        if (quitPressed && (_paused || _kicked))
        {
            Exit();
            return;
        }

        if (inventoryPressed && !_paused && !_kicked)
        {
            SetInventoryOpen(!_inventoryOpen);
        }
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        ReleaseMouse();

        if (!paused && !_inventoryOpen)
        {
            CaptureMouse();
        }
    }

    private void SetInventoryOpen(bool open)
    {
        _inventoryOpen = open;
        ReleaseMouse();

        if (!open && !_paused)
        {
            CaptureMouse();
        }
    }

    private void CaptureMouse()
    {
        IsMouseVisible = false;
        _mouseCentered = false;
        _previousMouse = Mouse.GetState();
    }

    private void ReleaseMouse()
    {
        IsMouseVisible = true;
        _mouseCentered = false;
        _previousMouse = Mouse.GetState();
    }

    private void UpdateLook()
    {
        if (!IsActive || _kicked)
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

    private void UpdateHotbarKeys(KeyboardState keyboard)
    {
        for (var i = 0; i < HotbarSlots; i++)
        {
            var key = (Keys)((int)Keys.D1 + i);

            if (keyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key))
            {
                SelectSlot(i);
            }
        }

        if (keyboard.IsKeyDown(Keys.F) && !_previousKeyboard.IsKeyDown(Keys.F) && _mode == GameMode.Creative)
        {
            _flying = !_flying;
            _velocity = Vector3.Zero;
            ShowToast(_flying ? "Flying" : "Walking");
        }
    }

    private void UpdatePlayer(float deltaTime, KeyboardState keyboard)
    {
        if (!_hasSpawn)
        {
            return;
        }

        if (!IsChunkLoadedAt(_position))
        {
            _velocity = Vector3.Zero;
            return;
        }

        if (_position.Y < -8f)
        {
            Respawn();
            return;
        }

        var forward = new Vector3(-MathF.Sin(_yaw), 0f, -MathF.Cos(_yaw));
        var right = new Vector3(MathF.Cos(_yaw), 0f, -MathF.Sin(_yaw));
        var wish = Vector3.Zero;

        if (keyboard.IsKeyDown(Keys.W)) wish += forward;
        if (keyboard.IsKeyDown(Keys.S)) wish -= forward;
        if (keyboard.IsKeyDown(Keys.D)) wish += right;
        if (keyboard.IsKeyDown(Keys.A)) wish -= right;

        if (wish.LengthSquared() > 0f)
        {
            wish.Normalize();
        }

        _inWater = IsBodyInWater();

        var sprinting = keyboard.IsKeyDown(Keys.LeftControl);
        var speed = _flying
            ? (sprinting ? FlySprintSpeed : FlySpeed)
            : (sprinting ? SprintSpeed : WalkSpeed);

        if (_inWater && !_flying)
        {
            speed *= 0.55f;
        }

        var acceleration = _flying ? 14f : _onGround ? 16f : 6f;

        if (_inWater && !_flying)
        {
            acceleration = 9f;
        }

        var blend = 1f - MathF.Exp(-acceleration * deltaTime);
        _velocity.X = MathHelper.Lerp(_velocity.X, wish.X * speed, blend);
        _velocity.Z = MathHelper.Lerp(_velocity.Z, wish.Z * speed, blend);

        if (_flying)
        {
            var vertical = 0f;

            if (keyboard.IsKeyDown(Keys.Space)) vertical += speed;
            if (keyboard.IsKeyDown(Keys.LeftShift)) vertical -= speed;

            _velocity.Y = MathHelper.Lerp(_velocity.Y, vertical, blend);
        }
        else if (_inWater)
        {
            _velocity.Y -= 9f * deltaTime;
            _velocity.Y = Math.Max(_velocity.Y, -4f);

            if (keyboard.IsKeyDown(Keys.Space))
            {
                _velocity.Y = 4.2f;
            }
        }
        else
        {
            _velocity.Y -= Gravity * deltaTime;
            _velocity.Y = Math.Max(_velocity.Y, -50f);

            if (keyboard.IsKeyDown(Keys.Space) && _onGround)
            {
                _velocity.Y = JumpVelocity;
                _onGround = false;
            }
        }

        MoveWithCollisions(_velocity * deltaTime);
        ClampToWorld();

        // Fall damage (survival).
        if (_mode == GameMode.Survival)
        {
            if (_onGround && !_wasOnGround)
            {
                var fallDist = _fallStartY - _position.Y;
                if (fallDist > 3f)
                {
                    var dmg = (int)((fallDist - 3f) * 2f);
                    _health = Math.Max(0, _health - dmg);
                    if (_health <= 0)
                    {
                        Respawn();
                        _health = 20;
                    }
                }
                _wasOnGround = true;
            }
            else if (!_onGround && _wasOnGround)
            {
                _fallStartY = _position.Y;
                _wasOnGround = false;
            }
        }
    }

    private void MoveWithCollisions(Vector3 motion)
    {
        var steps = Math.Max(1, (int)MathF.Ceiling(motion.Length() / 0.2f));
        var step = motion / steps;

        for (var i = 0; i < steps; i++)
        {
            MoveAxis(step.Y, axis: 1);
            MoveHorizontal(step.X, step.Z);
        }

        _onGround = Collides(new Vector3(_position.X, _position.Y - 0.02f, _position.Z));
    }

    private void MoveAxis(float delta, int axis)
    {
        if (MathF.Abs(delta) < 1e-6f)
        {
            return;
        }

        var previous = _position;

        if (axis == 1)
        {
            _position.Y += delta;
        }

        if (!Collides(_position))
        {
            return;
        }

        _position = previous;

        if (axis == 1)
        {
            _velocity.Y = 0f;

            if (delta < 0f)
            {
                _onGround = true;
            }
        }
    }

    private void MoveHorizontal(float dx, float dz)
    {
        if (MathF.Abs(dx) > 1e-6f)
        {
            var target = new Vector3(_position.X + dx, _position.Y, _position.Z);

            if (!Collides(target))
            {
                _position = target;
            }
            else
            {
                var step = new Vector3(target.X, target.Y + StepHeight, target.Z);

                if ((_onGround || _inWater) && !Collides(step))
                {
                    _position = step;
                }
                else
                {
                    _velocity.X = 0f;
                }
            }
        }

        if (MathF.Abs(dz) > 1e-6f)
        {
            var target = new Vector3(_position.X, _position.Y, _position.Z + dz);

            if (!Collides(target))
            {
                _position = target;
            }
            else
            {
                var step = new Vector3(target.X, target.Y + StepHeight, target.Z);

                if ((_onGround || _inWater) && !Collides(step))
                {
                    _position = step;
                }
                else
                {
                    _velocity.Z = 0f;
                }
            }
        }
    }

    private bool Collides(Vector3 feet)
    {
        const float half = PlayerWidth * 0.5f;
        const float epsilon = 0.001f;

        var minX = (int)MathF.Floor(feet.X - half + epsilon);
        var maxX = (int)MathF.Floor(feet.X + half - epsilon);
        var minY = (int)MathF.Floor(feet.Y + epsilon);
        var maxY = (int)MathF.Floor(feet.Y + PlayerHeight - epsilon);
        var minZ = (int)MathF.Floor(feet.Z - half + epsilon);
        var maxZ = (int)MathF.Floor(feet.Z + half - epsilon);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (IsSolidAt(x, y, z))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool IsSolidAt(int x, int y, int z)
    {
        if (x < 0 || z < 0 || x >= _world.SizeX || z >= _world.SizeZ || y < 0)
        {
            return true;
        }

        if (y >= _world.SizeY)
        {
            return false;
        }

        if (!_loadedChunks.Contains(ChunkKey(x / ChunkSize, z / ChunkSize)))
        {
            return true;
        }

        var block = _world.GetBlock(x, y, z);

        if (block == BlockId.Air)
        {
            return false;
        }

        return BlockRegistry.TryGet(block, out var definition) && definition.IsSolid;
    }

    private bool IsBodyInWater()
    {
        var x = (int)MathF.Floor(_position.X);
        var z = (int)MathF.Floor(_position.Z);
        var feet = (int)MathF.Floor(_position.Y + 0.2f);
        var chest = (int)MathF.Floor(_position.Y + 1.1f);

        return BlockRegistry.IsWater(_world.GetBlock(x, feet, z)) ||
               BlockRegistry.IsWater(_world.GetBlock(x, chest, z));
    }

    private bool IsEyeInWater()
    {
        var eye = _position + new Vector3(0f, EyeHeight, 0f);

        return BlockRegistry.IsWater(_world.GetBlock(
            (int)MathF.Floor(eye.X),
            (int)MathF.Floor(eye.Y),
            (int)MathF.Floor(eye.Z)));
    }

    private void ClampToWorld()
    {
        const float margin = PlayerWidth * 0.5f + 0.01f;

        _position.X = MathHelper.Clamp(_position.X, margin, _world.SizeX - margin);
        _position.Z = MathHelper.Clamp(_position.Z, margin, _world.SizeZ - margin);
    }

    private void Respawn()
    {
        _position = _spawnPoint + new Vector3(0f, 0.5f, 0f);
        _velocity = Vector3.Zero;
        _flying = false;
        _health = 20;
    }

    private bool IsChunkLoadedAt(Vector3 position)
    {
        var x = (int)MathF.Floor(position.X);
        var z = (int)MathF.Floor(position.Z);

        if (x < 0 || z < 0 || x >= _world.SizeX || z >= _world.SizeZ)
        {
            return false;
        }

        return _loadedChunks.Contains(ChunkKey(x / ChunkSize, z / ChunkSize));
    }

    private void UpdateTarget()
    {
        var eye = _position + new Vector3(0f, EyeHeight, 0f);
        var direction = Matrix.CreateFromYawPitchRoll(_yaw, _pitch, 0).Forward;

        _hasTarget = BlockRaycaster.TryHitWithFace(
            _world,
            eye.X, eye.Y, eye.Z,
            direction.X, direction.Y, direction.Z,
            Reach,
            out _targetX, out _targetY, out _targetZ,
            out _targetFaceX, out _targetFaceY, out _targetFaceZ);
    }

    private void UpdateBlockActions(MouseState mouse, float deltaTime)
    {
        _breakCooldown -= deltaTime;

        if (!IsActive || _kicked)
        {
            return;
        }

        if (mouse.ScrollWheelValue != _previousMouse.ScrollWheelValue)
        {
            var step = Math.Sign(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);

            if (step != 0)
            {
                SelectSlot((_selectedSlot + step + HotbarSlots) % HotbarSlots);
            }
        }

        var leftPressed = mouse.LeftButton == ButtonState.Pressed &&
                          _previousMouse.LeftButton != ButtonState.Pressed;
        var rightPressed = mouse.RightButton == ButtonState.Pressed &&
                           _previousMouse.RightButton != ButtonState.Pressed;
        var middlePressed = mouse.MiddleButton == ButtonState.Pressed &&
                            _previousMouse.MiddleButton != ButtonState.Pressed;
        var leftHeld = mouse.LeftButton == ButtonState.Pressed;

        // Entity attack.
        if (leftPressed && IsActive)
        {
            var eye = _position + new Vector3(0f, EyeHeight, 0f);
            var dir = Matrix.CreateFromYawPitchRoll(_yaw, _pitch, 0).Forward;
            ClientEntity nearest = null;
            var nearestDist = Reach;

            foreach (var e in _entities)
            {
                var toE = e.Position + new Vector3(0, 0.5f, 0) - eye;
                var dist = toE.Length();
                if (dist > Reach || dist < 0.1f) continue;
                var dot = Vector3.Dot(Vector3.Normalize(toE), dir);
                if (dot < 0.9f) continue;
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = e;
                }
            }

            if (nearest != null)
            {
                _network.SendEntityHit(nearest.Id);
                _breakCooldown = 0.5f;
                return;
            }
        }

        if (!_hasTarget)
        {
            return;
        }

        if (middlePressed)
        {
            PickBlock();
            return;
        }

        if ((leftPressed || (leftHeld && _breakCooldown <= 0f)) && IsActive)
        {
            if (_mode == GameMode.Creative)
            {
                _network.SendBlockChange(_targetX, _targetY, _targetZ, BlockId.Air);
                _breakCooldown = 0.05f;
                return;
            }

            // Survival: accumulate break progress.
            if (_breakTargetX != _targetX || _breakTargetY != _targetY || _breakTargetZ != _targetZ)
            {
                _breakTargetX = _targetX;
                _breakTargetY = _targetY;
                _breakTargetZ = _targetZ;
                _breakProgress = 0f;
            }

            var hardness = BlockRegistry.Hardness(_world.GetBlock(_targetX, _targetY, _targetZ));
            if (hardness <= 0f || float.IsInfinity(hardness))
            {
                return;
            }

            _breakProgress += deltaTime / hardness;

            if (_breakProgress >= 1f)
            {
                var brokenBlock = _world.GetBlock(_targetX, _targetY, _targetZ);

                _network.SendBlockChange(_targetX, _targetY, _targetZ, BlockId.Air);

                if (_mode == GameMode.Survival)
                {
                    AddToSurvivalInventory((int)brokenBlock);
                }

                _breakProgress = 0f;
                _breakCooldown = 0.1f;
            }

            return;
        }

        // Reset progress if not breaking.
        if (!leftHeld)
        {
            _breakProgress = 0f;
        }

        if (!rightPressed)
        {
            return;
        }

        var placeX = _targetX + _targetFaceX;
        var placeY = _targetY + _targetFaceY;
        var placeZ = _targetZ + _targetFaceZ;

        if (!_world.InBounds(placeX, placeY, placeZ))
        {
            return;
        }

        var existing = _world.GetBlock(placeX, placeY, placeZ);

        if (!BlockRegistry.IsReplaceable(existing))
        {
            return;
        }

        if (IntersectsPlayer(placeX, placeY, placeZ))
        {
            return;
        }

        var selected = _hotbar[_selectedSlot];

        // Water bucket handling.
        if (selected == ItemWaterBucket)
        {
            if (BlockRegistry.IsReplaceable(existing))
            {
                _network.SendBlockChange(placeX, placeY, placeZ, BlockId.Water);
                _hotbar[_selectedSlot] = ItemEmptyBucket;
            }

            return;
        }

        if (selected == ItemEmptyBucket && BlockRegistry.IsWater(existing))
        {
            _network.SendBlockChange(_targetX, _targetY, _targetZ, BlockId.Air);
            _hotbar[_selectedSlot] = ItemWaterBucket;
            return;
        }

        if (selected < 0)
        {
            return;
        }

        // Place block (creative or survival with sufficient count).
        if (selected >= 0)
        {
            if (_mode == GameMode.Survival && _survivalHotbarCount[_selectedSlot] <= 0)
            {
                return;
            }

            _network.SendBlockChange(placeX, placeY, placeZ, (BlockId)selected);

            if (_mode == GameMode.Survival)
            {
                _survivalHotbarCount[_selectedSlot]--;
                if (_survivalHotbarCount[_selectedSlot] <= 0)
                {
                    _hotbar[_selectedSlot] = -1;
                    _survivalHotbarCount[_selectedSlot] = 0;
                }
            }
        }
    }

    private void PickBlock()
    {
        var block = _world.GetBlock(_targetX, _targetY, _targetZ);

        if (!BlockRegistry.TryGet(block, out var definition) ||
            !BlockRegistry.CreativePalette.Contains(definition))
        {
            return;
        }

        _hotbar[_selectedSlot] = (int)block;
        ShowToast(definition.Name);
    }

    private bool IntersectsPlayer(int x, int y, int z)
    {
        const float half = PlayerWidth * 0.5f;

        var minX = x;
        var maxX = x + 1;
        var minY = y;
        var maxY = y + 1;
        var minZ = z;
        var maxZ = z + 1;

        var playerMinX = _position.X - half;
        var playerMaxX = _position.X + half;
        var playerMinY = _position.Y;
        var playerMaxY = _position.Y + PlayerHeight;
        var playerMinZ = _position.Z - half;
        var playerMaxZ = _position.Z + half;

        return minX < playerMaxX && maxX > playerMinX &&
               minY < playerMaxY && maxY > playerMinY &&
               minZ < playerMaxZ && maxZ > playerMinZ;
    }

    private void AddToSurvivalInventory(int blockId)
    {
        if (blockId <= 0 || blockId == (int)BlockId.Air)
        {
            return;
        }

        // Try to stack into an existing hotbar slot.
        for (var i = 0; i < HotbarSlots; i++)
        {
            if (_hotbar[i] == blockId)
            {
                _survivalHotbarCount[i]++;
                return;
            }
        }

        // Otherwise find an empty hotbar slot.
        for (var i = 0; i < HotbarSlots; i++)
        {
            if (_hotbar[i] < 0 || _survivalHotbarCount[i] <= 0)
            {
                _hotbar[i] = blockId;
                _survivalHotbarCount[i] = 1;
                return;
            }
        }

        // Hotbar full: add to main inventory.
        _survivalInventory.Add(new InventoryStack { BlockId = blockId, Count = 1 });
    }
    private void SelectSlot(int index, bool announce = true)
    {
        _selectedSlot = Math.Clamp(index, 0, HotbarSlots - 1);

        if (announce)
        {
            var name = ItemName(_hotbar[_selectedSlot]);
            ShowToast(name);
        }
    }

    private static string ItemName(int slot)
    {
        return slot switch
        {
            ItemEmptyBucket => "Empty Bucket",
            ItemWaterBucket => "Water Bucket",
            _ => BlockRegistry.Get((BlockId)slot).Name,
        };
    }

    private void ShowToast(string text)
    {
        _toast = text;
        _toastTimer = 2.2f;
    }

    private void ApplyServerMessages()
    {
        while (_network.Statuses.TryDequeue(out var status))
        {
            if (!_kicked)
            {
                Window.Title = $"Minecraft Client - {status}";
            }
        }

        while (_network.WorldInfos.TryDequeue(out var info))
        {
            InitializeWorld(info);
        }

        while (_network.Chunks.TryDequeue(out var chunk))
        {
            ApplyChunk(chunk);
        }

        while (_network.Changes.TryDequeue(out var change))
        {
            ApplyBlockChange(change);
        }

        while (_network.PlayerLists.TryDequeue(out var players))
        {
            SyncRemotePlayers(players);
        }

        while (_network.EntitySpawns.TryDequeue(out var snap))
        {
            _entities.Add(new ClientEntity
            {
                Id = snap.Id,
                Type = snap.Type,
                Position = new Vector3(snap.X, snap.Y, snap.Z),
                Data = snap.Data,
            });
        }

        while (_network.EntityUpdates.TryDequeue(out var eu))
        {
            var e = _entities.FirstOrDefault(x => x.Id == eu.Id);
            if (e != null)
            {
                e.Position = new Vector3(eu.X, eu.Y, eu.Z);
                e.HeadYaw = eu.Yaw;
            }
        }

        while (_network.EntityHits.TryDequeue(out var ehid))
        {
            var e = _entities.FirstOrDefault(x => x.Id == ehid);
            if (e != null)
            {
                e.HurtTimer = 0.5f;
            }
        }
        while (_network.EntityRemoves.TryDequeue(out var erid))
        {
            var e = _entities.FirstOrDefault(x => x.Id == erid);
            if (e != null)
            {
                // If it was an item and we're the picker, add to inventory.
                // For simplicity, add to inventory in survival when an item entity
                // we recently requested is removed.
                if (e.Type == EntityType.Item && _pendingPickups.Remove(erid))
                {
                    if (_mode == GameMode.Survival)
                    {
                        AddToSurvivalInventory(e.Data);
                    }
                }

                _entities.Remove(e);
            }
        }

        while (_network.Kicks.TryDequeue(out var username))
        {
            if (!string.Equals(username, _network.Username, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _kicked = true;
            SetPaused(true);
            Window.Title = "Minecraft Client - kicked from server";
            ClearRemotePlayers();
        }
    }

    private void InitializeWorld(WorldInfo info)
    {
        _worldInfo = info;
        _world = new World(info.SizeX, info.SizeY, info.SizeZ);
        _spawnPoint = new Vector3(info.SpawnX + 0.5f, info.SpawnY, info.SpawnZ + 0.5f);
        _position = _spawnPoint;
        _velocity = Vector3.Zero;
        _hasSpawn = true;
        _onGround = false;

        ClearChunkMeshes();
        _loadedChunks.Clear();
        _dirtyChunks.Clear();
        _lighting = new LightingEngine(_world);
        _worldReady = false;
        _worldStartTime = _networkTime;
        Window.Title = $"Minecraft Client - world {info.SizeX}x{info.SizeY}x{info.SizeZ} (seed {info.Seed})";
    }

    /// <summary>
    /// Lighting is computed once, after every chunk has arrived. Meshes are
    /// held back until then so the world appears properly lit in one go.
    /// </summary>
    private void UpdateLighting()
    {
        if (_worldReady)
        {
            return;
        }

        var totalChunks = (_world.SizeX / ChunkSize) * (_world.SizeZ / ChunkSize);

        // Wait for every chunk, but never hold the world hostage if the server
        // is slow or a packet was lost.
        if (_loadedChunks.Count < totalChunks && _networkTime - _worldStartTime < 5.0)
        {
            return;
        }

        _lighting.Initialize();
        _worldReady = true;

        foreach (var key in _loadedChunks)
        {
            _dirtyChunks.Add(key);
        }
    }

    private void ApplyChunk(ChunkSnapshot chunk)
    {
        var expected = ChunkSize * ChunkSize * _world.SizeY;

        if (chunk.Blocks.Length != expected)
        {
            return;
        }

        var baseX = chunk.ChunkX * ChunkSize;
        var baseZ = chunk.ChunkZ * ChunkSize;
        var index = 0;

        for (var x = 0; x < ChunkSize; x++)
        {
            for (var z = 0; z < ChunkSize; z++)
            {
                for (var y = 0; y < _world.SizeY; y++)
                {
                    _world.SetBlock(baseX + x, y, baseZ + z, chunk.Blocks[index++]);
                }
            }
        }

        var key = ChunkKey(chunk.ChunkX, chunk.ChunkZ);
        _loadedChunks.Add(key);
        MarkDirty(chunk.ChunkX, chunk.ChunkZ);
    }

    private void ApplyBlockChange(BlockChange change)
    {
        if (!_world.InBounds(change.X, change.Y, change.Z))
        {
            return;
        }

        var previous = _world.GetBlock(change.X, change.Y, change.Z);

        if (!_world.SetBlock(change.X, change.Y, change.Z, change.Block))
        {
            return;
        }

        var chunkX = change.X / ChunkSize;
        var chunkZ = change.Z / ChunkSize;

        MarkDirty(chunkX, chunkZ);

        // A face on a chunk border needs the neighbouring chunk rebuilt too.
        if (change.X % ChunkSize == 0) MarkDirty(chunkX - 1, chunkZ);
        if (change.X % ChunkSize == ChunkSize - 1) MarkDirty(chunkX + 1, chunkZ);
        if (change.Z % ChunkSize == 0) MarkDirty(chunkX, chunkZ - 1);
        if (change.Z % ChunkSize == ChunkSize - 1) MarkDirty(chunkX, chunkZ + 1);

        if (!_worldReady)
        {
            return;
        }

        _lighting.OnBlockChanged(change.X, change.Y, change.Z, previous, change.Block);

        foreach (var (lightChunkX, lightChunkZ) in _lighting.ChangedChunks)
        {
            MarkDirty(lightChunkX, lightChunkZ);
        }

        _lighting.ClearChangedChunks();
    }

    private void MarkDirty(int chunkX, int chunkZ)
    {
        if (chunkX < 0 || chunkZ < 0 ||
            chunkX >= _world.SizeX / ChunkSize ||
            chunkZ >= _world.SizeZ / ChunkSize)
        {
            return;
        }

        _dirtyChunks.Add(ChunkKey(chunkX, chunkZ));
    }

    private void RebuildDirtyChunks(int budget)
    {
        if (_dirtyChunks.Count == 0)
        {
            return;
        }

        var completed = new List<long>(budget);
        var built = 0;

        foreach (var key in _dirtyChunks)
        {
            if (built >= budget)
            {
                break;
            }

            completed.Add(key);

            var chunkX = (int)(key >> 32);
            var chunkZ = (int)(uint)key;

            if (!_loadedChunks.Contains(key))
            {
                continue;
            }

            if (_chunkMeshes.TryGetValue(key, out var existing))
            {
                existing.Dispose();
            }

            _chunkMeshes[key] = WorldMeshBuilder.Build(
                GraphicsDevice, _world, chunkX, chunkZ);

            built++;
        }

        foreach (var key in completed)
        {
            _dirtyChunks.Remove(key);
        }
    }

    private void ClearChunkMeshes()
    {
        foreach (var mesh in _chunkMeshes.Values)
        {
            mesh.Dispose();
        }

        _chunkMeshes.Clear();
    }

    private static long ChunkKey(int chunkX, int chunkZ) =>
        ((long)chunkX << 32) | (uint)chunkZ;

    private void SyncRemotePlayers(IReadOnlyList<NetworkPlayer> players)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var player in players)
        {
            seen.Add(player.Username);
            ApplyRemotePlayerUpdate(player);
        }

        var stale = _visuals.Keys.Where(username => !seen.Contains(username)).ToList();

        foreach (var username in stale)
        {
            _visuals[username].Dispose();
            _visuals.Remove(username);
        }
    }

    private void ApplyRemotePlayerUpdate(NetworkPlayer player)
    {
        if (string.Equals(player.Username, _network.Username, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_visuals.TryGetValue(player.Username, out var visual))
        {
            visual = new RemotePlayerVisual(GraphicsDevice, player.Username);
            _visuals.Add(player.Username, visual);
        }

        visual.AddSample(_networkTime, player.Position);
    }

    private void ClearRemotePlayers()
    {
        foreach (var visual in _visuals.Values)
        {
            visual.Dispose();
        }

        _visuals.Clear();
    }

    private void UpdateRemotePlayers(float deltaTime)
    {
        var renderTime = _networkTime - InterpolationDelay;

        foreach (var visual in _visuals.Values)
        {
            visual.Update(renderTime, deltaTime);
        }
    }

    private void DrawRemotePlayers()
    {
        foreach (var visual in _visuals.Values)
        {
            var baseWorld =
                Matrix.CreateRotationY(visual.BodyYaw) *
                Matrix.CreateTranslation(visual.RenderPosition + new Vector3(0f, visual.Bob, 0f));

            DrawColorMesh(visual.Torso, baseWorld);

            var headWorld =
                Matrix.CreateRotationX(visual.HeadPitch) *
                Matrix.CreateRotationY(visual.HeadYaw - visual.BodyYaw) *
                Matrix.CreateTranslation(0f, HeadPivotY, 0f) *
                baseWorld;

            DrawColorMesh(visual.Head, headWorld);

            var leftArm =
                Matrix.CreateRotationX(visual.Swing) *
                Matrix.CreateTranslation(-ShoulderX, ShoulderY, 0f) *
                baseWorld;

            var rightArm =
                Matrix.CreateRotationX(-visual.Swing) *
                Matrix.CreateTranslation(ShoulderX, ShoulderY, 0f) *
                baseWorld;

            DrawColorMesh(visual.LeftArm, leftArm);
            DrawColorMesh(visual.RightArm, rightArm);

            var leftLeg =
                Matrix.CreateRotationX(-visual.Swing) *
                Matrix.CreateTranslation(-HipX, HipY, 0f) *
                baseWorld;

            var rightLeg =
                Matrix.CreateRotationX(visual.Swing) *
                Matrix.CreateTranslation(HipX, HipY, 0f) *
                baseWorld;

            DrawColorMesh(visual.LeftLeg, leftLeg);
            DrawColorMesh(visual.RightLeg, rightLeg);
        }
    }

    private void DrawSelectionOutline()
    {
        if (!_hasTarget)
        {
            return;
        }

        var progress = _mode == GameMode.Survival ? MathHelper.Clamp(_breakProgress, 0f, 1f) : 0f;
        var outlineColor = Color.Lerp(Color.White, new Color(220, 40, 40), progress);

        var edgeCount = BoxEdges.GetLength(0);
        var vertices = new VertexPositionColor[edgeCount * 2];
        var min = new Vector3(_targetX - 0.005f, _targetY - 0.005f, _targetZ - 0.005f);
        var size = new Vector3(1.01f, 1.01f, 1.01f);

        for (var i = 0; i < edgeCount; i++)
        {
            var a = min + UnitCorners[BoxEdges[i, 0]] * size;
            var b = min + UnitCorners[BoxEdges[i, 1]] * size;
            vertices[i * 2] = new VertexPositionColor(a, outlineColor);
            vertices[i * 2 + 1] = new VertexPositionColor(b, outlineColor);
        }

        _colorEffect.World = Matrix.Identity;

        foreach (var pass in _colorEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.LineList,
                vertices,
                0,
                edgeCount);
        }

        // Cracking overlay: handled by DrawCrackOverlay via multiplicative blend.
    }

    private void DrawCrackOverlay()
    {
        if (_mode != GameMode.Survival || !_hasTarget || _breakProgress <= 0f)
        {
            return;
        }

        var stage = Math.Clamp((int)(_breakProgress * 9.99f), 0, 9);

        if (stage != _crackStage)
        {
            _crackStage = stage;
            RebuildCrackMesh(stage);
        }

        if (_crackVb == null || _crackIb == null || _crackIndexCount == 0)
        {
            return;
        }

        var uv = BlockAtlas.GetCrackUvRect(stage);

        var prevBlend = GraphicsDevice.BlendState;
        var prevRaster = GraphicsDevice.RasterizerState;
        var prevDepth = GraphicsDevice.DepthStencilState;

        GraphicsDevice.BlendState = new BlendState
        {
            ColorSourceBlend = Blend.DestinationColor,
            ColorDestinationBlend = Blend.SourceColor,
            AlphaSourceBlend = Blend.DestinationAlpha,
            AlphaDestinationBlend = Blend.SourceAlpha,
        };
        GraphicsDevice.RasterizerState = new RasterizerState
        {
            DepthBias = -1,
            SlopeScaleDepthBias = -1,
            CullMode = CullMode.CullCounterClockwiseFace,
        };
        GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;

        _texturedEffect.Texture = _atlasTexture;
        _texturedEffect.World = Matrix.CreateTranslation(_targetX, _targetY, _targetZ);
        _texturedEffect.CurrentTechnique.Passes[0].Apply();

        GraphicsDevice.SetVertexBuffer(_crackVb);
        GraphicsDevice.Indices = _crackIb;
        GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _crackIndexCount / 3);

        GraphicsDevice.BlendState = prevBlend;
        GraphicsDevice.RasterizerState = prevRaster;
        GraphicsDevice.DepthStencilState = prevDepth;
    }

    private void RebuildCrackMesh(int stage)
    {
        _crackVb?.Dispose();
        _crackIb?.Dispose();
        _crackVb = null;
        _crackIb = null;
        _crackIndexCount = 0;

        var uv = BlockAtlas.GetCrackUvRect(stage);

        var verts = new List<VertexPositionColorTexture>();
        var inds = new List<int>();

        void AddFace(Vector3[] corners, Vector2[] pattern)
        {
            var start = verts.Count;
            for (var i = 0; i < 4; i++)
            {
                verts.Add(new VertexPositionColorTexture(
                    corners[i],
                    Color.White,
                    new Vector2(
                        MathHelper.Lerp(uv.X, uv.Z, pattern[i].X),
                        MathHelper.Lerp(uv.Y, uv.W, pattern[i].Y))));
            }
            inds.Add(start); inds.Add(start + 1); inds.Add(start + 2);
            inds.Add(start); inds.Add(start + 2); inds.Add(start + 3);
        }

        var top = new[] { new Vector3(0,1,0), new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0) };
        var bottom = new[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) };
        var north = new[] { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) };
        var south = new[] { new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1), new Vector3(0,0,1) };
        var west = new[] { new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) };
        var east = new[] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) };

        var hUvs = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
        var sUvs = new[] { new Vector2(0,1), new Vector2(0,0), new Vector2(1,0), new Vector2(1,1) };

        AddFace(top, hUvs);
        AddFace(bottom, hUvs);
        AddFace(north, sUvs);
        AddFace(south, sUvs);
        AddFace(west, sUvs);
        AddFace(east, sUvs);

        _crackVb = new VertexBuffer(GraphicsDevice, typeof(VertexPositionColorTexture), verts.Count, BufferUsage.WriteOnly);
        _crackVb.SetData(verts.ToArray());
        _crackIb = new IndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, inds.Count, BufferUsage.WriteOnly);
        _crackIb.SetData(inds.Select(i => (short)i).ToArray());
        _crackIndexCount = inds.Count;
    }

    private void DrawEntities()
    {
        foreach (var e in _entities)
        {
            var pos = e.Position;
            var verts = new List<VertexPositionColor>();
            var inds = new List<int>();

            void AddBox(Vector3 origin, Vector3 min, Vector3 size, Color color)
            {
                var o = origin + min;
                var start = verts.Count;
                // 8 corners
                var c = new[]
                {
                    new Vector3(0,0,0), new Vector3(size.X,0,0), new Vector3(size.X,0,size.Z), new Vector3(0,0,size.Z),
                    new Vector3(0,size.Y,0), new Vector3(size.X,size.Y,0), new Vector3(size.X,size.Y,size.Z), new Vector3(0,size.Y,size.Z),
                };
                for (var i = 0; i < 8; i++) verts.Add(new VertexPositionColor(o + c[i], color));
                // 12 triangles (2 per face)
                int[] tris = { 0,1,2, 0,2,3, 4,6,5, 4,7,6, 0,4,5, 0,5,1, 2,6,7, 2,7,3, 0,3,7, 0,7,4, 1,5,6, 1,6,2 };
                foreach (var t in tris) inds.Add(start + t);
            }

            var hurt = e.HurtTimer > 0;

            if (e.Type == EntityType.Pig)
            {
                // Body
                AddBox(pos, new Vector3(-0.45f, 0.4f, -0.3f), new Vector3(0.9f, 0.5f, 0.6f), new Color(240, 150, 150));
                // Head
                AddBox(pos, new Vector3(-0.75f, 0.5f, -0.25f), new Vector3(0.35f, 0.35f, 0.5f), new Color(240, 150, 150));
                // Legs
                AddBox(pos, new Vector3(-0.35f, 0f, -0.2f), new Vector3(0.15f, 0.4f, 0.15f), new Color(230, 140, 140));
                AddBox(pos, new Vector3(0.2f, 0f, -0.2f), new Vector3(0.15f, 0.4f, 0.15f), new Color(230, 140, 140));
                AddBox(pos, new Vector3(-0.35f, 0f, 0.05f), new Vector3(0.15f, 0.4f, 0.15f), new Color(230, 140, 140));
                AddBox(pos, new Vector3(0.2f, 0f, 0.05f), new Vector3(0.15f, 0.4f, 0.15f), new Color(230, 140, 140));
            }
            else if (e.Type == EntityType.Sheep)
            {
                // Wool body (fluffy)
                AddBox(pos, new Vector3(-0.5f, 0.5f, -0.4f), new Vector3(1.0f, 0.7f, 0.8f), new Color(240, 240, 235));
                // Head
                AddBox(pos, new Vector3(-0.8f, 0.5f, -0.25f), new Vector3(0.3f, 0.35f, 0.5f), new Color(180, 180, 170));
                // Legs
                AddBox(pos, new Vector3(-0.4f, 0f, -0.25f), new Vector3(0.12f, 0.5f, 0.12f), new Color(80, 80, 70));
                AddBox(pos, new Vector3(0.28f, 0f, -0.25f), new Vector3(0.12f, 0.5f, 0.12f), new Color(80, 80, 70));
                AddBox(pos, new Vector3(-0.4f, 0f, 0.13f), new Vector3(0.12f, 0.5f, 0.12f), new Color(80, 80, 70));
                AddBox(pos, new Vector3(0.28f, 0f, 0.13f), new Vector3(0.12f, 0.5f, 0.12f), new Color(80, 80, 70));
            }
            else if (e.Type == EntityType.Cow)
            {
                AddBox(pos, new Vector3(-0.5f, 0.5f, -0.4f), new Vector3(1.0f, 0.6f, 0.8f), new Color(120, 80, 50));
                AddBox(pos, new Vector3(-0.8f, 0.5f, -0.25f), new Vector3(0.3f, 0.35f, 0.5f), new Color(120, 80, 50));
                AddBox(pos, new Vector3(-0.4f, 0f, -0.25f), new Vector3(0.12f, 0.5f, 0.12f), new Color(80, 60, 40));
                AddBox(pos, new Vector3(0.28f, 0f, -0.25f), new Vector3(0.12f, 0.5f, 0.12f), new Color(80, 60, 40));
                AddBox(pos, new Vector3(-0.4f, 0f, 0.13f), new Vector3(0.12f, 0.5f, 0.12f), new Color(80, 60, 40));
                AddBox(pos, new Vector3(0.28f, 0f, 0.13f), new Vector3(0.12f, 0.5f, 0.12f), new Color(80, 60, 40));
            }
            else // Item
            {
                AddBox(pos, new Vector3(-0.15f, 0f, -0.15f), new Vector3(0.3f, 0.3f, 0.3f), Color.White);
            }

            if (verts.Count == 0) continue;

            _colorEffect.World = Matrix.Identity;
            foreach (var pass in _colorEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    verts.ToArray(), 0, verts.Count,
                    inds.ToArray(), 0, inds.Count / 3);
            }

            if (hurt)
            {
                // Red flash overlay.
                var he = new VertexPositionColor[24];
                var hb = new Vector3(pos.X - 0.6f, pos.Y, pos.Z - 0.5f);
                var hs = new Vector3(1.2f, 1.2f, 1.0f);
                var hc = new[]
                {
                    new Vector3(0,0,0), new Vector3(hs.X,0,0), new Vector3(hs.X,0,hs.Z), new Vector3(0,0,hs.Z),
                    new Vector3(0,hs.Y,0), new Vector3(hs.X,hs.Y,0), new Vector3(hs.X,hs.Y,hs.Z), new Vector3(0,hs.Y,hs.Z),
                };
                var hedge = new (int,int)[]
                {
                    (0,1),(1,2),(2,3),(3,0),(4,5),(5,6),(6,7),(7,4),(0,4),(1,5),(2,6),(3,7),
                };
                for (var i = 0; i < hedge.Length; i++)
                {
                    he[i*2] = new VertexPositionColor(hb + hc[hedge[i].Item1], new Color(255, 60, 60));
                    he[i*2+1] = new VertexPositionColor(hb + hc[hedge[i].Item2], new Color(255, 60, 60));
                }
                _colorEffect.World = Matrix.Identity;
                foreach (var pass in _colorEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, he, 0, hedge.Length);
                }
            }
        }
    }

    private void DrawOverlay(Matrix view, Matrix projection, Vector3 forward, bool underwater)
    {
        var viewport = GraphicsDevice.Viewport;

        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);

        if (underwater)
        {
            _spriteBatch.Draw(
                _pixel,
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                new Color(24, 62, 140, 90));
        }

        foreach (var visual in _visuals.Values)
        {
            var anchor = visual.RenderPosition +
                         new Vector3(0f, NameTagHeight + visual.Bob, 0f);

            if (Vector3.Dot(anchor - (_position + new Vector3(0f, EyeHeight, 0f)), forward) <= 0f)
            {
                continue;
            }

            var screen = viewport.Project(anchor, projection, Matrix.Identity, view);

            if (screen.Z < 0f || screen.Z > 1f)
            {
                continue;
            }

            var text = visual.Username;
            var size = _font.MeasureString(text);
            var x = MathF.Round(MathHelper.Clamp(
                screen.X - size.X * 0.5f,
                0f,
                Math.Max(0f, viewport.Width - size.X)));
            var y = MathF.Round(MathHelper.Clamp(
                screen.Y - size.Y - 8f,
                0f,
                Math.Max(0f, viewport.Height - size.Y)));

            _spriteBatch.Draw(
                _pixel,
                new Rectangle((int)x - 4, (int)y - 2, (int)size.X + 8, (int)size.Y + 4),
                new Color(0, 0, 0, 160));

            DrawLabel(text, new Vector2(x, y));
        }

        if (!_paused && !_inventoryOpen)
        {
            DrawCrosshair(viewport);
        }

        if (_mode == GameMode.Survival && _state == GameState.Playing)
        {
            var heartSize = 16;
            var startX = 12;
            var y = viewport.Height - HotbarCell - 16 - 28;
            for (var i = 0; i < 10; i++)
            {
                var x = startX + i * (heartSize + 2);
                var full = _health >= (i + 1) * 2;
                var half = _health == (i + 1) * 2 - 1;
                var color = full ? new Color(220, 40, 40) : half ? new Color(220, 120, 120) : new Color(60, 20, 20);
                _spriteBatch.Draw(_pixel, new Rectangle(x, y, heartSize, heartSize), color);
                _spriteBatch.Draw(_pixel, new Rectangle(x, y, heartSize, 2), new Color(0, 0, 0, 160));
            }
        }

        DrawHotbar(viewport);

        DrawLabel($"FPS: {_fps}   XYZ: {_position.X:0.0} {_position.Y:0.0} {_position.Z:0.0}", new Vector2(12f, 12f));
        DrawLabel(_flying ? "Mode: flying" : _inWater ? "Mode: swimming" : "Mode: walking", new Vector2(12f, 36f));
        DrawLabel($"You: {_network.Username}", new Vector2(12f, 60f));

        var hint = "WASD move | Space jump | Ctrl sprint | F fly | E inventory | LMB break | RMB place";
        DrawLabel(hint, new Vector2(12f, viewport.Height - HotbarCell - 34f));

        if (_toastTimer > 0f && !string.IsNullOrEmpty(_toast) && !_inventoryOpen)
        {
            var size = _font.MeasureString(_toast);
            var x = MathF.Round((viewport.Width - size.X) / 2f);
            var y = viewport.Height - HotbarCell - 64f;
            _spriteBatch.Draw(
                _pixel,
                new Rectangle((int)x - 6, (int)y - 2, (int)size.X + 12, (int)size.Y + 4),
                new Color(0, 0, 0, 140));
            DrawLabel(_toast, new Vector2(x, y));
        }

        if (_inventoryOpen)
        {
            DrawInventory(viewport);
        }

        if (_paused)
        {
            DrawPause(viewport);
        }

        _spriteBatch.End();
    }

    private void DrawCrosshair(Viewport viewport)
    {
        var centerX = viewport.Width / 2;
        var centerY = viewport.Height / 2;

        _spriteBatch.Draw(_pixel, new Rectangle(centerX - 10, centerY, 7, 2), Color.White);
        _spriteBatch.Draw(_pixel, new Rectangle(centerX + 3, centerY, 7, 2), Color.White);
        _spriteBatch.Draw(_pixel, new Rectangle(centerX, centerY - 10, 2, 7), Color.White);
        _spriteBatch.Draw(_pixel, new Rectangle(centerX, centerY + 3, 2, 7), Color.White);
    }

    private void DrawHotbar(Viewport viewport)
    {
        var totalWidth = HotbarSlots * HotbarCell + (HotbarSlots - 1) * 4;
        var startX = (viewport.Width - totalWidth) / 2;
        var y = viewport.Height - HotbarCell - 16;

        for (var i = 0; i < HotbarSlots; i++)
        {
            var x = startX + i * (HotbarCell + 4);
            var cell = new Rectangle(x, y, HotbarCell, HotbarCell);

            _spriteBatch.Draw(_pixel, cell, new Color(0, 0, 0, 130));
            _spriteBatch.Draw(_pixel, new Rectangle(x, y, HotbarCell, 2), new Color(80, 80, 80, 170));

            DrawBlockIcon(_hotbar[i], new Rectangle(x + 6, y + 6, HotbarCell - 12, HotbarCell - 12));

            if (_mode == GameMode.Survival && _survivalHotbarCount[i] > 0)
            {
                var countText = _survivalHotbarCount[i].ToString();
                var countSize = _font.MeasureString(countText);
                _spriteBatch.DrawString(_font, countText, new Vector2(x + HotbarCell - countSize.X - 4, y + HotbarCell - countSize.Y - 2) + Vector2.One, Color.Black);
                _spriteBatch.DrawString(_font, countText, new Vector2(x + HotbarCell - countSize.X - 4, y + HotbarCell - countSize.Y - 2), Color.White);
            }

            if (i == _selectedSlot)
            {
                var border = 3;
                _spriteBatch.Draw(_pixel, new Rectangle(x, y, HotbarCell, border), Color.White);
                _spriteBatch.Draw(_pixel, new Rectangle(x, y + HotbarCell - border, HotbarCell, border), Color.White);
                _spriteBatch.Draw(_pixel, new Rectangle(x, y, border, HotbarCell), Color.White);
                _spriteBatch.Draw(_pixel, new Rectangle(x + HotbarCell - border, y, border, HotbarCell), Color.White);
            }
        }
    }

    private void DrawSurvivalInventory(Viewport viewport)
    {
        var padding = 16;
        var cell = InventoryCell;
        var columns = InventoryColumns;
        var totalSlots = HotbarSlots + _survivalInventory.Count;
        var rows = (totalSlots + columns - 1) / columns;
        var header = 60;
        var panelWidth = columns * cell + padding * 2;
        var panelHeight = rows * cell + header + padding;
        var panel = new Rectangle(
            (viewport.Width - panelWidth) / 2,
            (viewport.Height - panelHeight) / 2,
            panelWidth,
            panelHeight);

        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, viewport.Width, viewport.Height), new Color(0, 0, 0, 140));
        _spriteBatch.Draw(_pixel, panel, new Color(28, 28, 34, 240));
        DrawPanelBorder(panel, new Color(180, 180, 190));

        DrawLabel("Inventory", new Vector2(panel.X + padding, panel.Y + 10f));
        DrawLabel("Click a slot to move it to the selected hotbar slot", new Vector2(panel.X + padding, panel.Y + 34f));

        // Hotbar row first.
        for (var i = 0; i < HotbarSlots; i++)
        {
            var x = panel.X + padding + (i % columns) * cell;
            var y = panel.Y + header + (i / columns) * cell;
            var rect = new Rectangle(x + 3, y + 3, cell - 6, cell - 6);
            _spriteBatch.Draw(_pixel, rect, new Color(52, 52, 60, 230));
            if (_hotbar[i] >= 0 && _survivalHotbarCount[i] > 0)
            {
                DrawBlockIcon(_hotbar[i], new Rectangle(x + 8, y + 8, cell - 16, cell - 16));
                var countText = _survivalHotbarCount[i].ToString();
                var countSize = _font.MeasureString(countText);
                _spriteBatch.DrawString(_font, countText, new Vector2(x + cell - countSize.X - 6, y + cell - countSize.Y - 4) + Vector2.One, Color.Black);
                _spriteBatch.DrawString(_font, countText, new Vector2(x + cell - countSize.X - 6, y + cell - countSize.Y - 4), Color.White);
            }
        }

        // Main inventory.
        var offset = HotbarSlots;
        for (var i = 0; i < _survivalInventory.Count; i++)
        {
            var idx = offset + i;
            var x = panel.X + padding + (idx % columns) * cell;
            var y = panel.Y + header + (idx / columns) * cell;
            var rect = new Rectangle(x + 3, y + 3, cell - 6, cell - 6);
            _spriteBatch.Draw(_pixel, rect, new Color(52, 52, 60, 230));
            var stack = _survivalInventory[i];
            DrawBlockIcon(stack.BlockId, new Rectangle(x + 8, y + 8, cell - 16, cell - 16));
            var countText = stack.Count.ToString();
            var countSize = _font.MeasureString(countText);
            _spriteBatch.DrawString(_font, countText, new Vector2(x + cell - countSize.X - 6, y + cell - countSize.Y - 4) + Vector2.One, Color.Black);
            _spriteBatch.DrawString(_font, countText, new Vector2(x + cell - countSize.X - 6, y + cell - countSize.Y - 4), Color.White);
        }
    }

    private void DrawInventory(Viewport viewport)
    {
        if (_mode == GameMode.Survival)
        {
            DrawSurvivalInventory(viewport);
            return;
        }

        var palette = BlockRegistry.CreativePalette;
        var rows = (palette.Count + InventoryColumns - 1) / InventoryColumns;
        var padding = 16;
        var header = 84;
        var panelWidth = InventoryColumns * InventoryCell + padding * 2;
        var panelHeight = rows * InventoryCell + header;
        var panel = new Rectangle(
            (viewport.Width - panelWidth) / 2,
            (viewport.Height - panelHeight) / 2,
            panelWidth,
            panelHeight);

        _spriteBatch.Draw(
            _pixel,
            new Rectangle(0, 0, viewport.Width, viewport.Height),
            new Color(0, 0, 0, 140));

        _spriteBatch.Draw(_pixel, panel, new Color(28, 28, 34, 240));
        DrawPanelBorder(panel, new Color(180, 180, 190));

        DrawLabel("Inventory", new Vector2(panel.X + padding, panel.Y + 10f));
        DrawLabel(
            $"Slot {_selectedSlot + 1}  |  Click a block, E or Esc to close",
            new Vector2(panel.X + padding, panel.Y + 34f));

        for (var i = 0; i < palette.Count; i++)
        {
            var column = i % InventoryColumns;
            var row = i / InventoryColumns;
            var x = panel.X + padding + column * InventoryCell;
            var y = panel.Y + header + row * InventoryCell;
            var cell = new Rectangle(x + 3, y + 3, InventoryCell - 6, InventoryCell - 6);

            _spriteBatch.Draw(_pixel, cell, new Color(52, 52, 60, 230));
            DrawBlockIcon((int)palette[i].Id, new Rectangle(x + 8, y + 8, InventoryCell - 16, InventoryCell - 16));
        }
    }

    private void DrawPause(Viewport viewport)
    {
        _spriteBatch.Draw(
            _pixel,
            new Rectangle(0, 0, viewport.Width, viewport.Height),
            new Color(0, 0, 0, 150));

        const float titleScale = 3f;
        var title = "PAUSED";
        var titleSize = _font.MeasureString(title) * titleScale;

        _spriteBatch.DrawString(
            _font,
            title,
            new Vector2(
                MathF.Round((viewport.Width - titleSize.X) / 2f),
                MathF.Round((viewport.Height - titleSize.Y) / 2f - 28f)),
            Color.White,
            0f,
            Vector2.Zero,
            titleScale,
            SpriteEffects.None,
            0f);

        var hint = _kicked
            ? "You were kicked - press Q to quit"
            : "Esc to resume    |    Q to quit";
        var hintSize = _font.MeasureString(hint);

        DrawLabel(
            hint,
            new Vector2(
                MathF.Round((viewport.Width - hintSize.X) / 2f),
                MathF.Round((viewport.Height - hintSize.Y) / 2f + 24f)));
    }

    private void DrawBlockIcon(int slot, Rectangle destination)
    {
        if (slot == ItemEmptyBucket)
        {
            DrawItemIcon("B", destination, new Color(180, 180, 190));
            return;
        }

        if (slot == ItemWaterBucket)
        {
            DrawItemIcon("W", destination, new Color(63, 118, 228));
            return;
        }

        if (!BlockRegistry.TryGet((BlockId)slot, out var definition))
        {
            return;
        }

        var tint = definition.Tint;
        var color = new Color(
            (int)((tint >> 16) & 0xFF),
            (int)((tint >> 8) & 0xFF),
            (int)(tint & 0xFF));

        var source = BlockAtlas.GetSourceRect(definition.Top);
        _spriteBatch.Draw(_atlasTexture, destination, source, color);
    }

    private void DrawItemIcon(string label, Rectangle destination, Color color)
    {
        _spriteBatch.Draw(_pixel, destination, color);
        var size = _font.MeasureString(label);
        var pos = new Vector2(
            destination.X + (destination.Width - size.X) / 2f,
            destination.Y + (destination.Height - size.Y) / 2f);
        _spriteBatch.DrawString(_font, label, pos + Vector2.One, Color.Black);
        _spriteBatch.DrawString(_font, label, pos, Color.White);
    }

    private void DrawPanelBorder(Rectangle panel, Color color)
    {
        const int width = 2;

        _spriteBatch.Draw(_pixel, new Rectangle(panel.X, panel.Y, panel.Width, width), color);
        _spriteBatch.Draw(_pixel, new Rectangle(panel.X, panel.Bottom - width, panel.Width, width), color);
        _spriteBatch.Draw(_pixel, new Rectangle(panel.X, panel.Y, width, panel.Height), color);
        _spriteBatch.Draw(_pixel, new Rectangle(panel.Right - width, panel.Y, width, panel.Height), color);
    }

    private void UpdateSurvivalInventoryClick(MouseState mouse)
    {
        var viewport = GraphicsDevice.Viewport;
        var cell = InventoryCell;
        var columns = InventoryColumns;
        var padding = 16;
        var header = 60;
        var totalSlots = HotbarSlots + _survivalInventory.Count;
        var rows = (totalSlots + columns - 1) / columns;
        var panelWidth = columns * cell + padding * 2;
        var panelHeight = rows * cell + header + padding;
        var panel = new Rectangle(
            (viewport.Width - panelWidth) / 2,
            (viewport.Height - panelHeight) / 2,
            panelWidth,
            panelHeight);

        var localX = mouse.X - (panel.X + padding);
        var localY = mouse.Y - (panel.Y + header);

        if (localX < 0 || localY < 0 ||
            localX >= columns * cell ||
            localY >= rows * cell)
        {
            return;
        }

        var column = localX / cell;
        var row = localY / cell;
        var index = row * columns + column;

        if (index < 0 || index >= totalSlots)
        {
            return;
        }

        int blockId;
        int count;

        if (index < HotbarSlots)
        {
            blockId = _hotbar[index];
            count = _survivalHotbarCount[index];
        }
        else
        {
            var si = index - HotbarSlots;
            if (si >= _survivalInventory.Count)
            {
                return;
            }

            blockId = _survivalInventory[si].BlockId;
            count = _survivalInventory[si].Count;
        }

        if (blockId < 0 || count <= 0)
        {
            return;
        }

        // Move to selected hotbar slot.
        if (_hotbar[_selectedSlot] == blockId)
        {
            _survivalHotbarCount[_selectedSlot] += count;
        }
        else
        {
            // If selected slot has something, swap into main inventory.
            if (_hotbar[_selectedSlot] >= 0 && _survivalHotbarCount[_selectedSlot] > 0)
            {
                _survivalInventory.Add(new InventoryStack
                {
                    BlockId = _hotbar[_selectedSlot],
                    Count = _survivalHotbarCount[_selectedSlot],
                });
            }

            _hotbar[_selectedSlot] = blockId;
            _survivalHotbarCount[_selectedSlot] = count;
        }

        // Remove from source.
        if (index < HotbarSlots)
        {
            _hotbar[index] = -1;
            _survivalHotbarCount[index] = 0;
        }
        else
        {
            _survivalInventory.RemoveAt(index - HotbarSlots);
        }

        SetInventoryOpen(false);
    }

    private void UpdateInventoryClick(MouseState mouse)
    {
        if (mouse.LeftButton != ButtonState.Pressed ||
            _previousMouse.LeftButton == ButtonState.Pressed)
        {
            return;
        }

        if (_mode == GameMode.Survival)
        {
            UpdateSurvivalInventoryClick(mouse);
            return;
        }

        var viewport = GraphicsDevice.Viewport;
        var palette = BlockRegistry.CreativePalette;
        var rows = (palette.Count + InventoryColumns - 1) / InventoryColumns;
        var padding = 16;
        var header = 84;
        var panelWidth = InventoryColumns * InventoryCell + padding * 2;
        var panelHeight = rows * InventoryCell + header;
        var panel = new Rectangle(
            (viewport.Width - panelWidth) / 2,
            (viewport.Height - panelHeight) / 2,
            panelWidth,
            panelHeight);

        var localX = mouse.X - (panel.X + padding);
        var localY = mouse.Y - (panel.Y + header);

        if (localX < 0 || localY < 0 ||
            localX >= InventoryColumns * InventoryCell ||
            localY >= rows * InventoryCell)
        {
            return;
        }

        var column = localX / InventoryCell;
        var row = localY / InventoryCell;
        var index = row * InventoryColumns + column;

        if (index < 0 || index >= palette.Count)
        {
            return;
        }

        _hotbar[_selectedSlot] = (int)palette[index].Id;
        ShowToast(palette[index].Name);
        SetInventoryOpen(false);
    }

    private void DrawLabel(string text, Vector2 position)
    {
        var rounded = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
        _spriteBatch.DrawString(_font, text, rounded + Vector2.One, Color.Black);
        _spriteBatch.DrawString(_font, text, rounded, Color.White);
    }

    private static void AddBox(
        List<VertexPositionColor> vertices,
        List<int> indices,
        Vector3 origin,
        Vector3 min,
        Vector3 size,
        Color color)
    {
        var boxMin = origin + min;

        AddBoxFace(vertices, indices, boxMin, size, TopCorners, Shade(color, 1f));
        AddBoxFace(vertices, indices, boxMin, size, BottomCorners, Shade(color, 0.45f));
        AddBoxFace(vertices, indices, boxMin, size, NorthCorners, Shade(color, 0.7f));
        AddBoxFace(vertices, indices, boxMin, size, SouthCorners, Shade(color, 0.8f));
        AddBoxFace(vertices, indices, boxMin, size, WestCorners, Shade(color, 0.6f));
        AddBoxFace(vertices, indices, boxMin, size, EastCorners, Shade(color, 0.65f));
    }

    private static void AddBoxFace(
        List<VertexPositionColor> vertices,
        List<int> indices,
        Vector3 boxMin,
        Vector3 size,
        Vector3[] corners,
        Color color)
    {
        var start = vertices.Count;

        foreach (var corner in corners)
        {
            vertices.Add(new VertexPositionColor(boxMin + corner * size, color));
        }

        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }

    private static Color Shade(Color color, float factor) =>
        new(
            (int)(color.R * factor),
            (int)(color.G * factor),
            (int)(color.B * factor),
            color.A);

    private static Color PlayerColor(string username)
    {
        var hash = username.GetHashCode();
        var hue = Math.Abs(hash % 360) * MathF.PI / 180f;
        var red = (int)(MathF.Abs(MathF.Sin(hue)) * 200);
        var blue = (int)(MathF.Abs(MathF.Cos(hue)) * 200);
        return new Color(red, 90, blue);
    }

    private static float YawFromDirection(Vector3 direction) =>
        MathF.Atan2(-direction.X, -direction.Z);

    private static float LerpAngle(float from, float to, float t) =>
        from + MathHelper.WrapAngle(to - from) * t;

    private static string CreateUsername()
    {
        var random = new Random();
        var prefix = string.IsNullOrWhiteSpace(Environment.UserName)
            ? "player"
            : new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray());

        if (string.IsNullOrEmpty(prefix))
        {
            prefix = "player";
        }

        var suffix = random.Next(1000, 9999).ToString();
        var maxPrefix = Math.Min(prefix.Length, 16 - suffix.Length);
        return prefix[..maxPrefix] + suffix;
    }

    /// <summary>
    /// Interpolated remote player model. Network samples are rendered slightly in
    /// the past; the body turns toward travel and the head keeps the aim.
    /// </summary>
    private sealed class RemotePlayerVisual : IDisposable
    {
        private const int MaxSamples = 64;
        private const float TeleportDistance = 5f;
        private const float MaxHeadTurn = 1.3f;

        private readonly List<Sample> _samples = new();
        private float _walkPhase;

        public RemotePlayerVisual(GraphicsDevice device, string username)
        {
            Username = username;

            var color = PlayerColor(username);
            var headColor = Color.Lerp(color, Color.White, 0.25f);

            Torso = CreatePart(
                device, new Vector3(-0.30f, 0.75f, -0.15f), new Vector3(0.60f, 0.70f, 0.30f), color);
            Head = CreatePart(
                device, new Vector3(-0.25f, 0f, -0.25f), new Vector3(0.50f, 0.50f, 0.50f), headColor);
            LeftArm = CreatePart(
                device, new Vector3(-0.075f, -0.60f, -0.15f), new Vector3(0.15f, 0.60f, 0.30f), Shade(color, 0.9f));
            RightArm = CreatePart(
                device, new Vector3(-0.075f, -0.60f, -0.15f), new Vector3(0.15f, 0.60f, 0.30f), Shade(color, 0.9f));
            LeftLeg = CreatePart(
                device, new Vector3(-0.125f, -0.75f, -0.15f), new Vector3(0.25f, 0.75f, 0.30f), Shade(color, 0.8f));
            RightLeg = CreatePart(
                device, new Vector3(-0.125f, -0.75f, -0.15f), new Vector3(0.25f, 0.75f, 0.30f), Shade(color, 0.8f));
        }

        public string Username { get; }

        public PrimitiveMesh<VertexPositionColor> Torso { get; }

        public PrimitiveMesh<VertexPositionColor> Head { get; }

        public PrimitiveMesh<VertexPositionColor> LeftArm { get; }

        public PrimitiveMesh<VertexPositionColor> RightArm { get; }

        public PrimitiveMesh<VertexPositionColor> LeftLeg { get; }

        public PrimitiveMesh<VertexPositionColor> RightLeg { get; }

        public Vector3 RenderPosition { get; private set; }

        public float BodyYaw { get; private set; }

        public float HeadYaw { get; private set; }

        public float HeadPitch { get; private set; }

        public float Swing { get; private set; }

        public float Bob { get; private set; }

        public void AddSample(double time, NetworkPosition position)
        {
            var sample = new Sample(
                time,
                new Vector3(position.XPosition, position.YPosition, position.ZPosition),
                position.Yaw,
                position.Pitch);

            if (_samples.Count == 0 ||
                Vector3.Distance(_samples[^1].Position, sample.Position) > TeleportDistance)
            {
                _samples.Clear();
                _samples.Add(sample);
                RenderPosition = sample.Position;
                BodyYaw = sample.Yaw;
                HeadYaw = sample.Yaw;
                _walkPhase = 0f;
                return;
            }

            _samples.Add(sample);

            if (_samples.Count > MaxSamples)
            {
                _samples.RemoveAt(0);
            }
        }

        public void Update(double renderTime, float deltaTime)
        {
            if (_samples.Count == 0)
            {
                return;
            }

            while (_samples.Count > 2 && _samples[1].Time < renderTime)
            {
                _samples.RemoveAt(0);
            }

            Sample from;
            Sample to;

            if (renderTime <= _samples[0].Time)
            {
                from = to = _samples[0];
            }
            else if (renderTime >= _samples[^1].Time)
            {
                from = to = _samples[^1];
            }
            else
            {
                var index = 0;

                while (index < _samples.Count - 2 && _samples[index + 1].Time < renderTime)
                {
                    index++;
                }

                from = _samples[index];
                to = _samples[index + 1];
            }

            var t = from.Time >= to.Time
                ? 1f
                : MathHelper.Clamp((float)((renderTime - from.Time) / (to.Time - from.Time)), 0f, 1f);

            RenderPosition = Vector3.Lerp(from.Position, to.Position, t);

            var viewYaw = LerpAngle(from.Yaw, to.Yaw, t);
            var viewPitch = MathHelper.Lerp(from.Pitch, to.Pitch, t);

            var dx = to.Position.X - from.Position.X;
            var dz = to.Position.Z - from.Position.Z;
            var sampleDelta = Math.Max(to.Time - from.Time, 1e-4);
            var speed = MathF.Sqrt(dx * dx + dz * dz) / (float)sampleDelta;

            var targetBodyYaw = speed > 0.25f
                ? YawFromDirection(new Vector3(dx, 0f, dz))
                : viewYaw;
            var smoothing = 1f - MathF.Exp(-12f * deltaTime);

            BodyYaw = LerpAngle(BodyYaw, targetBodyYaw, smoothing);
            HeadYaw = viewYaw;

            var relativeYaw = MathHelper.WrapAngle(HeadYaw - BodyYaw);

            if (relativeYaw > MaxHeadTurn)
            {
                HeadYaw = BodyYaw + MaxHeadTurn;
            }
            else if (relativeYaw < -MaxHeadTurn)
            {
                HeadYaw = BodyYaw - MaxHeadTurn;
            }

            HeadPitch = MathHelper.Clamp(viewPitch, -1f, 1f);

            var intensity = MathHelper.Clamp(speed / 6f, 0f, 1f);
            _walkPhase += deltaTime * (2f + speed * 1.5f) * intensity;
            Swing = MathF.Sin(_walkPhase) * 0.7f * intensity;
            Bob = MathF.Abs(MathF.Sin(_walkPhase)) * 0.05f * intensity;
        }

        public void Dispose()
        {
            Torso.Dispose();
            Head.Dispose();
            LeftArm.Dispose();
            RightArm.Dispose();
            LeftLeg.Dispose();
            RightLeg.Dispose();
        }

        private static PrimitiveMesh<VertexPositionColor> CreatePart(
            GraphicsDevice device,
            Vector3 min,
            Vector3 size,
            Color color)
        {
            var vertices = new List<VertexPositionColor>();
            var indices = new List<int>();

            AddBox(vertices, indices, Vector3.Zero, min, size, color);
            return new PrimitiveMesh<VertexPositionColor>(device, vertices, indices);
        }

        private readonly struct Sample
        {
            public Sample(double time, Vector3 position, float yaw, float pitch)
            {
                Time = time;
                Position = position;
                Yaw = yaw;
                Pitch = pitch;
            }

            public double Time { get; }

            public Vector3 Position { get; }

            public float Yaw { get; }

            public float Pitch { get; }
        }
    }
}
