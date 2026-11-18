using Lidgren.Network;

namespace LetsCreateNetworkGame.Server.Packets;

internal sealed class KickPacket : IPacket
{
    public void Run(NetworkServer server, NetIncomingMessage message, string groupId)
    {
        server.Kick(message.ReadString());
    }
}
