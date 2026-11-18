using LetsCreateNetworkGame.OpenGL.Library;
using Lidgren.Network;

namespace LetsCreateNetworkGame.Server.Packets;

/// <summary>Client-to-server player transform update, adapted from upstream Key input.</summary>
internal sealed class InputPacket : IPacket
{
    public void Run(NetworkServer server, NetIncomingMessage message, string groupId)
    {
        var username = message.ReadString();
        var position = new Position();
        message.ReadAllProperties(position);

        var player = server.FindPlayer(username);

        if (player is null)
        {
            return;
        }

        server.UpdatePlayerPosition(player, position);
        server.Broadcast(
            server.CreatePlayerPositionMessage(groupId, player.Player),
            NetDeliveryMethod.UnreliableSequenced);
    }
}
