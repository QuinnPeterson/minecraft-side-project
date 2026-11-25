//------------------------------------------------------
// Adapted from LetsCreateNetworkGame.Server.Server
// Copyright - (c) - 2014 - Mille Boström
//------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using LetsCreateNetworkGame.OpenGL.Library;
using Lidgren.Network;
using LetsCreateNetworkGame.Server.Packets;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Entities;
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;

namespace LetsCreateNetworkGame.Server;

/// <summary>
/// Upstream-style Lidgren server with packet handlers, but no WinForms, rooms,
/// enemies, AI, or Direction. The console owns the process lifetime.
/// </summary>
internal sealed class NetworkServer
{
    private sealed class ServerEntity
    {
        public int Id;
        public Minecraft.Shared.Entities.EntityType Type;
        public float X, Y, Z;
        public float Vx, Vy, Vz;
        public float Yaw;
        public int Health = 10;
        public float HurtTimer;
        public int Data;
        public byte World;

        public ServerEntity(int id, Minecraft.Shared.Entities.EntityType type, float x, float y, float z, int data)
        {
            Id = id; Type = type; X = x; Y = y; Z = z; Data = data;
            Vx = 0; Vy = 0; Vz = 0;
            Yaw = 0;
        }
    }

    private const int SocketBufferSize = 4 * 1024 * 1024;

    private sealed class WorldState
    {
        public byte Id;
        public World World = null!;
        public int Seed;
        public int SpawnX, SpawnY, SpawnZ;
        public readonly object Sync = new();
        public BlockUpdateService Simulation = null!;
    }

    private readonly Dictionary<byte, WorldState> _worlds = new();

    private WorldState GetWorld(byte id) => _worlds[id];
    private readonly object _playersSync = new();
    private readonly List<PlayerConnection> _players = new();
    private readonly object _entitiesSync = new();
    private readonly List<ServerEntity> _entities = new();
    private int _nextEntityId = 1;
    private float _spawnTimer = 5f;
    private float _timeOfDay = 12f;
    private readonly ConcurrentDictionary<NetConnection, ConcurrentQueue<NetOutgoingMessage>> _pending = new();
    private readonly ConcurrentDictionary<NetConnection, Queue<ChunkRequest>> _chunkStreams = new();
    private readonly Action<string> _log;

    private NetServer _netServer = null!;

    private const string DefaultGroupId = "world";

    public NetworkServer(
        TerrainGenerationResult overworld,
        TerrainGenerationResult nether,
        Action<string> log)
    {
        _log = log;
        AddWorld((byte)Dimension.Overworld, overworld);
        AddWorld((byte)Dimension.Nether, nether);

        // Mob spawning is deferred to Run() after the NetServer is created.
    }

    private void AddWorld(byte id, TerrainGenerationResult generated)
    {
        var state = new WorldState
        {
            Id = id,
            World = generated.World,
            Seed = generated.Seed,
            SpawnX = generated.SpawnX,
            SpawnY = generated.SpawnY,
            SpawnZ = generated.SpawnZ,
        };

        var worldId = id;
        state.Simulation = new BlockUpdateService(
            state.World,
            state.Sync,
            change => BroadcastBlockChange(worldId, change),
            _log,
            (type, x, y, z, data, _) => SpawnEntity(type, x, y, z, data, worldId));

        _worlds[id] = state;
    }

    public NetServer NetServer => _netServer;

    /// <summary>Current in-game hour (0..24), authoritative on the server.</summary>
    public float TimeOfDay => _timeOfDay;

    /// <summary>Sets the time of day and tells every client to update its sky.</summary>
    public void SetTimeOfDay(float hours)
    {
        _timeOfDay = Math.Clamp(hours, 0f, 24f);
        _log($"Time set to {_timeOfDay:0.0}:00 ({(_timeOfDay >= 6f && _timeOfDay < 18f ? "day" : "night")}).");

        // The console loop starts before the network thread in some launches;
        // late joiners still receive the current time on connect.
        if (_netServer is null)
        {
            return;
        }

        Broadcast(WorldProtocol.CreateTimeUpdate(_netServer, DefaultGroupId, _timeOfDay));
    }

    public IReadOnlyList<PlayerConnection> GetPlayers()
    {
        lock (_playersSync)
        {
            return _players.ToList();
        }
    }

    public void Run(CancellationToken cancellationToken)
    {
        var config = new NetPeerConfiguration("networkGame")
        {
            Port = 14241,
            MaximumConnections = 16,
            ReceiveBufferSize = SocketBufferSize,
            SendBufferSize = SocketBufferSize,
        };
        config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

        _netServer = new NetServer(config);
        _netServer.Start();
        _log("Listening on UDP 14241 (Lidgren app 'networkGame').");

        // Spawn initial passive mobs now that the NetServer exists.
        var overworld = GetWorld((byte)Dimension.Overworld);
        var rng = new Random(overworld.Seed);
        for (var i = 0; i < 8; i++)
        {
            var mx = overworld.SpawnX + rng.Next(-20, 21);
            var mz = overworld.SpawnZ + rng.Next(-20, 21);
            var my = overworld.SpawnY + 1f;
            var type = rng.Next(2) == 0
                ? Minecraft.Shared.Entities.EntityType.Pig
                : Minecraft.Shared.Entities.EntityType.Sheep;
            SpawnEntity(type, mx + 0.5f, my, mz + 0.5f, 0);
        }

        var stopwatch = Stopwatch.StartNew();
        var nextTick = TickRate.Interval;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ProcessIncoming();
                StreamChunks();
                TickEntities();

                // Keep the authoritative clock moving so `time` and late
                // joiners reflect the current hour (ten real minutes per day).
                _timeOfDay += (float)TickRate.Interval.TotalSeconds / 600f;
                if (_timeOfDay >= 24f)
                {
                    _timeOfDay -= 24f;
                }

                nextTick += TickRate.Interval;
                var remaining = nextTick - stopwatch.Elapsed;

                if (remaining > TimeSpan.Zero)
                {
                    Thread.Sleep(remaining);
                }
                else
                {
                    nextTick = stopwatch.Elapsed + TickRate.Interval;
                }
            }
        }
        catch (NetException exception)
        {
            _log($"Network error: {exception.Message}");
        }
        finally
        {
            foreach (var state in _worlds.Values)
            {
                state.Simulation.Dispose();
            }

            if (_netServer.Status == NetPeerStatus.Running)
            {
                _netServer.Shutdown(new NetReason("Server stopped"), "Server stopped");
            }
        }
    }

    private void ProcessIncoming()
    {
        while (_netServer.ReadMessage(out var message))
        {
            try
            {
                switch (message.MessageType)
                {
                    case NetIncomingMessageType.ConnectionApproval:
                        HandleApproval(message);
                        break;

                    case NetIncomingMessageType.Data:
                        HandleData(message);
                        break;

                    case NetIncomingMessageType.StatusChanged:
                        HandleStatusChanged(message);
                        break;

                    case NetIncomingMessageType.WarningMessage:
                    case NetIncomingMessageType.ErrorMessage:
                        _log($"[network] {message.ReadString()}");
                        break;
                }
            }
            catch (NetException exception)
            {
                _log($"[network] Bad packet: {exception.Message}");
            }
            finally
            {
                _netServer.Recycle(message);
            }
        }
    }

    private void HandleApproval(NetIncomingMessage message)
    {
        var groupId = message.ReadString();

        if ((PacketType)message.ReadByte() != PacketType.Login)
        {
            message.SenderConnection.Deny("Expected a login packet.");
            return;
        }

        var username = message.ReadString();

        if (!IsValidUsername(username))
        {
            message.SenderConnection.Deny("Username must be 1-16 letters, numbers, '_' or '-'.");
            return;
        }

        lock (_playersSync)
        {
            if (_players.Any(p => string.Equals(p.Player.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                message.SenderConnection.Deny($"Username '{username}' is already in use.");
                return;
            }

            var overworld = GetWorld((byte)Dimension.Overworld);
            var random = new Random();
            var player = new Player(
                username,
                new Position(
                    overworld.SpawnX + 0.5f + random.Next(-6, 7),
                    overworld.SpawnY,
                    overworld.SpawnZ + 0.5f + random.Next(-6, 7)));

            _players.Add(new PlayerConnection(player, message.SenderConnection, groupId));

            message.SenderConnection.Approve();
            _log($"Player '{username}' logged in from {message.SenderConnection.RemoteEndPoint}.");

            QueueMessage(message.SenderConnection, CreatePlayerListMessage(groupId));
            QueueWorld(message.SenderConnection, groupId, (byte)Dimension.Overworld, player);
            Broadcast(CreatePlayerListMessage(groupId));
        }
    }

    /// <summary>
    /// Sends the world description, its entities and a spawn-first chunk stream
    /// to one connection. Called at login and whenever a player uses a portal.
    /// </summary>
    private void QueueWorld(NetConnection connection, string groupId, byte dimension, Player player, Position? joinPosition = null)
    {
        var state = GetWorld(dimension);
        var spawn = joinPosition ?? new Position(
            state.SpawnX + 0.5f,
            state.SpawnY,
            state.SpawnZ + 0.5f,
            player.Position.Yaw,
            player.Position.Pitch);

        player.World = dimension;
        player.Position = new Position(spawn.XPosition, spawn.YPosition, spawn.ZPosition, spawn.Yaw, spawn.Pitch);

        void SendOrQueue(NetOutgoingMessage message)
        {
            if (connection.Status == NetConnectionStatus.Connected)
            {
                _netServer.SendMessage(message, connection, NetDeliveryMethod.ReliableOrdered, 0);
            }
            else
            {
                QueueMessage(connection, message);
            }
        }

        SendOrQueue(CreateWorldInfoMessage(
            groupId,
            dimension,
            (int)MathF.Floor(spawn.XPosition),
            (int)MathF.Floor(spawn.YPosition),
            (int)MathF.Floor(spawn.ZPosition)));

        lock (_entitiesSync)
        {
            foreach (var e in _entities)
            {
                if (e.World != dimension)
                {
                    continue;
                }

                SendOrQueue(WorldProtocol.CreateEntitySpawn(_netServer, groupId, e.Id, e.Type, e.X, e.Y, e.Z, e.Data));
            }
        }

        var spawnChunkX = state.SpawnX / WorldProtocol.ChunkSize;
        var spawnChunkZ = state.SpawnZ / WorldProtocol.ChunkSize;
        var requests = new List<ChunkRequest>();

        for (var chunkX = 0; chunkX < state.World.SizeX / WorldProtocol.ChunkSize; chunkX++)
        {
            for (var chunkZ = 0; chunkZ < state.World.SizeZ / WorldProtocol.ChunkSize; chunkZ++)
            {
                requests.Add(new ChunkRequest(groupId, chunkX, chunkZ, dimension));
            }
        }

        // Send the chunks around the spawn first so players stand on ground
        // before the rest of the world finishes streaming in.
        requests.Sort((a, b) =>
        {
            var da = (a.ChunkX - spawnChunkX) * (a.ChunkX - spawnChunkX) +
                     (a.ChunkZ - spawnChunkZ) * (a.ChunkZ - spawnChunkZ);
            var db = (b.ChunkX - spawnChunkX) * (b.ChunkX - spawnChunkX) +
                     (b.ChunkZ - spawnChunkZ) * (b.ChunkZ - spawnChunkZ);
            return da.CompareTo(db);
        });

        _chunkStreams[connection] = new Queue<ChunkRequest>(requests);
    }

    private void StreamChunks()
    {
        const int chunksPerTick = 6;
        List<NetConnection>? finished = null;

        foreach (var pair in _chunkStreams)
        {
            if (pair.Key.Status != NetConnectionStatus.Connected)
            {
                continue;
            }

            var queue = pair.Value;

            for (var i = 0; i < chunksPerTick && queue.Count > 0; i++)
            {
                var request = queue.Dequeue();
                var message = CreateChunkSnapshotMessage(
                    request.GroupId, request.ChunkX, request.ChunkZ, request.World);

                _netServer.SendMessage(
                    message,
                    pair.Key,
                    NetDeliveryMethod.ReliableOrdered,
                    0);
            }

            if (queue.Count == 0)
            {
                finished ??= new List<NetConnection>();
                finished.Add(pair.Key);
            }
        }

        if (finished is null)
        {
            return;
        }

        foreach (var connection in finished)
        {
            _chunkStreams.TryRemove(connection, out _);
        }
    }

    private void HandleData(NetIncomingMessage message)
    {
        var packetType = (PacketType)message.ReadByte();
        var groupId = message.ReadString();

        if (packetType == PacketType.EntityPickup)
        {
            var entityId = message.ReadInt32();
            HandleEntityPickup(entityId);
            return;
        }

        if (packetType == PacketType.EntityHit)
        {
            var entityId = message.ReadInt32();
            HandleEntityHit(entityId);
            return;
        }

        PacketFactory.GetPacket(packetType).Run(this, message, groupId);
    }

    private void HandleEntityPickup(int entityId)
    {
        byte world = 0;
        var removed = false;

        lock (_entitiesSync)
        {
            var entity = _entities.FirstOrDefault(e => e.Id == entityId);
            if (entity is not null)
            {
                world = entity.World;
                removed = _entities.Remove(entity);
            }
        }

        if (removed)
        {
            BroadcastToWorld(world, WorldProtocol.CreateEntityRemove(_netServer, DefaultGroupId, entityId));
        }
    }

    private void HandleEntityHit(int entityId)
    {
        ServerEntity? entity;
        byte world = 0;

        lock (_entitiesSync)
        {
            entity = _entities.FirstOrDefault(e => e.Id == entityId);
            if (entity == null) return;
            entity.Health -= 4;
            entity.HurtTimer = 0.5f;
            world = entity.World;
        }

        BroadcastToWorld(world, WorldProtocol.CreateEntityHit(_netServer, DefaultGroupId, entityId));
    }

    private void HandleStatusChanged(NetIncomingMessage message)
    {
        var status = (NetConnectionStatus)message.ReadByte();
        var reason = message.ReadString();
        var connection = message.SenderConnection;

        if (status == NetConnectionStatus.Connected)
        {
            if (_pending.TryRemove(connection, out var queued))
            {
                while (queued.TryDequeue(out var packet))
                {
                    _netServer.SendMessage(packet, connection, NetDeliveryMethod.ReliableOrdered, 0);
                }
            }

            // Give late joiners the current time so their sky matches everyone else's.
            var timeMessage = WorldProtocol.CreateTimeUpdate(_netServer, DefaultGroupId, _timeOfDay);
            _netServer.SendMessage(timeMessage, connection, NetDeliveryMethod.ReliableOrdered, 0);
        }

        if (status != NetConnectionStatus.Disconnected)
        {
            return;
        }

        _pending.TryRemove(connection, out _);
        _chunkStreams.TryRemove(connection, out _);

        string username;
        string groupId;
        lock (_playersSync)
        {
            var player = _players.FirstOrDefault(p => p.Connection == connection);
            username = player?.Player.Username ?? string.Empty;
            groupId = player?.GroupId ?? string.Empty;

            if (player is not null)
            {
                _players.Remove(player);
            }
        }

        if (!string.IsNullOrEmpty(username))
        {
            _log($"Player '{username}' disconnected ({reason}).");
            Broadcast(CreatePlayerListMessage(groupId));
        }
    }

    public void QueueMessage(NetConnection connection, NetOutgoingMessage message)
    {
        var queue = _pending.GetOrAdd(connection, _ => new ConcurrentQueue<NetOutgoingMessage>());
        queue.Enqueue(message);
    }

    public void Broadcast(
        NetOutgoingMessage message,
        NetDeliveryMethod deliveryMethod = NetDeliveryMethod.ReliableOrdered)
    {
        var connections = GetPlayers().Select(p => p.Connection).ToList();

        if (connections.Count > 0)
        {
            _netServer.SendMessage(message, connections, deliveryMethod, 0);
        }
    }

    /// <summary>Sends a world-specific packet only to players in that world.</summary>
    public void BroadcastToWorld(
        byte world,
        NetOutgoingMessage message,
        NetDeliveryMethod deliveryMethod = NetDeliveryMethod.ReliableOrdered)
    {
        var connections = GetPlayers()
            .Where(p => p.World == world)
            .Select(p => p.Connection)
            .ToList();

        if (connections.Count > 0)
        {
            _netServer.SendMessage(message, connections, deliveryMethod, 0);
        }
    }

    public PlayerConnection? FindPlayer(string username)
    {
        lock (_playersSync)
        {
            return _players.FirstOrDefault(p =>
                string.Equals(p.Player.Username, username, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void UpdatePlayerPosition(PlayerConnection player, Position position)
    {
        player.Player.Position = position;
    }

    public NetOutgoingMessage CreatePlayerListMessage(string groupId)
    {
        var players = GetPlayers().Select(p => p.Player).ToList();
        var message = _netServer.CreateMessage();

        message.Write((byte)PacketType.AllPlayers);
        message.Write(groupId);
        PlayerProtocol.WritePlayerList(message, false, players);
        return message;
    }

    public NetOutgoingMessage CreatePlayerPositionMessage(string groupId, Player player)
    {
        var message = _netServer.CreateMessage();

        message.Write((byte)PacketType.PlayerPosition);
        message.Write(groupId);
        PlayerProtocol.WritePlayer(message, player);
        return message;
    }

    public NetOutgoingMessage CreateWorldInfoMessage(string groupId, byte dimension = 0, int? spawnX = null, int? spawnY = null, int? spawnZ = null)
    {
        var state = GetWorld(dimension);

        return WorldProtocol.CreateWorldInfo(
            _netServer,
            groupId,
            new WorldInfo(
                state.World.SizeX,
                state.World.SizeY,
                state.World.SizeZ,
                state.Seed,
                spawnX ?? state.SpawnX,
                spawnY ?? state.SpawnY,
                spawnZ ?? state.SpawnZ,
                dimension));
    }

    public NetOutgoingMessage CreateChunkSnapshotMessage(
        string groupId,
        int chunkX,
        int chunkZ,
        byte dimension = 0)
    {
        var state = GetWorld(dimension);

        lock (state.Sync)
        {
            return WorldProtocol.CreateChunkSnapshot(
                _netServer,
                groupId,
                state.World,
                chunkX,
                chunkZ,
                dimension);
        }
    }

    public NetOutgoingMessage CreateBlockUpdateMessage(string groupId, BlockChange change)
    {
        return WorldProtocol.CreateBlockUpdate(
            _netServer,
            groupId,
            change.X,
            change.Y,
            change.Z,
            change.Block,
            change.World);
    }

    public bool SetBlock(string groupId, BlockChange change, NetConnection? connection = null)
    {
        byte dimension = 0;

        if (connection is not null)
        {
            lock (_playersSync)
            {
                var player = _players.FirstOrDefault(p => p.Connection == connection);
                if (player is not null)
                {
                    dimension = player.World;
                }
            }
        }

        change = change with { World = dimension };
        var state = GetWorld(dimension);

        if (!state.World.InBounds(change.X, change.Y, change.Z))
        {
            return false;
        }

        if (!BlockRegistry.TryGet(change.Block, out _))
        {
            return false;
        }

        // Spawn a dropped item when a block is broken (set to air).
        if (change.Block == BlockId.Air)
        {
            BlockId oldBlock;

            lock (state.Sync)
            {
                oldBlock = state.World.GetBlock(change.X, change.Y, change.Z);
            }

            if (oldBlock != BlockId.Air && !BlockRegistry.IsWater(oldBlock) && !BlockRegistry.IsLava(oldBlock))
            {
                SpawnEntity(
                    Minecraft.Shared.Entities.EntityType.Item,
                    change.X + 0.5f,
                    change.Y + 0.5f,
                    change.Z + 0.5f,
                    (int)oldBlock,
                    dimension);
            }
        }

        return state.Simulation.Enqueue(change);
    }

    private void SpawnEntity(
        Minecraft.Shared.Entities.EntityType type,
        float x,
        float y,
        float z,
        int data,
        byte world = 0)
    {
        int id;
        lock (_entitiesSync)
        {
            id = _nextEntityId++;
            _entities.Add(new ServerEntity(id, type, x, y, z, data) { World = world });
        }

        BroadcastToWorld(
            world,
            WorldProtocol.CreateEntitySpawn(_netServer, DefaultGroupId, id, type, x, y, z, data));
    }

    private void TickEntities()
    {
        const float dt = 1f / 20f;
        const float gravity = 28f;

        _spawnTimer -= dt;
        if (_spawnTimer <= 0)
        {
            _spawnTimer = 5f;
            TrySpawnMobs();
        }

        TickPortals(dt);

        List<ServerEntity> snapshot;
        lock (_entitiesSync) { snapshot = _entities.ToList(); }

        var toRemove = new List<int>();

        foreach (var e in snapshot)
        {
            if (e.HurtTimer > 0) e.HurtTimer -= dt;

            // The previous look-before-move / wander / swim AI made mobs jitter,
            // so entities now just fall and settle (items keep their drop drift).
            if (e.Type == Minecraft.Shared.Entities.EntityType.Item)
            {
                e.Vx *= 0.9f;
                e.Vz *= 0.9f;
            }

            var entityGravity = e.Type == Minecraft.Shared.Entities.EntityType.FallingBlock ? 4f : gravity;
            var terminalVy = e.Type == Minecraft.Shared.Entities.EntityType.FallingBlock ? -4f : -20f;
            e.Vy -= entityGravity * dt;
            if (e.Vy < terminalVy) e.Vy = terminalVy;

            e.X += e.Vx * dt;
            e.Y += e.Vy * dt;
            e.Z += e.Vz * dt;

            ResolvePlayerCollisions(e);

            var entityWorld = GetWorld(e.World);
            int gbx = (int)MathF.Floor(e.X);
            int gby = (int)MathF.Floor(e.Y - 0.5f);
            int gbz = (int)MathF.Floor(e.Z);

            if (gby >= 0 && gby < entityWorld.World.SizeY && gbx >= 0 && gbx < entityWorld.World.SizeX && gbz >= 0 && gbz < entityWorld.World.SizeZ)
            {
                BlockId ground;
                lock (entityWorld.Sync) { ground = entityWorld.World.GetBlock(gbx, gby, gbz); }

                if (ground != BlockId.Air && !BlockRegistry.IsWater(ground) && !BlockRegistry.IsLava(ground))
                {
                    e.Y = gby + 1.5f;
                    e.Vy = 0;

                    if (e.Type == Minecraft.Shared.Entities.EntityType.FallingBlock)
                    {
                        var placeY = gby + 1;
                        var placed = false;

                        lock (entityWorld.Sync)
                        {
                            if (entityWorld.World.InBounds(gbx, placeY, gbz))
                            {
                                var target = entityWorld.World.GetBlock(gbx, placeY, gbz);
                                if (target == BlockId.Air || BlockRegistry.IsReplaceable(target))
                                {
                                    placed = entityWorld.World.SetBlock(gbx, placeY, gbz, (BlockId)e.Data);
                                }
                            }
                        }

                        if (placed)
                        {
                            BroadcastToWorld(e.World, CreateBlockUpdateMessage(DefaultGroupId, new BlockChange(gbx, placeY, gbz, (BlockId)e.Data, e.World)));
                        }
                        else
                        {
                            SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, e.Data, e.World);
                        }

                        toRemove.Add(e.Id);
                        BroadcastToWorld(e.World, WorldProtocol.CreateEntityRemove(_netServer, DefaultGroupId, e.Id));
                        continue;
                    }
                }
            }

            if (e.Type == Minecraft.Shared.Entities.EntityType.FallingBlock && e.Y < -20)
            {
                toRemove.Add(e.Id);
                BroadcastToWorld(e.World, WorldProtocol.CreateEntityRemove(_netServer, DefaultGroupId, e.Id));
                continue;
            }

            if (e.Health <= 0)
            {
                if (e.Type == Minecraft.Shared.Entities.EntityType.Pig)
                {
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -10, e.World);
                }
                else if (e.Type == Minecraft.Shared.Entities.EntityType.Sheep)
                {
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -11, e.World);
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -11, e.World);
                }
                else if (e.Type == Minecraft.Shared.Entities.EntityType.Cow)
                {
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -12, e.World);
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -13, e.World);
                }
                else if (e.Type == Minecraft.Shared.Entities.EntityType.Ghast)
                {
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -14, e.World);
                }
                else if (e.Type == Minecraft.Shared.Entities.EntityType.Pigman)
                {
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -15, e.World);
                }

                toRemove.Add(e.Id);
                BroadcastToWorld(e.World, WorldProtocol.CreateEntityRemove(_netServer, DefaultGroupId, e.Id));
                continue;
            }

            BroadcastToWorld(
                e.World,
                WorldProtocol.CreateEntityUpdate(_netServer, DefaultGroupId, e.Id, e.X, e.Y, e.Z, e.Yaw));

            if (e.Type == Minecraft.Shared.Entities.EntityType.Item)
            {
                bool picked = false;
                lock (_playersSync)
                {
                    foreach (var p in _players)
                    {
                        if (p.World != e.World)
                        {
                            continue;
                        }

                        var pos = p.Player.Position;
                        var dx = pos.XPosition - e.X;
                        var dy = pos.YPosition - e.Y;
                        var dz = pos.ZPosition - e.Z;
                        if (dx * dx + dy * dy + dz * dz < 4f)
                        {
                            picked = true;
                            break;
                        }
                    }
                }

                if (picked)
                {
                    lock (_entitiesSync) { _entities.RemoveAll(x => x.Id == e.Id); }
                    BroadcastToWorld(e.World, WorldProtocol.CreateEntityRemove(_netServer, DefaultGroupId, e.Id));
                }
            }
        }

        // Keep mobs from stacking on top of one another.
        lock (_entitiesSync)
        {
            for (var i = 0; i < snapshot.Count; i++)
            {
                for (var j = i + 1; j < snapshot.Count; j++)
                {
                    var a = snapshot[i];
                    var b = snapshot[j];

                    if (a.Type == Minecraft.Shared.Entities.EntityType.Item ||
                        b.Type == Minecraft.Shared.Entities.EntityType.Item ||
                        a.Type == Minecraft.Shared.Entities.EntityType.FallingBlock ||
                        b.Type == Minecraft.Shared.Entities.EntityType.FallingBlock ||
                        a.World != b.World)
                    {
                        continue;
                    }

                    var dx = a.X - b.X;
                    var dz = a.Z - b.Z;
                    var distanceSquared = dx * dx + dz * dz;

                    if (distanceSquared >= 0.64f || distanceSquared < 0.000001f)
                    {
                        continue;
                    }

                    var distance = MathF.Sqrt(distanceSquared);
                    var push = (0.8f - distance) * 0.5f;
                    var nx = dx / distance;
                    var nz = dz / distance;

                    a.X += nx * push;
                    a.Z += nz * push;
                    b.X -= nx * push;
                    b.Z -= nz * push;
                }
            }
        }

        if (toRemove.Count > 0)
        {
            lock (_entitiesSync)
            {
                _entities.RemoveAll(x => toRemove.Contains(x.Id));
            }
        }
    }

    /// <summary>
    /// Pushes entities out of player collision boxes. Player movement is
    /// client-authoritative; Game1 resolves entities on the client as well.
    /// </summary>
    private void ResolvePlayerCollisions(ServerEntity entity)
    {
        if (entity.Type == Minecraft.Shared.Entities.EntityType.Item ||
            entity.Type == Minecraft.Shared.Entities.EntityType.FallingBlock)
        {
            return;
        }

        const float entityHalf = 0.4f;
        const float playerHalf = 0.3f;
        const float entityHeight = 1f;
        const float playerHeight = 1.8f;

        lock (_playersSync)
        {
            foreach (var player in _players)
            {
                if (player.World != entity.World)
                {
                    continue;
                }

                var pos = player.Player.Position;

                if (entity.Y + entityHeight <= pos.YPosition ||
                    entity.Y >= pos.YPosition + playerHeight)
                {
                    continue;
                }

                var dx = entity.X - pos.XPosition;
                var dz = entity.Z - pos.ZPosition;
                var overlapX = entityHalf + playerHalf - MathF.Abs(dx);
                var overlapZ = entityHalf + playerHalf - MathF.Abs(dz);

                if (overlapX <= 0f || overlapZ <= 0f)
                {
                    continue;
                }

                if (overlapX < overlapZ)
                {
                    entity.X += (dx >= 0f ? 1f : -1f) * overlapX;
                    entity.Vx = 0f;
                }
                else
                {
                    entity.Z += (dz >= 0f ? 1f : -1f) * overlapZ;
                    entity.Vz = 0f;
                }
            }
        }
    }

    private void TrySpawnMobs()
    {
        TrySpawnOverworldMobs();
        TrySpawnNetherMobs();
    }

    private void TrySpawnOverworldMobs()
    {
        const byte worldId = (byte)Dimension.Overworld;
        var state = GetWorld(worldId);
        int mobCount;
        lock (_entitiesSync)
        {
            mobCount = _entities.Count(e => e.World == worldId &&
                (e.Type == Minecraft.Shared.Entities.EntityType.Pig ||
                 e.Type == Minecraft.Shared.Entities.EntityType.Sheep ||
                 e.Type == Minecraft.Shared.Entities.EntityType.Cow));
        }

        if (mobCount >= 20) return;

        List<PlayerConnection> players;
        lock (_playersSync) { players = _players.Where(p => p.World == worldId).ToList(); }
        if (players.Count == 0) return;

        var rng = new Random();

        foreach (var p in players)
        {
            var pos = p.Player.Position;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var x = (int)(pos.XPosition + rng.Next(-24, 25));
                var z = (int)(pos.ZPosition + rng.Next(-24, 25));

                if (x < 1 || z < 1 || x >= state.World.SizeX - 1 || z >= state.World.SizeZ - 1) continue;

                var y = (int)pos.YPosition;
                if (y < 1 || y >= state.World.SizeY - 1) y = state.SpawnY;

                for (var yy = y; yy > 1; yy--)
                {
                    BlockId below;
                    lock (state.Sync) { below = state.World.GetBlock(x, yy - 1, z); }
                    if (below == BlockId.Grass || below == BlockId.Dirt || below == BlockId.Sand)
                    {
                        y = yy;
                        break;
                    }

                    if (below == BlockId.Air) continue;
                    break;
                }

                BlockId ground, space;
                lock (state.Sync)
                {
                    ground = state.World.GetBlock(x, y - 1, z);
                    space = state.World.GetBlock(x, y, z);
                }

                if (ground != BlockId.Grass && ground != BlockId.Dirt) continue;
                if (space != BlockId.Air) continue;

                var roll = rng.Next(3);
                var type = roll == 0
                    ? Minecraft.Shared.Entities.EntityType.Pig
                    : roll == 1
                        ? Minecraft.Shared.Entities.EntityType.Sheep
                        : Minecraft.Shared.Entities.EntityType.Cow;

                SpawnEntity(type, x + 0.5f, y + 0.5f, z + 0.5f, 0, worldId);
                mobCount++;
                if (mobCount >= 20) return;
                break;
            }
        }
    }

    private void TickPortals(float dt)
    {
        List<PlayerConnection> players;
        lock (_playersSync) { players = _players.ToList(); }

        foreach (var player in players)
        {
            player.PortalCooldown = Math.Max(0, player.PortalCooldown - dt);
            if (player.PortalCooldown > 0)
            {
                continue;
            }

            var state = GetWorld(player.World);
            var pos = player.Player.Position;
            var bx = (int)MathF.Floor(pos.XPosition);
            var by = (int)MathF.Floor(pos.YPosition);
            var bz = (int)MathF.Floor(pos.ZPosition);

            bool inPortal;
            lock (state.Sync)
            {
                inPortal = state.World.InBounds(bx, by, bz) &&
                           state.World.GetBlock(bx, by, bz) == BlockId.NetherPortal;
            }

            if (!inPortal)
            {
                continue;
            }

            var destination = player.World == (byte)Dimension.Overworld ? (byte)Dimension.Nether : (byte)Dimension.Overworld;

            Position? joinPosition = null;
            if (destination == (byte)Dimension.Nether)
            {
                player.OverworldReturn = new Position(
                    player.Player.Position.XPosition,
                    player.Player.Position.YPosition,
                    player.Player.Position.ZPosition,
                    player.Player.Position.Yaw,
                    player.Player.Position.Pitch);

                var nether = GetWorld((byte)Dimension.Nether);
                joinPosition = FindPortalExit(nether, nether.SpawnX, nether.SpawnY, nether.SpawnZ, player.Player.Position.Yaw, player.Player.Position.Pitch);
            }
            else if (player.OverworldReturn is not null)
            {
                var overworld = GetWorld((byte)Dimension.Overworld);
                var rx = (int)MathF.Floor(player.OverworldReturn.XPosition);
                var ry = (int)MathF.Floor(player.OverworldReturn.YPosition);
                var rz = (int)MathF.Floor(player.OverworldReturn.ZPosition);
                joinPosition = FindPortalExit(overworld, rx, ry, rz, player.OverworldReturn.Yaw, player.OverworldReturn.Pitch);
            }

            player.World = destination;
            player.Player.World = destination;
            player.PortalCooldown = 4f;
            QueueWorld(player.Connection, player.GroupId, destination, player.Player, joinPosition);
            Broadcast(CreatePlayerListMessage(DefaultGroupId));

            var destName = destination == (byte)Dimension.Nether ? "the Nether" : "the Overworld";
            _log($"{player.Player.Username} entered {destName}.");
        }
    }

    private static Position FindPortalExit(WorldState state, int x, int y, int z, float yaw, float pitch)
    {
        // Prefer stepping out to the side of the portal so the player doesn't
        // immediately re-enter it after teleport cooldown expires.
        Span<(int dx, int dz)> offsets = stackalloc (int, int)[]
        {
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (-1, 1), (1, -1), (-1, -1),
            (2, 0), (-2, 0), (0, 2), (0, -2),
        };

        lock (state.Sync)
        {
            foreach (var (dx, dz) in offsets)
            {
                var px = x + dx;
                var pz = z + dz;

                for (var py = y + 2; py >= y - 2; py--)
                {
                    if (py <= 0 || py + 1 >= state.World.SizeY) continue;

                    var feet = state.World.GetBlock(px, py, pz);
                    var head = state.World.GetBlock(px, py + 1, pz);
                    var below = state.World.GetBlock(px, py - 1, pz);

                    if (feet == BlockId.NetherPortal || head == BlockId.NetherPortal) continue;
                    if (feet != BlockId.Air && !BlockRegistry.IsReplaceable(feet)) continue;
                    if (head != BlockId.Air && !BlockRegistry.IsReplaceable(head)) continue;
                    if (below == BlockId.Air || BlockRegistry.IsReplaceable(below) || below == BlockId.NetherPortal) continue;

                    return new Position(px + 0.5f, py, pz + 0.5f, yaw, pitch);
                }
            }
        }

        return new Position(x + 0.5f, y, z + 0.5f, yaw, pitch);
    }

    private void TrySpawnNetherMobs()
    {
        const byte worldId = (byte)Dimension.Nether;
        var state = GetWorld(worldId);
        int mobCount;
        lock (_entitiesSync)
        {
            mobCount = _entities.Count(e => e.World == worldId &&
                (e.Type == Minecraft.Shared.Entities.EntityType.Ghast ||
                 e.Type == Minecraft.Shared.Entities.EntityType.Pigman));
        }

        if (mobCount >= 10) return;

        List<PlayerConnection> players;
        lock (_playersSync) { players = _players.Where(p => p.World == worldId).ToList(); }
        if (players.Count == 0) return;

        var rng = new Random();

        foreach (var p in players)
        {
            var pos = p.Player.Position;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var x = (int)(pos.XPosition + rng.Next(-24, 25));
                var z = (int)(pos.ZPosition + rng.Next(-24, 25));
                var y = Math.Clamp((int)(pos.YPosition + rng.Next(-8, 9)), 5, state.World.SizeY - 6);

                if (x < 1 || z < 1 || x >= state.World.SizeX - 1 || z >= state.World.SizeZ - 1) continue;

                var type = rng.Next(3) == 0
                    ? Minecraft.Shared.Entities.EntityType.Ghast
                    : Minecraft.Shared.Entities.EntityType.Pigman;

                bool canSpawn;
                lock (state.Sync)
                {
                    if (type == Minecraft.Shared.Entities.EntityType.Ghast)
                    {
                        canSpawn = state.World.GetBlock(x, y, z) == BlockId.Air &&
                                   state.World.GetBlock(x, y + 1, z) == BlockId.Air &&
                                   state.World.GetBlock(x, y + 2, z) == BlockId.Air &&
                                   state.World.GetBlock(x, y + 3, z) == BlockId.Air;
                    }
                    else
                    {
                        var ground = state.World.GetBlock(x, y - 1, z);
                        canSpawn = (ground == BlockId.Netherrack || ground == BlockId.SoulSand) &&
                                   state.World.GetBlock(x, y, z) == BlockId.Air &&
                                   state.World.GetBlock(x, y + 1, z) == BlockId.Air;
                    }
                }

                if (!canSpawn) continue;

                SpawnEntity(type, x + 0.5f, y + 0.5f, z + 0.5f, 0, worldId);
                mobCount++;
                if (mobCount >= 10) return;
                break;
            }
        }
    }

    private void BroadcastBlockChange(byte world, BlockChange change)
    {
        if (_netServer is null || _netServer.Status != NetPeerStatus.Running)
        {
            return;
        }

        BroadcastToWorld(world, CreateBlockUpdateMessage(DefaultGroupId, change with { World = world }));
    }

    public BlockId GetBlock(int x, int y, int z, byte world = 0)
    {
        var state = GetWorld(world);
        lock (state.Sync)
        {
            return state.World.GetBlock(x, y, z);
        }
    }

    public IReadOnlyDictionary<BlockId, int> CountBlocks(byte world = 0)
    {
        var state = GetWorld(world);
        lock (state.Sync)
        {
            return state.World.CountBlocks();
        }
    }

    public void Kick(string username)
    {
        var player = FindPlayer(username);

        if (player is null)
        {
            return;
        }

        var message = _netServer.CreateMessage();
        message.Write((byte)PacketType.Kick);
        message.Write(player.GroupId);
        message.Write(player.Player.Username);
        Broadcast(message);
        player.Connection.Disconnect("You were kicked from the server.");
    }

    private readonly record struct ChunkRequest(string GroupId, int ChunkX, int ChunkZ, byte World = 0);

    private static bool IsValidUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 16)
        {
            return false;
        }

        return username.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
    }
}
