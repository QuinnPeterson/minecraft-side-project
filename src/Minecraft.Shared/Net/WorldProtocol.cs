using Lidgren.Network;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Worlds;

namespace Minecraft.Shared.Net;

/// <summary>A single block edit sent from the server to its clients.</summary>
public readonly record struct BlockChange(int X, int Y, int Z, BlockId Block);

/// <summary>
/// Wire protocol shared by the server and the client.
/// Every message starts with a type byte, followed by its payload.
/// </summary>
public static class WorldProtocol
{
    /// <summary>Lidgren refuses connections between peers with different identifiers.</summary>
    public const string AppIdentifier = "MinecraftSideProject";

    public const int DefaultPort = 25565;

    public const byte WorldSnapshot = 1;
    public const byte BlockUpdate = 2;

    /// <summary>
    /// Builds a full copy of the world. Lidgren fragments and reassembles
    /// messages larger than the MTU automatically, so no manual chunking is needed.
    /// </summary>
    public static NetOutgoingMessage CreateWorldSnapshot(NetPeer peer, World world)
    {
        var blockCount = world.SizeX * world.SizeY * world.SizeZ;
        var message = peer.CreateMessage(13 + blockCount * sizeof(ushort));

        message.Write(WorldSnapshot);
        message.Write(world.SizeX);
        message.Write(world.SizeY);
        message.Write(world.SizeZ);

        for (var x = 0; x < world.SizeX; x++)
        {
            for (var y = 0; y < world.SizeY; y++)
            {
                for (var z = 0; z < world.SizeZ; z++)
                {
                    message.Write((ushort)world.GetBlock(x, y, z));
                }
            }
        }

        return message;
    }

    public static World ReadWorldSnapshot(NetIncomingMessage message)
    {
        var sizeX = message.ReadInt32();
        var sizeY = message.ReadInt32();
        var sizeZ = message.ReadInt32();
        var world = new World(sizeX, sizeY, sizeZ);

        for (var x = 0; x < sizeX; x++)
        {
            for (var y = 0; y < sizeY; y++)
            {
                for (var z = 0; z < sizeZ; z++)
                {
                    world.SetBlock(x, y, z, (BlockId)message.ReadUInt16());
                }
            }
        }

        return world;
    }

    public static NetOutgoingMessage CreateBlockUpdate(
        NetPeer peer,
        int x,
        int y,
        int z,
        BlockId block)
    {
        var message = peer.CreateMessage(15);
        message.Write(BlockUpdate);
        message.Write(x);
        message.Write(y);
        message.Write(z);
        message.Write((ushort)block);
        return message;
    }

    public static BlockChange ReadBlockUpdate(NetIncomingMessage message) =>
        new(message.ReadInt32(), message.ReadInt32(), message.ReadInt32(), (BlockId)message.ReadUInt16());
}
