//------------------------------------------------------
// Adapted from LetsCreateNetworkGame.Server.Server
// Copyright - (c) - 2014 - Mille BostrÃ¶m
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
        public float HeadYaw;
        public int Health = 10;
        public float HurtTimer;
        public int Data;
        public float WanderTimer;
        public float LookTimer;
        public Random Rng = new();

        public ServerEntity(int id, Minecraft.Shared.Entities.EntityType type, float x, float y, float z, int data)
        {
            Id = id; Type = type; X = x; Y = y; Z = z; Data = data;
            WanderTimer = (float)(new Random().NextDouble() * 2 + 1);
            LookTimer = (float)(new Random().NextDouble() * 2 + 1);
            Yaw = (float)(new Random().NextDouble() * Math.PI * 2);
            HeadYaw = Yaw;
        }
    }

    private const int SocketBufferSize = 4 * 1024 * 1024;

    private readonly World _world;
    private readonly int _seed;
    private readonly int _spawnX;
    private readonly int _spawnY;
    private readonly int _spawnZ;
    private readonly object _worldSync = new();
    private readonly object _playersSync = new();
    private readonly List<PlayerConnection> _players = new();
    private readonly object _entitiesSync = new();
    private readonly List<ServerEntity> _entities = new();
    private int _nextEntityId = 1;
    private float _spawnTimer = 5f;
    private readonly ConcurrentDictionary<NetConnection, ConcurrentQueue<NetOutgoingMessage>> _pending = new();
    private readonly ConcurrentDictionary<NetConnection, Queue<ChunkRequest>> _chunkStreams = new();
    private readonly Action<string> _log;
    private readonly BlockUpdateService _simulation;

    private NetServer _netServer = null!;

    private const string DefaultGroupId = "world";

    public NetworkServer(
        World world,
        int seed,
        int spawnX,
        int spawnY,
        int spawnZ,
        Action<string> log)
    {
        _world = world;
        _seed = seed;
        _spawnX = spawnX;
        _spawnY = spawnY;
        _spawnZ = spawnZ;
        _log = log;
        _simulation = new BlockUpdateService(
            _world,
            _worldSync,
            BroadcastBlockChange,
            _log);

        // Mob spawning is deferred to Run() after the NetServer is created.
    }

    public NetServer NetServer => _netServer;

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
        var rng = new Random(_seed);
        for (var i = 0; i < 8; i++)
        {
            var mx = _spawnX + rng.Next(-20, 21);
            var mz = _spawnZ + rng.Next(-20, 21);
            var my = _spawnY + 1f;
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
            _simulation.Dispose();

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

            var random = new Random();
            var player = new Player(
                username,
                new Position(
                    _spawnX + 0.5f + random.Next(-6, 7),
                    _spawnY,
                    _spawnZ + 0.5f + random.Next(-6, 7)));

            _players.Add(new PlayerConnection(player, message.SenderConnection, groupId));
        }

        message.SenderConnection.Approve();
        _log($"Player '{username}' logged in from {message.SenderConnection.RemoteEndPoint}.");

        QueueMessage(message.SenderConnection, CreatePlayerListMessage(groupId));
        QueueMessage(message.SenderConnection, CreateWorldInfoMessage(groupId));

        // Send existing entities.
        lock (_entitiesSync)
        {
            foreach (var e in _entities)
            {
                QueueMessage(
                    message.SenderConnection,
                    WorldProtocol.CreateEntitySpawn(_netServer, groupId, e.Id, e.Type, e.X, e.Y, e.Z, e.Data));
            }
        }

        // Chunk snapshots are large, so they stream a few per tick instead of
        // being flushed in one burst when the connection finishes approving.
        var spawnChunkX = _spawnX / WorldProtocol.ChunkSize;
        var spawnChunkZ = _spawnZ / WorldProtocol.ChunkSize;
        var requests = new List<ChunkRequest>();

        for (var chunkX = 0; chunkX < _world.SizeX / WorldProtocol.ChunkSize; chunkX++)
        {
            for (var chunkZ = 0; chunkZ < _world.SizeZ / WorldProtocol.ChunkSize; chunkZ++)
            {
                requests.Add(new ChunkRequest(groupId, chunkX, chunkZ));
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

        _chunkStreams[message.SenderConnection] = new Queue<ChunkRequest>(requests);
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
                    request.GroupId, request.ChunkX, request.ChunkZ);

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
        lock (_entitiesSync)
        {
            _entities.RemoveAll(e => e.Id == entityId);
        }

        Broadcast(WorldProtocol.CreateEntityRemove(_netServer, DefaultGroupId, entityId));
    }
    private void HandleEntityHit(int entityId)
    {
        ServerEntity? entity;
        lock (_entitiesSync)
        {
            entity = _entities.FirstOrDefault(e => e.Id == entityId);
            if (entity == null) return;
            entity.Health -= 4;
            entity.HurtTimer = 0.5f;
        }

        Broadcast(WorldProtocol.CreateEntityHit(_netServer, DefaultGroupId, entityId));
    }

    private void HandleStatusChanged(NetIncomingMessage message)
    {
        var status = (NetConnectionStatus)message.ReadByte();
        var reason = message.ReadString();
        var connection = message.SenderConnection;

        if (status == NetConnectionStatus.Connected && _pending.TryRemove(connection, out var queued))
        {
            while (queued.TryDequeue(out var packet))
            {
                _netServer.SendMessage(packet, connection, NetDeliveryMethod.ReliableOrdered, 0);
            }
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

    public NetOutgoingMessage CreateWorldInfoMessage(string groupId)
    {
        return WorldProtocol.CreateWorldInfo(
            _netServer,
            groupId,
            new WorldInfo(
                _world.SizeX,
                _world.SizeY,
                _world.SizeZ,
                _seed,
                _spawnX,
                _spawnY,
                _spawnZ));
    }

    public NetOutgoingMessage CreateChunkSnapshotMessage(string groupId, int chunkX, int chunkZ)
    {
        lock (_worldSync)
        {
            return WorldProtocol.CreateChunkSnapshot(
                _netServer,
                groupId,
                _world,
                chunkX,
                chunkZ);
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
            change.Block);
    }

    public bool SetBlock(string groupId, BlockChange change)
    {
        if (!_world.InBounds(change.X, change.Y, change.Z))
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

            lock (_worldSync)
            {
                oldBlock = _world.GetBlock(change.X, change.Y, change.Z);
            }

            if (oldBlock != BlockId.Air && !BlockRegistry.IsWater(oldBlock) && !BlockRegistry.IsLava(oldBlock))
            {
                SpawnEntity(
                    Minecraft.Shared.Entities.EntityType.Item,
                    change.X + 0.5f,
                    change.Y + 0.5f,
                    change.Z + 0.5f,
                    (int)oldBlock);
            }
        }

        return _simulation.Enqueue(change);
    }

    private void SpawnEntity(Minecraft.Shared.Entities.EntityType type, float x, float y, float z, int data)
    {
        int id;
        lock (_entitiesSync)
        {
            id = _nextEntityId++;
            _entities.Add(new ServerEntity(id, type, x, y, z, data));
        }

        Broadcast(WorldProtocol.CreateEntitySpawn(_netServer, DefaultGroupId, id, type, x, y, z, data));
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

        List<ServerEntity> snapshot;
        lock (_entitiesSync) { snapshot = _entities.ToList(); }

        var toRemove = new List<int>();

        foreach (var e in snapshot)
        {
            if (e.HurtTimer > 0) e.HurtTimer -= dt;

            int bx = (int)MathF.Floor(e.X);
            int by = (int)MathF.Floor(e.Y);
            int bz = (int)MathF.Floor(e.Z);
            bool inWater = false;
            if (bx >= 0 && bx < _world.SizeX && by >= 0 && by < _world.SizeY && bz >= 0 && bz < _world.SizeZ)
            {
                BlockId block;
                lock (_worldSync) { block = _world.GetBlock(bx, by, bz); }
                inWater = BlockRegistry.IsWater(block);
            }

            e.LookTimer -= dt;
            if (e.LookTimer <= 0)
            {
                e.LookTimer = 1f + (float)(e.Rng.NextDouble() * 2);
                e.HeadYaw = e.Yaw + (float)(e.Rng.NextDouble() * 1.2 - 0.6);
            }
            e.HeadYaw += (e.Yaw - e.HeadYaw) * 0.05f;

            if (e.Type == Minecraft.Shared.Entities.EntityType.Pig || e.Type == Minecraft.Shared.Entities.EntityType.Sheep || e.Type == Minecraft.Shared.Entities.EntityType.Cow)
            {
                e.WanderTimer -= dt;
                if (e.WanderTimer <= 0)
                {
                    e.WanderTimer = 2f + (float)(e.Rng.NextDouble() * 3);
                    if (e.Rng.NextDouble() < 0.5)
                    {
                        var speed = inWater ? 0.8f : 1.2f;
                        var angle = (float)(e.Rng.NextDouble() * Math.PI * 2);
                        e.Vx = MathF.Cos(angle) * speed;
                        e.Vz = MathF.Sin(angle) * speed;
                        e.Yaw = angle;
                    }
                    else
                    {
                        e.Vx = 0;
                        e.Vz = 0;
                    }
                }
            }
            else
            {
                e.Vx *= 0.9f;
                e.Vz *= 0.9f;
            }

            if (inWater)
            {
                e.Vy += 0.5f * dt;
                if (e.Vy > 1f) e.Vy = 1f;
                e.Vy *= 0.9f;
            }
            else
            {
                e.Vy -= gravity * dt;
                if (e.Vy < -20f) e.Vy = -20f;
            }

            e.X += e.Vx * dt;
            e.Y += e.Vy * dt;
            e.Z += e.Vz * dt;

            int gbx = (int)MathF.Floor(e.X);
            int gby = (int)MathF.Floor(e.Y - 0.5f);
            int gbz = (int)MathF.Floor(e.Z);

            if (gby >= 0 && gby < _world.SizeY && gbx >= 0 && gbx < _world.SizeX && gbz >= 0 && gbz < _world.SizeZ)
            {
                BlockId ground;
                lock (_worldSync) { ground = _world.GetBlock(gbx, gby, gbz); }

                if (ground != BlockId.Air && !BlockRegistry.IsWater(ground) && !BlockRegistry.IsLava(ground))
                {
                    e.Y = gby + 1.5f;
                    e.Vy = 0;
                }
            }

            if (e.Health <= 0)
            {
                if (e.Type == Minecraft.Shared.Entities.EntityType.Pig)
                {
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -10);
                }
                else if (e.Type == Minecraft.Shared.Entities.EntityType.Sheep)
                {
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -11);
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -11);
                }
                else if (e.Type == Minecraft.Shared.Entities.EntityType.Cow)
                {
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -12);
                    SpawnEntity(Minecraft.Shared.Entities.EntityType.Item, e.X, e.Y, e.Z, -13);
                }

                toRemove.Add(e.Id);
                Broadcast(WorldProtocol.CreateEntityRemove(_netServer, DefaultGroupId, e.Id));
                continue;
            }

            Broadcast(WorldProtocol.CreateEntityUpdate(_netServer, DefaultGroupId, e.Id, e.X, e.Y, e.Z, e.HeadYaw));

            if (e.Type == Minecraft.Shared.Entities.EntityType.Item)
            {
                bool picked = false;
                lock (_playersSync)
                {
                    foreach (var p in _players)
                    {
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
                    Broadcast(WorldProtocol.CreateEntityRemove(_netServer, DefaultGroupId, e.Id));
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

    private void TrySpawnMobs()
    {
        int mobCount;
        lock (_entitiesSync) { mobCount = _entities.Count(e => e.Type == Minecraft.Shared.Entities.EntityType.Pig || e.Type == Minecraft.Shared.Entities.EntityType.Sheep); }
        if (mobCount >= 20) return;

        List<PlayerConnection> players;
        lock (_playersSync) { players = _players.ToList(); }
        if (players.Count == 0) return;

        var rng = new Random();

        foreach (var p in players)
        {
            var pos = p.Player.Position;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var dx = rng.Next(-24, 25);
                var dz = rng.Next(-24, 25);
                var x = (int)(pos.XPosition + dx);
                var z = (int)(pos.ZPosition + dz);

                if (x < 1 || z < 1 || x >= _world.SizeX - 1 || z >= _world.SizeZ - 1) continue;

                int y = (int)pos.YPosition;
                if (y < 1 || y >= _world.SizeY - 1) y = _spawnY;

                for (var yy = y; yy > 1; yy--)
                {
                    BlockId below;
                    lock (_worldSync) { below = _world.GetBlock(x, yy - 1, z); }
                    if (below == BlockId.Grass || below == BlockId.Dirt || below == BlockId.Sand)
                    {
                        y = yy;
                        break;
                    }
                    if (below == BlockId.Air) continue;
                    break;
                }

                BlockId ground;
                lock (_worldSync) { ground = _world.GetBlock(x, y - 1, z); }
                if (ground != BlockId.Grass && ground != BlockId.Dirt) continue;

                BlockId space;
                lock (_worldSync) { space = _world.GetBlock(x, y, z); }
                if (space != BlockId.Air) continue;

                var type = rng.Next(2) == 0
                    ? Minecraft.Shared.Entities.EntityType.Pig
                    : Minecraft.Shared.Entities.EntityType.Sheep;

                SpawnEntity(type, x + 0.5f, y + 0.5f, z + 0.5f, 0);
                mobCount++;
                if (mobCount >= 20) return;
                break;
            }
        }
    }

    private void BroadcastBlockChange(BlockChange change)
    {
        if (_netServer is null || _netServer.Status != NetPeerStatus.Running)
        {
            return;
        }

        Broadcast(CreateBlockUpdateMessage(DefaultGroupId, change));
    }

    public BlockId GetBlock(int x, int y, int z)
    {
        lock (_worldSync)
        {
            return _world.GetBlock(x, y, z);
        }
    }

    public IReadOnlyDictionary<BlockId, int> CountBlocks()
    {
        lock (_worldSync)
        {
            return _world.CountBlocks();
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

    private readonly record struct ChunkRequest(string GroupId, int ChunkX, int ChunkZ);

    private static bool IsValidUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 16)
        {
            return false;
        }

        return username.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
    }
}
