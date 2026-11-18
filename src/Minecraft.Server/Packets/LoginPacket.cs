using Lidgren.Network;

namespace LetsCreateNetworkGame.Server.Packets;

/// <summary>Sends the current player list to a client that requested login data.</summary>
internal sealed class LoginPacket : IPacket
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
