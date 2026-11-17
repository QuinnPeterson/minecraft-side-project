using System;
using System.Collections.Concurrent;
using Lidgren.Network;
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;

namespace Minecraft.Client;

/// <summary>
/// Keeps a Lidgren client pointed at the server and queues incoming messages
/// so the game loop can apply them without blocking.
/// </summary>
internal sealed class ServerConnection : IDisposable
{
    // The world snapshot is larger than the default socket buffers, so raise them.
    private const int SocketBufferSize = 4 * 1024 * 1024;
    private const int ReconnectDelayMs = 2000;

    private string _host = "127.0.0.1";
    private int _port = WorldProtocol.DefaultPort;

    private NetClient _client;
    private bool _started;
    private bool _connecting;
    private long _nextConnectAttempt;

    public ConcurrentQueue<World> Snapshots { get; } = new();

    public ConcurrentQueue<BlockChange> Changes { get; } = new();

    public ConcurrentQueue<string> Statuses { get; } = new();

    public ServerConnection()
    {
        _client = CreateClient();
    }

    public void Start(string host, int port)
    {
        _host = host;
        _port = port;
        _client.Start();
        _started = true;
        Connect();
    }

    /// <summary>Polls Lidgren for messages. Call this once per frame on the game thread.</summary>
    public void Update()
    {
        if (!_started)
        {
            return;
        }

        NetIncomingMessage message;
        while (_client.ReadMessage(out message))
        {
            try
            {
                HandleMessage(message);
            }
            finally
            {
                _client.Recycle(message);
            }
        }

        if (!_connecting && Environment.TickCount64 >= _nextConnectAttempt)
        {
            Reconnect();
        }
    }

    public void Dispose()
    {
        _started = false;
        ShutdownClient();
    }

    private static NetClient CreateClient()
    {
        var config = new NetPeerConfiguration(WorldProtocol.AppIdentifier)
        {
            ReceiveBufferSize = SocketBufferSize,
            SendBufferSize = SocketBufferSize,
            ConnectionTimeout = 10f,
        };

        return new NetClient(config);
    }

    private void Connect()
    {
        _connecting = true;
        Statuses.Enqueue("connecting");
        _client.Connect(_host, _port);
    }

    private void Reconnect()
    {
        // A Lidgren client holds a single connection, so replace the peer.
        ShutdownClient();
        _client = CreateClient();
        _client.Start();
        Connect();
    }

    private void ShutdownClient()
    {
        try
        {
            if (_client.Status == NetPeerStatus.Running)
            {
                _client.Disconnect("Client closing");
            }
        }
        catch (Exception exception) when (exception is NetException or InvalidOperationException)
        {
        }

        try
        {
            _client.Shutdown(NetReason.Empty, "Client closing");
        }
        catch (NetException)
        {
        }
    }

    private void HandleMessage(NetIncomingMessage message)
    {
        switch (message.MessageType)
        {
            case NetIncomingMessageType.StatusChanged:
                HandleStatusChanged(message);
                break;

            case NetIncomingMessageType.Data:
                HandleData(message);
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
                Statuses.Enqueue("connected");
                break;

            case NetConnectionStatus.Disconnected:
                _connecting = false;
                _nextConnectAttempt = Environment.TickCount64 + ReconnectDelayMs;
                Statuses.Enqueue(
                    string.IsNullOrEmpty(reason)
                        ? "server not running"
                        : $"server not running ({reason})");
                break;
        }
    }

    private void HandleData(NetIncomingMessage message)
    {
        var messageType = message.ReadByte();

        switch (messageType)
        {
            case WorldProtocol.WorldSnapshot:
                Snapshots.Enqueue(WorldProtocol.ReadWorldSnapshot(message));
                break;

            case WorldProtocol.BlockUpdate:
                Changes.Enqueue(WorldProtocol.ReadBlockUpdate(message));
                break;
        }
    }
}
