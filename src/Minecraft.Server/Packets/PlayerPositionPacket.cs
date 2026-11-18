using LetsCreateNetworkGame.OpenGL.Library;
using Lidgren.Network;

namespace LetsCreateNetworkGame.Server.Packets;

internal sealed class PlayerPositionPacket : IPacket
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
        server.Broadcast(server.CreatePlayerPositionMessage(groupId, player.Player));
    }
}
