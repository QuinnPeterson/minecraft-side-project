using Minecraft.Shared.Net;

namespace LetsCreateNetworkGame.Server.Packets;

internal static class PacketFactory
{
    public static IPacket GetPacket(PacketType packetType)
    {
        return packetType switch
        {
            PacketType.Login => new LoginPacket(),
            PacketType.PlayerPosition => new PlayerPositionPacket(),
            PacketType.AllPlayers => new AllPlayersPacket(),
            PacketType.Input => new InputPacket(),
            PacketType.Kick => new KickPacket(),
            PacketType.BlockUpdate => new BlockUpdatePacket(),
            _ => new IgnorePacket(),
        };
    }
}
