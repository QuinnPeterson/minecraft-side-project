using LetsCreateNetworkGame.OpenGL.Library;
using Lidgren.Network;

namespace Minecraft.Shared.Net;

/// <summary>Serialization helpers for the upstream player packets.</summary>
public static class PlayerProtocol
{
    public static void WritePlayer(NetBuffer message, Player player)
    {
        message.Write(player.Username);
        message.WriteAllProperties(player.Position);
    }

    public static Player ReadPlayer(NetBuffer message)
    {
        var username = message.ReadString();
        var position = new Position();
        message.ReadAllProperties(position);
        return new Player(username, position);
    }

    /// <summary>
    /// Writes the upstream full-player-list payload. The boolean keeps the
    /// upstream camera-update field, even though this build has no camera room.
    /// </summary>
    public static void WritePlayerList(
        NetBuffer message,
        bool cameraUpdate,
        IReadOnlyList<Player> players)
    {
        message.Write(cameraUpdate);
        message.Write(players.Count);

        foreach (var player in players)
        {
            WritePlayer(message, player);
        }
    }

    public static IReadOnlyList<Player> ReadPlayerList(NetBuffer message)
    {
        message.ReadBoolean();
        var count = message.ReadInt32();
        var players = new List<Player>(count);

        for (var i = 0; i < count; i++)
        {
            players.Add(ReadPlayer(message));
        }

        return players;
    }
}
