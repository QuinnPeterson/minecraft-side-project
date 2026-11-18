using LetsCreateNetworkGame.OpenGL.Library;
using Lidgren.Network;

namespace LetsCreateNetworkGame.Server;

/// <summary>A logged-in player and its network connection.</summary>
internal sealed class PlayerConnection
{
    public Player Player { get; }

    public NetConnection Connection { get; }

    public string GroupId { get; }

    public PlayerConnection(Player player, NetConnection connection, string groupId)
    {
        Player = player;
        Connection = connection;
        GroupId = groupId;
    }
}
