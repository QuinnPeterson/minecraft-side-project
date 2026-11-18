using Lidgren.Network;

namespace LetsCreateNetworkGame.Server.Packets;

internal sealed class AllPlayersPacket : IPacket
{
    public void Run(NetworkServer server, NetIncomingMessage message, string groupId)
    {
        server.NetServer.SendMessage(
            server.CreatePlayerListMessage(groupId),
            message.SenderConnection,
            NetDeliveryMethod.ReliableOrdered,
            0);
    }
}
