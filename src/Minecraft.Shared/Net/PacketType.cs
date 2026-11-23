namespace Minecraft.Shared.Net;

/// <summary>
/// Packet identifiers shared by the client and server. The first five values
/// follow the upstream SpeedCodingNetworkGame protocol; world packets are
/// extensions carried by the same Lidgren transport.
/// </summary>
public enum PacketType : byte
{
    Login,
    PlayerPosition,
    AllPlayers,
    Input,
    Kick,
    WorldInfo,
    ChunkSnapshot,
    BlockUpdate,
    EntitySpawn,
    EntityUpdate,
    EntityRemove,
    EntityPickup,
    EntityHit,
}
