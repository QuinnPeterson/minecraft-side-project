using LetsCreateNetworkGame.Server;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Net;
using Minecraft.Shared.Worlds;

namespace Minecraft.Server;

internal static class Program
{
    private const int WorldSizeX = 128;
    private const int WorldSizeZ = 128;
    private const int WorldHeight = 128;

    private static World _world = null!;
    private static NetworkServer _network = null!;
    private static Thread _networkThread = null!;
    private static CancellationTokenSource _stop = null!;

    private static void Main(string[] args)
    {
        Console.Title = "Minecraft Server";

        Console.WriteLine("=== Minecraft Server ===");
        Console.WriteLine();

        var seedText = Environment.GetEnvironmentVariable("MINECRAFT_SEED");
        var seed = int.TryParse(seedText, out var requestedSeed)
            ? requestedSeed
            : Random.Shared.Next();

        Console.WriteLine($"Generating a {WorldSizeX} x {WorldHeight} x {WorldSizeZ} world (seed {seed})...");
        var started = DateTime.UtcNow;
        var generated = TerrainGenerator.Generate(WorldSizeX, WorldSizeZ, WorldHeight, seed);
        _world = generated.World;
        Console.WriteLine($"Terrain generated in {(DateTime.UtcNow - started).TotalSeconds:0.0}s.");
        Console.WriteLine($"Spawn: {generated.SpawnX},{generated.SpawnY},{generated.SpawnZ}");

        _network = new NetworkServer(
            _world,
            generated.Seed,
            generated.SpawnX,
            generated.SpawnY,
            generated.SpawnZ,
            message => Console.WriteLine(message));

        Console.WriteLine($"World size: {_world.SizeX} x {_world.SizeY} x {_world.SizeZ}");
        Console.WriteLine($"{BlockRegistry.All.Count} block types loaded from the shared library:");
        PrintBlocks();
        Console.WriteLine();

        PrintHelp();
        StartNetwork();

        var headless = args.Any(arg =>
                           string.Equals(arg, "--headless", StringComparison.OrdinalIgnoreCase)) ||
                       Environment.GetEnvironmentVariable("MINECRAFT_HEADLESS") == "1";

        if (headless)
        {
            // Hosted runs have no interactive console. Stay up until the process
            // is stopped (Ctrl+C cancels the wait cleanly when attached to one).
            using var stopSignal = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stopSignal.Set();
            };

            Console.WriteLine("Headless mode: press Ctrl+C to stop.");
            stopSignal.Wait();
        }
        else
        {
            while (true)
            {
                Console.Write("> ");
                var input = Console.ReadLine();

                if (input is null || !Execute(input))
                {
                    break;
                }
            }
        }

        StopNetwork();
        Console.WriteLine("Server stopped.");
    }

    private static void StartNetwork()
    {
        _stop = new CancellationTokenSource();
        _networkThread = new Thread(() => _network.Run(_stop.Token))
        {
            IsBackground = true,
            Name = "Lidgren server",
        };
        _networkThread.Start();
    }

    private static void StopNetwork()
    {
        _stop.Cancel();
        _networkThread?.Join(2000);
    }

    private static bool Execute(string input)
    {
        var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return true;
        }

        switch (parts[0].ToLowerInvariant())
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

            case "players":
                PrintPlayers();
                return true;

            case "get":
                if (TryReadCoordinates(parts, out var getX, out var getY, out var getZ))
                {
                    var block = _network.GetBlock(getX, getY, getZ);
                    Console.WriteLine($"{getX},{getY},{getZ} = {BlockRegistry.Get(block).Name}");
                }

                return true;

            case "set":
                SetBlock(parts);
                return true;

            case "kick":
                KickPlayer(parts);
                return true;

            case "quit":
            case "exit":
                return false;

            default:
                Console.WriteLine($"Unknown command '{parts[0]}'. Type 'help' for a list.");
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

        var change = new BlockChange(x, y, z, block.Id);

        if (!_network.SetBlock("world", change))
        {
            Console.WriteLine($"Position {x},{y},{z} is outside the world.");
            return;
        }

        Console.WriteLine($"Placed {block.Name} at {x},{y},{z}.");
    }

    private static void KickPlayer(string[] parts)
    {
        if (parts.Length != 2)
        {
            Console.WriteLine("Usage: kick <username>");
            return;
        }

        var player = _network.FindPlayer(parts[1]);

        if (player is null)
        {
            Console.WriteLine($"Player '{parts[1]}' is not online.");
            return;
        }

        _network.Kick(parts[1]);
        Console.WriteLine($"Kicked {parts[1]}.");
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
        var counts = _network.CountBlocks();

        foreach (var block in BlockRegistry.All)
        {
            var count = counts.GetValueOrDefault(block.Id);

            if (count > 0)
            {
                Console.WriteLine($"  {block.Name,-8} {count}");
            }
        }
    }

    private static void PrintPlayers()
    {
        var players = _network.GetPlayers();

        if (players.Count == 0)
        {
            Console.WriteLine("  (no players online)");
            return;
        }

        foreach (var player in players)
        {
            var position = player.Player.Position;
            Console.WriteLine(
                $"  {player.Player.Username} [{player.GroupId}] " +
                $"({position.XPosition:0},{position.YPosition:0},{position.ZPosition:0})");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  blocks                        List every registered block");
        Console.WriteLine("  stats                         Count blocks in the world");
        Console.WriteLine("  players                       List connected players");
        Console.WriteLine("  get <x> <y> <z>               Read a block");
        Console.WriteLine("  set <x> <y> <z> <block>       Place a block (updates all clients live)");
        Console.WriteLine("  kick <username>               Disconnect a player");
        Console.WriteLine("  help                          Show this help");
        Console.WriteLine("  quit                          Stop the server");
        Console.WriteLine();
    }
}
