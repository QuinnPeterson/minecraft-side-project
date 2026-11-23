using Lidgren.Network;
using Minecraft.Shared.Blocks;
using Minecraft.Shared.Worlds;

namespace Minecraft.Shared.Net;

/// <summary>A single block edit sent over the Lidgren packet stream.</summary>
public readonly record struct BlockChange(int X, int Y, int Z, BlockId Block);

/// <summary>The dimensions and spawn point of the server's world.</summary>
public readonly record struct WorldInfo(
    int SizeX,
    int SizeY,
    int SizeZ,
    int Seed,
    int SpawnX,
    int SpawnY,
    int SpawnZ);

/// <summary>One 16x16 column of blocks. Blocks are indexed x, z, y.</summary>
public readonly record struct ChunkSnapshot(int ChunkX, int ChunkZ, BlockId[] Blocks);

/// <summary>
/// World packets carried by the upstream-style Lidgren transport. Every packet
/// starts with a <see cref="PacketType"/> byte followed by its payload.
///
/// Chunks use a tiny run-length encoding because the generated world is mostly
/// air, stone, and water. That keeps the initial world transfer small enough
/// for one reliable stream without switching to a different transport.
/// </summary>
public static class WorldProtocol
{
    public const int ChunkSize = 16;

    private const byte ChunkEncodingRaw = 0;
    private const byte ChunkEncodingRle = 1;

    public static NetOutgoingMessage CreateWorldInfo(
        NetPeer peer,
        string groupId,
        WorldInfo info)
    {
        var message = peer.CreateMessage(48 + groupId.Length);

        message.Write((byte)PacketType.WorldInfo);
        message.Write(groupId);
        message.Write(info.SizeX);
        message.Write(info.SizeY);
        message.Write(info.SizeZ);
        message.Write(info.Seed);
        message.Write(info.SpawnX);
        message.Write(info.SpawnY);
        message.Write(info.SpawnZ);
        return message;
    }

    /// <summary>The packet type and group id are read by the caller.</summary>
    public static WorldInfo ReadWorldInfo(NetBuffer message)
    {
        return new WorldInfo(
            message.ReadInt32(),
            message.ReadInt32(),
            message.ReadInt32(),
            message.ReadInt32(),
            message.ReadInt32(),
            message.ReadInt32(),
            message.ReadInt32());
    }

    public static NetOutgoingMessage CreateChunkSnapshot(
        NetPeer peer,
        string groupId,
        World world,
        int chunkX,
        int chunkZ)
    {
        var blocks = ReadChunkBlocks(world, chunkX, chunkZ);
        var runs = CountRuns(blocks);
        var rleSize = 4 + runs * 4;
        var rawSize = blocks.Length * sizeof(ushort);
        var useRle = rleSize < rawSize;
        var payloadSize = 1 + 4 + (useRle ? rleSize : rawSize);
        var message = peer.CreateMessage(1 + 4 + groupId.Length + 8 + payloadSize);

        message.Write((byte)PacketType.ChunkSnapshot);
        message.Write(groupId);
        message.Write(chunkX);
        message.Write(chunkZ);
        message.Write(useRle ? ChunkEncodingRle : ChunkEncodingRaw);
        message.Write(blocks.Length);

        if (!useRle)
        {
            foreach (var block in blocks)
            {
                message.Write((ushort)block);
            }

            return message;
        }

        message.Write(runs);

        var index = 0;

        while (index < blocks.Length)
        {
            var block = blocks[index];
            var length = 1;

            while (index + length < blocks.Length &&
                   blocks[index + length] == block &&
                   length < ushort.MaxValue)
            {
                length++;
            }

            message.Write((ushort)block);
            message.Write((ushort)length);
            index += length;
        }

        return message;
    }

    /// <summary>The packet type and group id are read by the caller.</summary>
    public static ChunkSnapshot ReadChunkSnapshot(NetBuffer message, int worldHeight)
    {
        var chunkX = message.ReadInt32();
        var chunkZ = message.ReadInt32();
        var encoding = message.ReadByte();
        var count = message.ReadInt32();
        var expected = ChunkSize * ChunkSize * worldHeight;

        if (count != expected)
        {
            throw new NetException(
                $"Chunk payload has {count} blocks, expected {expected}.");
        }

        var blocks = new BlockId[count];

        if (encoding == ChunkEncodingRle)
        {
            var runs = message.ReadInt32();
            var index = 0;

            for (var run = 0; run < runs; run++)
            {
                var block = (BlockId)message.ReadUInt16();
                var length = message.ReadUInt16();

                for (var i = 0; i < length && index < blocks.Length; i++)
                {
                    blocks[index++] = block;
                }
            }

            if (index != blocks.Length)
            {
                throw new NetException("Chunk run-length payload ended early.");
            }
        }
        else if (encoding == ChunkEncodingRaw)
        {
            for (var i = 0; i < blocks.Length; i++)
            {
                blocks[i] = (BlockId)message.ReadUInt16();
            }
        }
        else
        {
            throw new NetException($"Unknown chunk encoding {encoding}.");
        }

        return new ChunkSnapshot(chunkX, chunkZ, blocks);
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

    private static BlockId[] ReadChunkBlocks(World world, int chunkX, int chunkZ)
    {
        var blocks = new BlockId[ChunkSize * ChunkSize * world.SizeY];
        var index = 0;
        var baseX = chunkX * ChunkSize;
        var baseZ = chunkZ * ChunkSize;

        for (var x = 0; x < ChunkSize; x++)
        {
            for (var z = 0; z < ChunkSize; z++)
            {
                for (var y = 0; y < world.SizeY; y++)
                {
                    blocks[index++] = world.GetBlock(baseX + x, y, baseZ + z);
                }
            }
        }

        return blocks;
    }

    private static int CountRuns(BlockId[] blocks)
    {
        var runs = 1;

        for (var i = 1; i < blocks.Length; i++)
        {
            if (blocks[i] != blocks[i - 1])
            {
                runs++;
            }
        }

        return runs;
    }
    public static NetOutgoingMessage CreateEntitySpawn(NetPeer peer, string groupId, int id, Minecraft.Shared.Entities.EntityType type, float x, float y, float z, int data)
    {
        var message = peer.CreateMessage(24 + groupId.Length);
        message.Write((byte)PacketType.EntitySpawn);
        message.Write(groupId);
        message.Write(id);
        message.Write((byte)type);
        message.Write(x);
        message.Write(y);
        message.Write(z);
        message.Write(data);
        return message;
    }

    public static Minecraft.Shared.Entities.EntitySnapshot ReadEntitySpawn(NetBuffer message)
    {
        var id = message.ReadInt32();
        var type = (Minecraft.Shared.Entities.EntityType)message.ReadByte();
        var x = message.ReadFloat();
        var y = message.ReadFloat();
        var z = message.ReadFloat();
        var data = message.ReadInt32();
        return new Minecraft.Shared.Entities.EntitySnapshot(id, type, x, y, z, data);
    }

    public static NetOutgoingMessage CreateEntityUpdate(NetPeer peer, string groupId, int id, float x, float y, float z, float yaw)
    {
        var message = peer.CreateMessage(24 + groupId.Length);
        message.Write((byte)PacketType.EntityUpdate);
        message.Write(groupId);
        message.Write(id);
        message.Write(x);
        message.Write(y);
        message.Write(z);
        message.Write(yaw);
        return message;
    }

    public static (int Id, float X, float Y, float Z, float Yaw) ReadEntityUpdate(NetBuffer message)
    {
        var id = message.ReadInt32();
        var x = message.ReadFloat();
        var y = message.ReadFloat();
        var z = message.ReadFloat();
        var yaw = message.ReadFloat();
        return (id, x, y, z, yaw);
    }

    public static NetOutgoingMessage CreateEntityHit(NetPeer peer, string groupId, int id)
    {
        var message = peer.CreateMessage(8 + groupId.Length);
        message.Write((byte)PacketType.EntityHit);
        message.Write(groupId);
        message.Write(id);
        return message;
    }

    public static int ReadEntityHit(NetBuffer message) => message.ReadInt32();

    public static NetOutgoingMessage CreateEntityRemove(NetPeer peer, string groupId, int id)
    {
        var message = peer.CreateMessage(8 + groupId.Length);
        message.Write((byte)PacketType.EntityRemove);
        message.Write(groupId);
        message.Write(id);
        return message;
    }

    public static int ReadEntityRemove(NetBuffer message) => message.ReadInt32();

}
