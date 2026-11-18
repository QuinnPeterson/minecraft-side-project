namespace Minecraft.Shared.Blocks;

/// <summary>
/// How the client should build the block's geometry.
/// Cube is the default full block; Water and Cross have custom meshes.
/// </summary>
public enum BlockShape : byte
{
    Cube,
    Water,
    Cross,
}

/// <summary>
/// Describes a single block type. This record is the single source of truth
/// shared by the client (rendering) and the server (world simulation).
/// </summary>
/// <param name="Id">Stable numeric id. Saved in worlds and sent over the network.</param>
/// <param name="Name">Human readable name, also used by console commands.</param>
/// <param name="Color">Placeholder color in 0xRRGGBB format until textures exist.</param>
/// <param name="IsSolid">Whether entities collide with this block.</param>
/// <param name="IsTransparent">Whether blocks behind this one can be seen.</param>
/// <param name="Alpha">Opacity used by the client's transparent pass.</param>
/// <param name="Shape">Selects the client-side block mesh builder.</param>
public sealed record BlockDefinition(
    BlockId Id,
    string Name,
    uint Color,
    bool IsSolid,
    bool IsTransparent,
    float Alpha = 1f,
    BlockShape Shape = BlockShape.Cube);
