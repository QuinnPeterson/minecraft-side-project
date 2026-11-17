using System.Net.Sockets;
using Lidgren.Network;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;

namespace Minecraft.Server;

internal static class Program
{
    private const int WorldSizeX = 32;
    private const int WorldSizeZ = 32;

    // The world snapshot is larger than the default socket buffers, so raise them.
    private const int SocketBufferSize = 4 * 1024 * 1024;

    private static readonly object WorldLock = new();

    private static World _world = null!;
    private static NetServer _server = null!;
    private static Thread _networkThread = null!;
    private static volatile bool _networkRunning;

    private static void Main()
    {
        Console.Title = "Minecraft Server";

        Console.WriteLine("=== Minecraft Server ===");
        Console.WriteLine();

        // The server owns the authoritative world. Both projects use the same
        // BlockRegistry, so any block registered there is valid here too.
        _world = FlatWorldGenerator.Generate(WorldSizeX, WorldSizeZ);

        Console.WriteLine($"World size: {_world.SizeX} x {_world.SizeY} x {_world.SizeZ}");
        Console.WriteLine($"{BlockRegistry.All.Count} block types loaded from the shared library:");
        PrintBlocks();
        Console.WriteLine();

        PrintHelp();
        StartNetwork();

        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine();

            if (input is null || !Execute(input))
            {
                break;
            }
        }

        StopNetwork();
        Console.WriteLine("Server stopped.");
    }

    private static void StartNetwork()
    {
        var config = new NetPeerConfiguration(WorldProtocol.AppIdentifier)
        {
            Port = WorldProtocol.DefaultPort,
            MaximumConnections = 16,
            ReceiveBufferSize = SocketBufferSize,
            SendBufferSize = SocketBufferSize,
        };

        _server = new NetServer(config);

        try
        {
            _server.Start();
        }
        catch (Exception exception) when (exception is NetException or SocketException)
        {
            Console.WriteLine(
                $"Network disabled: could not listen on UDP port {WorldProtocol.DefaultPort} ({exception.Message}).");
            Console.WriteLine("Block commands still work, but clients will not connect.");
            Console.WriteLine();
            _server = null!;
            return;
        }

        _networkRunning = true;
        _networkThread = new Thread(NetworkLoop)
        {
            IsBackground = true,
            Name = "Lidgren network",
        };
        _networkThread.Start();

        Console.WriteLine(
            $"Listening on UDP {WorldProtocol.DefaultPort} (Lidgren app '{WorldProtocol.AppIdentifier}').");
        Console.WriteLine("Start the client to see block changes live.");
        Console.WriteLine();
    }

    private static void StopNetwork()
    {
        _networkRunning = false;

        if (_server is not null)
        {
            _server.Shutdown(NetReason.Empty, "Server stopped");
        }

        _networkThread?.Join(1000);
    }

    private static void NetworkLoop()
    {
        try
        {
            while (_networkRunning)
            {
                NetIncomingMessage message;
                if (!_server.ReadMessage(out message))
                {
                    Thread.Sleep(5);
                    continue;
                }

                try
                {
                    HandleMessage(message);
                }
                finally
                {
                    _server.Recycle(message);
                }
            }
        }
        catch (NetException)
        {
            // The peer was shut down while we were reading.
        }
    }

    private static void HandleMessage(NetIncomingMessage message)
    {
        switch (message.MessageType)
        {
            case NetIncomingMessageType.StatusChanged:
                HandleStatusChanged(message);
                break;

            case NetIncomingMessageType.Data:
                HandleData(message);
                break;

            case NetIncomingMessageType.WarningMessage:
            case NetIncomingMessageType.ErrorMessage:
                Console.WriteLine($"[network] {message.ReadString()}");
                break;
        }
    }

    private static void HandleStatusChanged(NetIncomingMessage message)
    {
        var status = (NetConnectionStatus)message.ReadByte();
        var reason = message.ReadString();
        var connection = message.SenderConnection;

        if (connection is null)
        {
            return;
        }

        switch (status)
        {
            case NetConnectionStatus.Connected:
                Console.WriteLine($"Client connected: {connection.RemoteEndPoint}");
                SendWorldSnapshot(connection);
                break;

            case NetConnectionStatus.Disconnected:
                Console.WriteLine($"Client left: {connection.RemoteEndPoint} ({reason})");
                break;
        }
    }

    private static void HandleData(NetIncomingMessage message)
    {
        var messageType = message.ReadByte();

        if (messageType != WorldProtocol.BlockUpdate)
        {
            return;
        }

        var change = WorldProtocol.ReadBlockUpdate(message);

        if (!BlockRegistry.TryGet(change.Block, out var definition))
        {
            return;
        }

        var placed = false;

        lock (WorldLock)
        {
            placed = _world.SetBlock(change.X, change.Y, change.Z, change.Block);
        }

        if (!placed)
        {
            return;
        }

        Console.WriteLine($"Client placed {definition.Name} at {change.X},{change.Y},{change.Z}.");

        BroadcastBlockUpdate(change.X, change.Y, change.Z, change.Block);
    }

    private static void SendWorldSnapshot(NetConnection connection)
    {
        NetOutgoingMessage message;

        lock (WorldLock)
        {
            message = WorldProtocol.CreateWorldSnapshot(_server, _world);
        }

        _server.SendMessage(message, connection, NetDeliveryMethod.ReliableOrdered);
    }

    private static void BroadcastBlockUpdate(int x, int y, int z, BlockId block)
    {
        if (!_networkRunning || _server.ConnectionsCount == 0)
        {
            return;
        }

        var message = WorldProtocol.CreateBlockUpdate(_server, x, y, z, block);
        _server.SendToAll(message, NetDeliveryMethod.ReliableOrdered);
    }

    private static bool Execute(string input)
    {
        var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return true;
        }

        var command = parts[0].ToLowerInvariant();

        switch (command)
        {
            case "help":
                PrintHelp();
                return true;

            case "blocks":
                PrintBlocks();
                return true;

            case "stats":
                PrintStats();
                return true;

            case "get":
                if (!TryReadCoordinates(parts, out var getX, out var getY, out var getZ))
                {
                    return true;
                }

                BlockId block;
                lock (WorldLock)
                {
                    block = _world.GetBlock(getX, getY, getZ);
                }

                Console.WriteLine($"{getX},{getY},{getZ} = {BlockRegistry.Get(block).Name}");
                return true;

            case "set":
                SetBlock(parts);
                return true;

            case "quit":
            case "exit":
                return false;

            default:
                Console.WriteLine($"Unknown command '{command}'. Type 'help' for a list.");
                return true;
        }
    }

    private static void SetBlock(string[] parts)
    {
        if (parts.Length != 5)
        {
            Console.WriteLine("Usage: set <x> <y> <z> <block>");
            return;
        }

        if (!TryReadCoordinates(parts, out var x, out var y, out var z))
        {
            return;
        }

        if (!BlockRegistry.TryGetByName(parts[4], out var block))
        {
            Console.WriteLine($"Unknown block '{parts[4]}'. Type 'blocks' to see them all.");
            return;
        }

        bool placed;
        lock (WorldLock)
        {
            placed = _world.SetBlock(x, y, z, block.Id);
        }

        if (!placed)
        {
            Console.WriteLine($"Position {x},{y},{z} is outside the world.");
            return;
        }

        Console.WriteLine($"Placed {block.Name} at {x},{y},{z}.");
        BroadcastBlockUpdate(x, y, z, block.Id);
    }

    private static bool TryReadCoordinates(string[] parts, out int x, out int y, out int z)
    {
        x = y = z = 0;

        if (parts.Length < 4 ||
            !int.TryParse(parts[1], out x) ||
            !int.TryParse(parts[2], out y) ||
            !int.TryParse(parts[3], out z))
        {
            Console.WriteLine("Coordinates must be whole numbers: <x> <y> <z>");
            return false;
        }

        return true;
    }

    private static void PrintBlocks()
    {
        foreach (var block in BlockRegistry.All)
        {
            var flags = block.IsSolid ? "solid" : "passable";
            var transparency = block.IsTransparent ? ", transparent" : string.Empty;
            Console.WriteLine($"  {block.Name,-8} color #{block.Color:X6}  ({flags}{transparency})");
        }
    }

    private static void PrintStats()
    {
        IReadOnlyDictionary<BlockId, int> counts;
        lock (WorldLock)
        {
            counts = _world.CountBlocks();
        }

        foreach (var block in BlockRegistry.All)
        {
            var count = counts.GetValueOrDefault(block.Id);
            if (count > 0)
            {
                Console.WriteLine($"  {block.Name,-8} {count}");
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  blocks                        List every registered block");
        Console.WriteLine("  stats                         Count blocks in the world");
        Console.WriteLine("  get <x> <y> <z>               Read a block");
        Console.WriteLine("  set <x> <y> <z> <block>       Place a block (updates all clients live)");
        Console.WriteLine("  help                          Show this help");
        Console.WriteLine("  quit                          Stop the server");
        Console.WriteLine();
    }
}
