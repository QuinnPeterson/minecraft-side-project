using Lidgren.Network;

namespace LetsCreateNetworkGame.Server.Packets;

internal sealed class IgnorePacket : IPacket
{
    public void Run(NetworkServer server, NetIncomingMessage message, string groupId)
    {
    }
}
