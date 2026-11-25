using LetsCreateNetworkGame.OpenGL.Library;
using Lidgren.Network;

namespace LetsCreateNetworkGame.Server;

/// <summary>A logged-in player and its network connection.</summary>
internal sealed class PlayerConnection
{
    public Player Player { get; }

    public NetConnection Connection { get; }

    public string GroupId { get; }

    /// <summary>The world this player is currently visiting.</summary>
    public byte World { get; set; }

    /// <summary>Prevents immediate re-teleport after entering a portal.</summary>
    public double PortalCooldown { get; set; }

    /// <summary>Where to return in the overworld after leaving the nether.</summary>
    public Position? OverworldReturn { get; set; }

    public PlayerConnection(Player player, NetConnection connection, string groupId)
    {
        Player = player;
        Connection = connection;
        GroupId = groupId;
    }
}
