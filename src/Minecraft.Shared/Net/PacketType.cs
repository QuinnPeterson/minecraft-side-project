namespace Minecraft.Shared.Net;

/// <summary>
/// Packet identifiers shared by the client and server. The first five values
/// follow the upstream SpeedCodingNetworkGame protocol; world packets are added
/// so the same transport can carry the 3D Minecraft world.
/// </summary>
public enum PacketType : byte
{
    Login,
    PlayerPosition,
    AllPlayers,
    Input,
    Kick,
    WorldSnapshot,
    BlockUpdate,
}
