using Lidgren.Network;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Worlds;

namespace Minecraft.Shared.Net;

/// <summary>A single block edit sent over the Lidgren packet stream.</summary>
public readonly record struct BlockChange(int X, int Y, int Z, BlockId Block);

/// <summary>
/// World packets carried by the upstream-style Lidgren transport. Every packet
/// starts with a <see cref="PacketType"/> byte followed by its payload.
/// </summary>
public static class WorldProtocol
{
    public static NetOutgoingMessage CreateWorldSnapshot(NetPeer peer, string groupId, World world)
    {
        var blockCount = world.SizeX * world.SizeY * world.SizeZ;
        var message = peer.CreateMessage(13 + groupId.Length + blockCount * sizeof(ushort));

        message.Write((byte)PacketType.WorldSnapshot);
        message.Write(groupId);
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

    /// <summary>The packet type and group id are read by the caller.</summary>
    public static World ReadWorldSnapshot(NetBuffer message)
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
        string groupId,
        int x,
        int y,
        int z,
        BlockId block)
    {
        var message = peer.CreateMessage(17 + groupId.Length);

        message.Write((byte)PacketType.BlockUpdate);
        message.Write(groupId);
        message.Write(x);
        message.Write(y);
        message.Write(z);
        message.Write((ushort)block);
        return message;
    }

    /// <summary>The packet type and group id are read by the caller.</summary>
    public static BlockChange ReadBlockUpdate(NetBuffer message)
    {
        return new BlockChange(
            message.ReadInt32(),
            message.ReadInt32(),
            message.ReadInt32(),
            (BlockId)message.ReadUInt16());
    }
}
