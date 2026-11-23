//------------------------------------------------------
// Adapted from LetsCreateNetworkGame.OpenGL.Manager.ManagerNetwork
// Copyright - (c) - 2014 - Mille Boström
//------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LetsCreateNetworkGame.OpenGL.Library;
using Lidgren.Network;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Entities;
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;

namespace LetsCreateNetworkGame.OpenGL.Manager;

/// <summary>
/// Upstream-style Lidgren client. It owns the connection on a dedicated fixed
/// 60 Hz thread; the game loop only drains queues and updates local state.
/// </summary>
internal sealed class ManagerNetwork : IDisposable
{
    private const int SocketBufferSize = 4 * 1024 * 1024;
    private const int ReconnectDelayTicks = TickRate.TicksPerSecond * 2;

    private readonly string _host;
    private readonly int _port;
    private readonly object _localPositionLock = new();
    private readonly ConcurrentQueue<BlockChange> _outgoingBlocks = new();

    private NetClient _client = null!;
    private Thread _thread = null!;
    private volatile bool _running;
    private bool _connecting;
    private bool _active;
    private long _tickCount;
    private long _nextConnectTick;
    private volatile int _worldHeight = World.DefaultHeight;
    private Position _localPosition = new();

    public ManagerNetwork(string host, int port, string groupId, string username)
    {
        _host = host;
        _port = port;
        GroupId = groupId;
        Username = username;
    }

    public string GroupId { get; }

    public string Username { get; }

    public bool Active => _active;

    public ConcurrentQueue<WorldInfo> WorldInfos { get; } = new();

    public ConcurrentQueue<ChunkSnapshot> Chunks { get; } = new();

    public ConcurrentQueue<BlockChange> Changes { get; } = new();

    public ConcurrentQueue<IReadOnlyList<Player>> PlayerLists { get; } = new();

    public ConcurrentQueue<Player> PlayerUpdates { get; } = new();

    public ConcurrentQueue<string> Statuses { get; } = new();

    public ConcurrentQueue<string> Kicks { get; } = new();
    public ConcurrentQueue<EntitySnapshot> EntitySpawns { get; } = new();
    public ConcurrentQueue<(int Id, float X, float Y, float Z, float Yaw)> EntityUpdates { get; } = new();
    public ConcurrentQueue<int> EntityRemoves { get; } = new();
    public ConcurrentQueue<int> EntityHits { get; } = new();

    public void Start()
    {
        if (_running)
        {
            return;
        }

        CreateClient();
        _running = true;
        _thread = new Thread(NetworkLoop)
        {
            IsBackground = true,
            Name = "Lidgren client",
        };
        _thread.Start();
    }

    /// <summary>Updates the position sent to the server on the next network tick.</summary>
    public void SetLocalPosition(Position position)
    {
        lock (_localPositionLock)
        {
            _localPosition = new Position(
                position.XPosition,
                position.YPosition,
                position.ZPosition,
                position.Yaw,
                position.Pitch);
        }
    }

    public void SendBlockChange(int x, int y, int z, BlockId block)
    {
        _outgoingBlocks.Enqueue(new BlockChange(x, y, z, block));
    }

    public void SendEntityPickup(int entityId)
    {
        if (!_active) return;
        var message = _client.CreateMessage();
        message.Write((byte)PacketType.EntityPickup);
        message.Write(GroupId);
        message.Write(entityId);
        _client.SendMessage(message, NetDeliveryMethod.ReliableOrdered);
    }

    public void SendEntityHit(int entityId)
    {
        if (!_active) return;
        var message = _client.CreateMessage();
        message.Write((byte)PacketType.EntityHit);
        message.Write(GroupId);
        message.Write(entityId);
        _client.SendMessage(message, NetDeliveryMethod.ReliableOrdered);
    }

    public void Dispose()
    {
        _running = false;

        if (_thread is not null && _thread.IsAlive)
        {
            _thread.Join(1000);
        }

        ShutdownClient("Client closing");
    }

    private void NetworkLoop()
    {
        var stopwatch = Stopwatch.StartNew();
        var nextTick = TickRate.Interval;

        Connect();

        while (_running)
        {
            Tick();

            _tickCount++;
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

    private void Tick()
    {
        ReadMessages();

        if (!_connecting && !_active && _tickCount >= _nextConnectTick)
        {
            Reconnect();
            return;
        }

        if (!_active)
        {
            return;
        }

        SendLocalPosition();

        while (_outgoingBlocks.TryDequeue(out var change))
        {
            SendBlockChangePacket(change);
        }
    }

    private void ReadMessages()
    {
        while (_client.ReadMessage(out var message))
        {
            try
            {
                switch (message.MessageType)
                {
                    case NetIncomingMessageType.Data:
                        HandleData(message);
                        break;

                    case NetIncomingMessageType.StatusChanged:
                        HandleStatusChanged(message);
                        break;

                    case NetIncomingMessageType.WarningMessage:
                    case NetIncomingMessageType.ErrorMessage:
                        Statuses.Enqueue(message.ReadString());
                        break;
                }
            }
            finally
            {
                _client.Recycle(message);
            }
        }
    }

    private void HandleData(NetIncomingMessage message)
    {
        var packetType = (PacketType)message.ReadByte();
        message.ReadString(); // group id

        switch (packetType)
        {
            case PacketType.Login:
            case PacketType.AllPlayers:
                PlayerLists.Enqueue(PlayerProtocol.ReadPlayerList(message));
                break;

            case PacketType.PlayerPosition:
                PlayerUpdates.Enqueue(PlayerProtocol.ReadPlayer(message));
                break;

            case PacketType.WorldInfo:
                var info = WorldProtocol.ReadWorldInfo(message);
                _worldHeight = info.SizeY;
                WorldInfos.Enqueue(info);
                break;

            case PacketType.ChunkSnapshot:
                Chunks.Enqueue(WorldProtocol.ReadChunkSnapshot(message, _worldHeight));
                break;

            case PacketType.BlockUpdate:
                Changes.Enqueue(WorldProtocol.ReadBlockUpdate(message));
                break;

            case PacketType.EntitySpawn:
                EntitySpawns.Enqueue(WorldProtocol.ReadEntitySpawn(message));
                break;

            case PacketType.EntityUpdate:
                var (uid, ux, uy, uz, uyaw) = WorldProtocol.ReadEntityUpdate(message);
                EntityUpdates.Enqueue((uid, ux, uy, uz, uyaw));
                break;

            case PacketType.EntityRemove:
                EntityRemoves.Enqueue(WorldProtocol.ReadEntityRemove(message));
                break;

            case PacketType.EntityHit:
                EntityHits.Enqueue(WorldProtocol.ReadEntityHit(message));
                break;

            case PacketType.Kick:
                Kicks.Enqueue(message.ReadString());
                break;
        }
    }

    private void HandleStatusChanged(NetIncomingMessage message)
    {
        var status = (NetConnectionStatus)message.ReadByte();
        var reason = message.ReadString();

        switch (status)
        {
            case NetConnectionStatus.Connected:
                _connecting = false;
                _active = true;
                Statuses.Enqueue("connected");
                break;

            case NetConnectionStatus.Disconnected:
                _connecting = false;
                _active = false;
                _nextConnectTick = _tickCount + ReconnectDelayTicks;
                Statuses.Enqueue(
                    string.IsNullOrEmpty(reason)
                        ? "server not running"
                        : $"server not running ({reason})");
                break;
        }
    }

    private void SendLocalPosition()
    {
        NetOutgoingMessage message;

        lock (_localPositionLock)
        {
            message = _client.CreateMessage();
            message.Write((byte)PacketType.Input);
            message.Write(GroupId);
            message.Write(Username);
            message.WriteAllProperties(_localPosition);
        }

        _client.SendMessage(message, NetDeliveryMethod.UnreliableSequenced, 0);
    }

    private void SendBlockChangePacket(BlockChange change)
    {
        var message = WorldProtocol.CreateBlockUpdate(
            _client,
            GroupId,
            change.X,
            change.Y,
            change.Z,
            change.Block);

        _client.SendMessage(message, NetDeliveryMethod.ReliableOrdered);
    }

    private void Connect()
    {
        _connecting = true;
        Statuses.Enqueue("connecting");

        var approval = _client.CreateMessage();
        approval.Write(GroupId);
        approval.Write((byte)PacketType.Login);
        approval.Write(Username);

        _client.Connect(_host, _port, approval);
    }

    private void Reconnect()
    {
        ShutdownClient("Reconnecting");
        CreateClient();
        Connect();
    }

    private void CreateClient()
    {
        var config = new NetPeerConfiguration("networkGame")
        {
            ReceiveBufferSize = SocketBufferSize,
            SendBufferSize = SocketBufferSize,
            ConnectionTimeout = 10f,
        };

        _client = new NetClient(config);
        _client.Start();
    }

    private void ShutdownClient(string reason)
    {
        try
        {
            if (_client is not null && _client.Status == NetPeerStatus.Running)
            {
                _client.Shutdown(new NetReason(reason), reason);
            }
        }
        catch (NetException)
        {
        }
    }
}
