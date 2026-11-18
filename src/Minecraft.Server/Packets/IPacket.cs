using Lidgren.Network;

namespace LetsCreateNetworkGame.Server.Packets;

/// <summary>Upstream-style packet handler without rooms or game-state managers.</summary>
internal interface IPacket
{
    void Run(NetworkServer server, NetIncomingMessage message, string groupId);
}
