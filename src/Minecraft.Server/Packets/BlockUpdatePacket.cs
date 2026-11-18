using Minecraft.Shared.Blocks;
using Minecraft.Shared.Net;

namespace LetsCreateNetworkGame.Server.Packets;

/// <summary>Applies and rebroadcasts a block edit from a client.</summary>
internal sealed class BlockUpdatePacket : IPacket
{
    public void Run(NetworkServer server, Lidgren.Network.NetIncomingMessage message, string groupId)
    {
        var change = WorldProtocol.ReadBlockUpdate(message);

        if (!BlockRegistry.TryGet(change.Block, out var definition))
        {
            return;
        }

        if (!server.SetBlock(groupId, change))
        {
            return;
        }

        Console.WriteLine($"Client placed {definition.Name} at {change.X},{change.Y},{change.Z}.");
    }
}
