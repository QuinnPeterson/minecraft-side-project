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
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;

namespace LetsCreateNetworkGame.Server;

/// <summary>
/// Upstream-style Lidgren server with packet handlers, but no WinForms, rooms,
/// enemies, AI, or Direction. The console owns the process lifetime.
/// </summary>
internal sealed class NetworkServer
{
    private const int SocketBufferSize = 4 * 1024 * 1024;

    private readonly World _world;
    private readonly object _worldSync = new();
    private readonly object _playersSync = new();
    private readonly List<PlayerConnection> _players = new();
    private readonly ConcurrentDictionary<NetConnection, ConcurrentQueue<NetOutgoingMessage>> _pending = new();
    private readonly Action<string> _log;
    private readonly BlockUpdateService _simulation;

    private NetServer _netServer = null!;

    private const string DefaultGroupId = "world";

    public NetworkServer(World world, Action<string> log)
    {
        _world = world;
        _log = log;
        _simulation = new BlockUpdateService(
            _world,
            _worldSync,
            BroadcastBlockChange,
            _log);
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

        var stopwatch = Stopwatch.StartNew();
        var nextTick = TickRate.Interval;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ProcessIncoming();

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
                    random.Next(0, _world.SizeX),
                    8f,
                    random.Next(0, _world.SizeZ)));

            _players.Add(new PlayerConnection(player, message.SenderConnection, groupId));
        }

        message.SenderConnection.Approve();
        _log($"Player '{username}' logged in from {message.SenderConnection.RemoteEndPoint}.");

        QueueMessage(message.SenderConnection, CreatePlayerListMessage(groupId));
        QueueMessage(message.SenderConnection, CreateWorldSnapshotMessage(groupId));
    }

    private void HandleData(NetIncomingMessage message)
    {
        var packetType = (PacketType)message.ReadByte();
        var groupId = message.ReadString();

        PacketFactory.GetPacket(packetType).Run(this, message, groupId);
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

    public NetOutgoingMessage CreateWorldSnapshotMessage(string groupId)
    {
        lock (_worldSync)
        {
            return WorldProtocol.CreateWorldSnapshot(_netServer, groupId, _world);
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

        return _simulation.Enqueue(change);
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

    private static bool IsValidUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 16)
        {
            return false;
        }

        return username.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
    }
}
