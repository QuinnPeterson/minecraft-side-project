using System;

using System.Collections.Generic;

using System.Linq;

using Microsoft.Xna.Framework;

using Microsoft.Xna.Framework.Graphics;

using Microsoft.Xna.Framework.Input;

using LetsCreateNetworkGame.OpenGL.Manager;

using Minecraft.Shared.Blocks;

using Minecraft.Shared.Net;

using Minecraft.Shared.Worlds;

using NetworkPlayer = LetsCreateNetworkGame.OpenGL.Library.Player;

using NetworkPosition = LetsCreateNetworkGame.OpenGL.Library.Position;



namespace Minecraft.Client;



public class Game1 : Game

{

    private const int WorldSize = 32;

    private const float MouseSensitivity = 0.0025f;

    private const float MoveSpeed = 14f;

    private const string Host = "127.0.0.1";

    private const int Port = 14241;

    private const string GroupId = "world";



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



   // Unit-cube corners and the 12 edges used for the block selection outline.

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



    private const float Reach = 6f;



   // Remote players are rendered ~100 ms in the past so short network hiccups

   // can be smoothed instead of showing up as teleports.

    private const float InterpolationDelay = 0.1f;



    private readonly GraphicsDeviceManager _graphics;

    private readonly ManagerNetwork _network;

    private readonly Dictionary<string, RemotePlayerVisual> _visuals =

        new(StringComparer.OrdinalIgnoreCase);



    private World _world;

    private bool _worldDirty;

    private double _networkTime;

    private int _fps;
    private int _frameCount;
    private double _fpsTimer;



    private BasicEffect _effect = null!;

    private PartMesh _opaqueMesh = null!;

    private PartMesh _transparentMesh = null!;



   // Spawn just above the ground near the middle of the world so blocks are

   // immediately within reach (the old spawn sat outside the world bounds).

    private Vector3 _position = new(WorldSize / 2f, 10f, WorldSize / 2f + 4f);

    private float _yaw;

    private float _pitch = -0.4f;

    private bool _mouseCentered;

    private bool _kicked;

    private bool _paused;

    private MouseState _previousMouse;

    private KeyboardState _previousKeyboard;



    private bool _hasTarget;

    private int _targetX;

    private int _targetY;

    private int _targetZ;

    private int _targetFaceX;

    private int _targetFaceY;

    private int _targetFaceZ;



    private int _selectedBlockIndex;



    private SpriteBatch _spriteBatch = null!;

    private SpriteFont _font = null!;

    private Texture2D _pixel = null!;



   // Remote-player model dimensions, in blocks. The feet sit at the network

   // position; the name tag hangs just above the top of the head.

    private const float HeadPivotY = 1.45f;

    private const float ShoulderY = 1.40f;

    private const float ShoulderX = 0.375f;

    private const float HipY = 0.75f;

    private const float HipX = 0.175f;

    private const float PlayerHeadTop = HeadPivotY + 0.5f;

    private const float NameTagHeight = PlayerHeadTop + 0.15f;



    public Game1()

    {

        _graphics = new GraphicsDeviceManager(this);

        Content.RootDirectory = "Content";

        IsMouseVisible = false;



        _world = FlatWorldGenerator.Generate(WorldSize, WorldSize);

        _network = new ManagerNetwork(Host, Port, GroupId, CreateUsername());

    }



    protected override void Initialize()
    
    {
        _graphics.SynchronizeWithVerticalRetrace = false;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 200);

        _graphics.PreferredBackBufferWidth = 1280;

        _graphics.PreferredBackBufferHeight = 720;

        _graphics.ApplyChanges();



       // Name tags and the crosshair use the live viewport every frame, so

       // they follow the camera when the player resizes the window.

        Window.AllowUserResizing = true;



        Window.Title = "Minecraft Client - connecting";

        base.Initialize();



        _network.Start();

    }



    protected override void LoadContent()

    {

        _spriteBatch = new SpriteBatch(GraphicsDevice);

        _font = Content.Load<SpriteFont>("UiFont");

        _pixel = new Texture2D(GraphicsDevice, 1, 1);

        _pixel.SetData(new[] { Color.White });



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

        var deltaTime = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.1f);

        _networkTime = gameTime.TotalGameTime.TotalSeconds;

        var keyboard = Keyboard.GetState();



        if (!_kicked &&

            keyboard.IsKeyDown(Keys.Escape) &&

            !_previousKeyboard.IsKeyDown(Keys.Escape))

        {

            SetPaused(!_paused);

        }



        var quitPressed = (_paused || _kicked) &&

                          keyboard.IsKeyDown(Keys.Q) &&

                          !_previousKeyboard.IsKeyDown(Keys.Q);

        _previousKeyboard = keyboard;



        if (quitPressed)

        {

            Exit();

            return;

        }



        if (!_paused)

        {

            UpdateLook();

            UpdateMovement(keyboard, gameTime);

            UpdateTarget();

            UpdateBlockBreaking();

            UpdateBlockSelection(keyboard);



            _network.SetLocalPosition(

                new NetworkPosition(_position.X, _position.Y, _position.Z, _yaw, _pitch));

        }



       // Keep draining network messages while paused so the world and other

       // players stay in sync; only local simulation is frozen.

        ApplyServerMessages();

        UpdateRemotePlayers(deltaTime);



        base.Update(gameTime);

    }



    private void SetPaused(bool paused)

    {

        _paused = paused;

        IsMouseVisible = paused;

        _mouseCentered = false;



        if (!paused)

        {

            var center = new Point(

                GraphicsDevice.Viewport.Width / 2,

                GraphicsDevice.Viewport.Height / 2);

            Mouse.SetPosition(center.X, center.Y);

        }



       // Ignore button presses used to focus the window or open the pause menu.

        _previousMouse = Mouse.GetState();

    }



    protected override void Dispose(bool disposing)

    {

        if (disposing)

        {

            _network.Dispose();

        }



        base.Dispose(disposing);

    }



    protected override void Draw(GameTime gameTime)
    
    {
        _frameCount++;
        _fpsTimer += gameTime.ElapsedGameTime.TotalSeconds;

        if (_fpsTimer >= 1.0)
        {
            _fps = _frameCount;
            _frameCount = 0;
            _fpsTimer -= 1.0;
        }

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



        var forward = rotation.Forward;



        _effect.View = view;

        _effect.Projection = projection;

        _effect.World = Matrix.Identity;



        GraphicsDevice.BlendState = BlendState.Opaque;

        GraphicsDevice.DepthStencilState = DepthStencilState.Default;

        GraphicsDevice.RasterizerState = RasterizerState.CullNone;



        DrawMesh(_opaqueMesh, Matrix.Identity);

        DrawRemotePlayers();



        GraphicsDevice.BlendState = BlendState.AlphaBlend;

        GraphicsDevice.DepthStencilState = new DepthStencilState

        {

            DepthBufferEnable = true,

            DepthBufferWriteEnable = false,

        };

        DrawMesh(_transparentMesh, Matrix.Identity);



        GraphicsDevice.BlendState = BlendState.Opaque;

        GraphicsDevice.DepthStencilState = DepthStencilState.Default;

        _effect.World = Matrix.Identity;

        DrawSelectionOutline();



        DrawOverlay(view, projection, forward);



        base.Draw(gameTime);

    }



    protected override void UnloadContent()

    {

        ClearRemotePlayers();



        _opaqueMesh?.Dispose();

        _transparentMesh?.Dispose();

        _effect?.Dispose();

        _pixel?.Dispose();

        _spriteBatch?.Dispose();

        base.UnloadContent();

    }



    private void DrawMesh(

        VertexBuffer vertices,

        IndexBuffer indices,

        int indexCount,

        Matrix world)

    {

        if (indexCount == 0 || vertices is null || indices is null)

        {

            return;

        }



        _effect.World = world;



        foreach (var pass in _effect.CurrentTechnique.Passes)

        {

            pass.Apply();



            GraphicsDevice.SetVertexBuffer(vertices);

            GraphicsDevice.Indices = indices;

            GraphicsDevice.DrawIndexedPrimitives(

                PrimitiveType.TriangleList,

                0,

                0,

                indexCount / 3);

        }

    }



    private void DrawMesh(PartMesh part, Matrix world) =>

        DrawMesh(part.Vertices, part.Indices, part.IndexCount, world);



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



    private void UpdateMovement(KeyboardState keyboard, GameTime gameTime)

    {

        if (_kicked)

        {

            return;

        }



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

        while (_network.Snapshots.TryDequeue(out var world))

        {

            _world = world;

            _worldDirty = true;

        }



        while (_network.Changes.TryDequeue(out var change))

        {

            if (_world.SetBlock(change.X, change.Y, change.Z, change.Block))

            {

                _worldDirty = true;

            }

        }



        while (_network.PlayerLists.TryDequeue(out var players))

        {

            SyncRemotePlayers(players);

        }



        while (_network.PlayerUpdates.TryDequeue(out var player))

        {

            ApplyRemotePlayerUpdate(player);

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



        while (_network.Statuses.TryDequeue(out var status))

        {

            if (!_kicked)

            {

                Window.Title = $"Minecraft Client - {status}";

            }

        }



        if (_worldDirty)

        {

            RebuildWorldMesh();

            _worldDirty = false;

        }

    }



    private void RebuildWorldMesh()

    {

        _opaqueMesh?.Dispose();

        _transparentMesh?.Dispose();

        _opaqueMesh = null!;

        _transparentMesh = null!;



        var opaqueVertices = new List<VertexPositionColor>();

        var opaqueIndices = new List<int>();

        var transparentVertices = new List<VertexPositionColor>();

        var transparentIndices = new List<int>();



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



                    var definition = BlockRegistry.Get(block);

                    var color = ToColor(definition.Color, definition.Alpha);

                    var origin = new Vector3(x, y, z);



                    if (definition.Shape == BlockShape.Cross)

                    {

                       // Wheat grows taller with each stage.

                        var plantHeight = 0.35f + 0.1f * ((int)block - (int)BlockId.Wheat1);

                        AddCross(transparentVertices, transparentIndices, origin, color, plantHeight);

                        continue;

                    }



                    if (definition.Shape == BlockShape.Water)
                    {
                        var height = WaterHeight(block);

                        // Heights are calculated independently at all four corners.
                        var h00 = GetWaterCornerHeight(x, y, z, height);
                        var h10 = GetWaterCornerHeight(x + 1, y, z, height);
                        var h11 = GetWaterCornerHeight(x + 1, y, z + 1, height);
                        var h01 = GetWaterCornerHeight(x, y, z + 1, height);

                        if (IsFaceVisible(block, GetBlockSafe(x + 1, y, z)))
                            AddWaterSide(transparentVertices, transparentIndices, origin, 0, h10, h11, color);

                        if (IsFaceVisible(block, GetBlockSafe(x - 1, y, z)))
                            AddWaterSide(transparentVertices, transparentIndices, origin, 1, h00, h01, color);

                        if (IsFaceVisible(block, GetBlockSafe(x, y, z + 1)))
                            AddWaterSide(transparentVertices, transparentIndices, origin, 2, h01, h11, color);

                        if (IsFaceVisible(block, GetBlockSafe(x, y, z - 1)))
                            AddWaterSide(transparentVertices, transparentIndices, origin, 3, h00, h10, color);

                        if (IsFaceVisible(block, GetBlockSafe(x, y - 1, z)))
                            AddFace(transparentVertices, transparentIndices, origin,
                                BottomCorners, Shade(color, 0.45f));

                        if (IsFaceVisible(block, GetBlockSafe(x, y + 1, z)))
                            AddWaterTop(transparentVertices, transparentIndices, origin,
                                h00, h10, h11, h01, color);

                        continue;
                    }

                    var targetList = definition.Alpha < 1f ? transparentVertices : opaqueVertices;

                    var targetIndices = definition.Alpha < 1f ? transparentIndices : opaqueIndices;



                   // Top

                    if (IsFaceVisible(block, GetBlockSafe(x, y + 1, z)))

                    {

                        AddFace(targetList, targetIndices, origin, TopCorners, Shade(color, 1f));

                    }



                   // Bottom

                    if (IsFaceVisible(block, GetBlockSafe(x, y - 1, z)))

                    {

                        AddFace(targetList, targetIndices, origin, BottomCorners, Shade(color, 0.45f));

                    }



                   // North (-X)

                    if (IsFaceVisible(block, GetBlockSafe(x - 1, y, z)))

                    {

                        AddFace(targetList, targetIndices, origin, NorthCorners, Shade(color, 0.7f));

                    }



                   // South (+X)

                    if (IsFaceVisible(block, GetBlockSafe(x + 1, y, z)))

                    {

                        AddFace(targetList, targetIndices, origin, SouthCorners, Shade(color, 0.8f));

                    }



                   // West (-Z)

                    if (IsFaceVisible(block, GetBlockSafe(x, y, z - 1)))

                    {

                        AddFace(targetList, targetIndices, origin, WestCorners, Shade(color, 0.6f));

                    }



                   // East (+Z)

                    if (IsFaceVisible(block, GetBlockSafe(x, y, z + 1)))

                    {

                        AddFace(targetList, targetIndices, origin, EastCorners, Shade(color, 0.65f));

                    }

                }

            }

        }



        _opaqueMesh = new PartMesh(GraphicsDevice, opaqueVertices, opaqueIndices);

        _transparentMesh = new PartMesh(GraphicsDevice, transparentVertices, transparentIndices);

    }



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



            DrawMesh(visual.Torso, baseWorld);



            var headWorld =

                Matrix.CreateRotationX(visual.HeadPitch) *

                Matrix.CreateRotationY(visual.HeadYaw - visual.BodyYaw) *

                Matrix.CreateTranslation(0f, HeadPivotY, 0f) *

                baseWorld;

            DrawMesh(visual.Head, headWorld);



            var leftArm =

                Matrix.CreateRotationX(visual.Swing) *

                Matrix.CreateTranslation(-ShoulderX, ShoulderY, 0f) *

                baseWorld;

            var rightArm =

                Matrix.CreateRotationX(-visual.Swing) *

                Matrix.CreateTranslation(ShoulderX, ShoulderY, 0f) *

                baseWorld;

            DrawMesh(visual.LeftArm, leftArm);

            DrawMesh(visual.RightArm, rightArm);



            var leftLeg =

                Matrix.CreateRotationX(-visual.Swing) *

                Matrix.CreateTranslation(-HipX, HipY, 0f) *

                baseWorld;

            var rightLeg =

                Matrix.CreateRotationX(visual.Swing) *

                Matrix.CreateTranslation(HipX, HipY, 0f) *

                baseWorld;

            DrawMesh(visual.LeftLeg, leftLeg);

            DrawMesh(visual.RightLeg, rightLeg);

        }

    }



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



        public PartMesh Torso { get; }

        public PartMesh Head { get; }

        public PartMesh LeftArm { get; }

        public PartMesh RightArm { get; }

        public PartMesh LeftLeg { get; }

        public PartMesh RightLeg { get; }



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



           // The body turns toward travel; the head keeps looking where the

           // remote player is aiming, clamped so the neck does not spin.

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



        private static PartMesh CreatePart(

            GraphicsDevice device,

            Vector3 min,

            Vector3 size,

            Color color)

        {

            var vertices = new List<VertexPositionColor>();

            var indices = new List<int>();

            Game1.AddBox(vertices, indices, Vector3.Zero, min, size, color);

            return new PartMesh(device, vertices, indices);

        }



        private static float YawFromDirection(Vector3 direction) =>

            MathF.Atan2(-direction.X, -direction.Z);



        private static float LerpAngle(float from, float to, float t) =>

            from + MathHelper.WrapAngle(to - from) * t;



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



    private sealed class PartMesh : IDisposable

    {

        public PartMesh(

            GraphicsDevice device,

            IReadOnlyList<VertexPositionColor> vertices,

            IReadOnlyList<int> indices)

        {

            IndexCount = indices.Count;



            if (IndexCount == 0 || vertices.Count == 0)

            {

                return;

            }



            Vertices = new VertexBuffer(

                device,

                typeof(VertexPositionColor),

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

            if (Vertices is not null)

            {

                Vertices.Dispose();

            }



            if (Indices is not null)

            {

                Indices.Dispose();

            }

        }

    }



    private bool IsAir(int x, int y, int z) => _world.GetBlock(x, y, z) == BlockId.Air;



    private BlockId GetBlockSafe(int x, int y, int z)

    {

        if (!_world.InBounds(x, y, z))

        {

            return BlockId.Air;

        }



        return _world.GetBlock(x, y, z);

    }



    private static bool IsFaceVisible(BlockId current, BlockId neighbor)

    {

        if (neighbor == BlockId.Air)

        {

            return true;

        }



        var neighborDefinition = BlockRegistry.Get(neighbor);



        if (!neighborDefinition.IsTransparent)

        {

            return false;

        }



       // Water never renders faces against other water, even at different levels.

        if (BlockRegistry.IsWater(current) && BlockRegistry.IsWater(neighbor))

        {

            return false;

        }



        return neighbor != current;

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



    private float GetWaterCornerHeight(int cornerX, int y, int cornerZ, float fallback)
    {
        float totalHeight = 0f;
        float totalWeight = 0f;

        // Each corner is shared by up to four water blocks.
        for (int dx = -1; dx <= 0; dx++)
        {
            for (int dz = -1; dz <= 0; dz++)
            {
                int bx = cornerX + dx;
                int bz = cornerZ + dz;
                var water = GetBlockSafe(bx, y, bz);

                if (!BlockRegistry.IsWater(water))
                    continue;

                // A water block directly above fills this corner to the block top.
                if (BlockRegistry.IsWater(GetBlockSafe(bx, y + 1, bz)))
                    return 1f;

                float sampleHeight = WaterHeight(water);

                // Source blocks dominate nearby flowing-water levels.
                float weight = BlockRegistry.WaterLevel(water) <= 0 ? 10f : 1f;
                totalHeight += sampleHeight * weight;
                totalWeight += weight;
            }
        }

        return totalWeight <= 0f ? fallback : totalHeight / totalWeight;
    }

    private static void AddWaterSide(
        List<VertexPositionColor> vertices,
        List<int> indices,
        Vector3 origin,
        int face,
        float heightA,
        float heightB,
        Color color)
    {
        // face: 0=+X, 1=-X, 2=+Z, 3=-Z
        Vector3[] corners;

        if (face == 0)
        {
            corners = new[]
            {
                new Vector3(1, 0, 0),
                new Vector3(1, heightA, 0),
                new Vector3(1, heightB, 1),
                new Vector3(1, 0, 1),
            };
        }
        else if (face == 1)
        {
            corners = new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(0, 0, 1),
                new Vector3(0, heightB, 1),
                new Vector3(0, heightA, 0),
            };
        }
        else if (face == 2)
        {
            corners = new[]
            {
                new Vector3(0, 0, 1),
                new Vector3(0, heightA, 1),
                new Vector3(1, heightB, 1),
                new Vector3(1, 0, 1),
            };
        }
        else
        {
            corners = new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(1, heightB, 0),
                new Vector3(0, heightA, 0),
            };
        }

        AddFace(vertices, indices, origin, corners, color);
    }

    private static void AddWaterTop(
        List<VertexPositionColor> vertices,
        List<int> indices,
        Vector3 origin,
        float h00,
        float h10,
        float h11,
        float h01,
        Color color)
    {
        int start = vertices.Count;

        vertices.Add(new VertexPositionColor(origin + new Vector3(0f, h00, 0f), color));
        vertices.Add(new VertexPositionColor(origin + new Vector3(1f, h10, 0f), color));
        vertices.Add(new VertexPositionColor(origin + new Vector3(1f, h11, 1f), color));
        vertices.Add(new VertexPositionColor(origin + new Vector3(0f, h01, 1f), color));

        // Two triangles make one continuous, potentially sloped water surface.
        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }

    private static void AddCross(

        List<VertexPositionColor> vertices,

        List<int> indices,

        Vector3 origin,

        Color color,

        float height)

    {

       // Two diagonal quads forming an X.

        AddQuad(vertices, indices, origin,

            new Vector3(0.15f, 0f, 0.15f),

            new Vector3(0.85f, height, 0.85f),

            new Vector3(0.15f, height, 0.15f),

            new Vector3(0.85f, 0f, 0.85f),

            color);



        AddQuad(vertices, indices, origin,

            new Vector3(0.85f, 0f, 0.15f),

            new Vector3(0.15f, height, 0.85f),

            new Vector3(0.85f, height, 0.15f),

            new Vector3(0.15f, 0f, 0.85f),

            color);

    }



    private static void AddQuad(

        List<VertexPositionColor> vertices,

        List<int> indices,

        Vector3 origin,

        Vector3 a,

        Vector3 b,

        Vector3 c,

        Vector3 d,

        Color color)

    {

        var start = vertices.Count;



        vertices.Add(new VertexPositionColor(origin + a, color));

        vertices.Add(new VertexPositionColor(origin + b, color));

        vertices.Add(new VertexPositionColor(origin + c, color));

        vertices.Add(new VertexPositionColor(origin + d, color));



        indices.Add(start);

        indices.Add(start + 1);

        indices.Add(start + 2);

        indices.Add(start);

        indices.Add(start + 1);

        indices.Add(start + 3);

    }



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



    private void UpdateBlockBreaking()

    {

        var mouse = Mouse.GetState();

        var previous = _previousMouse;

        _previousMouse = mouse;



        var leftPressed = mouse.LeftButton == ButtonState.Pressed &&

                          previous.LeftButton != ButtonState.Pressed;

        var rightPressed = mouse.RightButton == ButtonState.Pressed &&

                           previous.RightButton != ButtonState.Pressed;



        if (!IsActive || _kicked)

        {

            return;

        }



        if (mouse.ScrollWheelValue != previous.ScrollWheelValue &&

            BlockRegistry.Placeable.Count > 0)

        {

            var step = mouse.ScrollWheelValue > previous.ScrollWheelValue ? 1 : -1;

            var count = BlockRegistry.Placeable.Count;

            _selectedBlockIndex = (_selectedBlockIndex + step + count) % count;

        }



        if (!_hasTarget)

        {

            return;

        }



        if (leftPressed)

        {

            _network.SendBlockChange(_targetX, _targetY, _targetZ, BlockId.Air);

            return;

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

        var existingBlock = _world.GetBlock(placeX, placeY, placeZ);

        if (existingBlock != BlockId.Air &&

            !BlockRegistry.IsWater(existingBlock) &&

            !BlockRegistry.IsPlant(existingBlock))

        {

            return;

        }



       // Never seal the player inside a block.

        var playerX = (int)MathF.Floor(_position.X);

        var playerY = (int)MathF.Floor(_position.Y);

        var playerZ = (int)MathF.Floor(_position.Z);



        if (placeX == playerX && placeZ == playerZ &&

            (placeY == playerY || placeY == playerY + 1))

        {

            return;

        }



        _network.SendBlockChange(

            placeX, placeY, placeZ,

            BlockRegistry.Placeable[_selectedBlockIndex].Id);

    }



    private void UpdateTarget()

    {

        var direction = Matrix.CreateFromYawPitchRoll(_yaw, _pitch, 0).Forward;



        _hasTarget = BlockRaycaster.TryHitWithFace(

            _world,

            _position.X, _position.Y, _position.Z,

            direction.X, direction.Y, direction.Z,

            Reach,

            out _targetX, out _targetY, out _targetZ,

            out _targetFaceX, out _targetFaceY, out _targetFaceZ);

    }



    private void UpdateBlockSelection(KeyboardState keyboard)

    {

        var count = Math.Min(BlockRegistry.Placeable.Count, 9);



        for (var i = 0; i < count; i++)

        {

            if (keyboard.IsKeyDown((Keys)((int)Keys.D1 + i)))

            {

                _selectedBlockIndex = i;

                return;

            }

        }

    }



    private void DrawSelectionOutline()

    {

        if (!_hasTarget)

        {

            return;

        }



        var edgeCount = BoxEdges.GetLength(0);

        var vertices = new VertexPositionColor[edgeCount * 2];

        var min = new Vector3(_targetX - 0.01f, _targetY - 0.01f, _targetZ - 0.01f);

        var size = new Vector3(1.02f, 1.02f, 1.02f);



        for (var i = 0; i < edgeCount; i++)

        {

            var a = min + UnitCorners[BoxEdges[i, 0]] * size;

            var b = min + UnitCorners[BoxEdges[i, 1]] * size;

            vertices[i * 2] = new VertexPositionColor(a, Color.Black);

            vertices[i * 2 + 1] = new VertexPositionColor(b, Color.Black);

        }



        foreach (var pass in _effect.CurrentTechnique.Passes)

        {

            pass.Apply();

            GraphicsDevice.DrawUserPrimitives(

                PrimitiveType.LineList,

                vertices,

                0,

                edgeCount);

        }

    }



    private void DrawOverlay(Matrix view, Matrix projection, Vector3 forward)

    {

        var viewport = GraphicsDevice.Viewport;

        _spriteBatch.Begin(

            SpriteSortMode.Deferred,

            BlendState.AlphaBlend,

            SamplerState.PointClamp,

            DepthStencilState.None,

            RasterizerState.CullNone);



        foreach (var visual in _visuals.Values)

        {

            var anchor = visual.RenderPosition +

                         new Vector3(0f, NameTagHeight + visual.Bob, 0f);



            if (Vector3.Dot(anchor - _position, forward) <= 0f)

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



        var centerX = viewport.Width / 2;

        var centerY = viewport.Height / 2;



        if (!_paused)

        {

            _spriteBatch.Draw(_pixel, new Rectangle(centerX - 9, centerY, 7, 2), Color.White);

            _spriteBatch.Draw(_pixel, new Rectangle(centerX + 3, centerY, 7, 2), Color.White);

            _spriteBatch.Draw(_pixel, new Rectangle(centerX, centerY - 9, 2, 7), Color.White);

            _spriteBatch.Draw(_pixel, new Rectangle(centerX, centerY + 3, 2, 7), Color.White);

        }



        var selected = BlockRegistry.Placeable[_selectedBlockIndex];

        DrawLabel($"Selected: {selected.Name}  (1-9 / mouse wheel)", new Vector2(12f, 12f));

        DrawLabel($"You: {_network.Username}", new Vector2(12f, 38f));

        DrawLabel($"FPS: {_fps}", new Vector2(12f, 64f));



        if (_paused)

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



        _spriteBatch.End();

    }



    private void DrawLabel(string text, Vector2 position)

    {

        var rounded = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));

        _spriteBatch.DrawString(_font, text, rounded + Vector2.One, Color.Black);

        _spriteBatch.DrawString(_font, text, rounded, Color.White);

    }



    private static Color ToColor(uint rgb) =>

        ToColor(rgb, 1f);



    private static Color ToColor(uint rgb, float alpha) =>

        new(

            (int)((rgb >> 16) & 0xFF),

            (int)((rgb >> 8) & 0xFF),

            (int)(rgb & 0xFF),

            (int)(MathHelper.Clamp(alpha, 0f, 1f) * 255f));



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

}
