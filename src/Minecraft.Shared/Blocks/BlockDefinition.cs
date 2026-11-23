namespace Minecraft.Shared.Blocks;

/// <summary>
/// How the client should build the block's geometry.
/// Cube is the default full block; the others have custom meshes.
/// </summary>
public enum BlockShape : byte
{
    Cube,
    Water,
    Cross,
    Torch,
    Farmland,
    SnowLayer,
    Glass,
}

/// <summary>
/// Procedural texture tiles. The client bakes one 16x16 tile per pattern into
/// a texture atlas, so the game does not depend on an external asset pack.
/// These names intentionally mirror the beta block textures.
/// </summary>
public enum BlockTexture : byte
{
    Air,
    Stone,
    Dirt,
    GrassTop,
    GrassSide,
    Sand,
    Gravel,
    Cobblestone,
    Planks,
    LogSide,
    LogTop,
    Leaves,
    Water,
    Lava,
    Bedrock,
    CoalOre,
    IronOre,
    GoldOre,
    DiamondOre,
    LapisOre,
    GoldBlock,
    IronBlock,
    DiamondBlock,
    LapisBlock,
    Bricks,
    TNT,
    Bookshelf,
    MossStone,
    Obsidian,
    Glass,
    Snow,
    Ice,
    CactusSide,
    CactusTop,
    Clay,
    SandstoneTop,
    SandstoneSide,
    Wool,
    Sponge,
    CraftingTableTop,
    CraftingTableSide,
    FurnaceSide,
    FurnaceFront,
    PumpkinSide,
    PumpkinTop,
    JackOLantern,
    Netherrack,
    SoulSand,
    Glowstone,
    Torch,
    FarmlandTop,
    FarmlandSide,
    Wheat,
    TallGrass,
    Sapling,
    Dandelion,
    Rose,
    BrownMushroom,
    RedMushroom,
    DeadBush,
    SugarCane,
}

/// <summary>
/// Describes a single block type. This record is the single source of truth
/// shared by the client (rendering) and the server (world simulation).
/// </summary>
/// <param name="Id">Stable numeric id. Sent over the network.</param>
/// <param name="Name">Human readable name, also used by console commands.</param>
/// <param name="Color">Representative 0xRRGGBB color for UI swatches.</param>
/// <param name="IsSolid">Whether entities collide with this block.</param>
/// <param name="IsTransparent">Whether blocks behind this one can be seen.</param>
/// <param name="Alpha">Opacity used by the client's transparent pass.</param>
/// <param name="Shape">Selects the client-side block mesh builder.</param>
/// <param name="Texture">Side texture tile.</param>
/// <param name="TopTexture">Top texture tile, or null to reuse <paramref name="Texture"/>.</param>
/// <param name="BottomTexture">Bottom texture tile, or null to reuse <paramref name="Texture"/>.</param>
/// <param name="Tint">Per-vertex tint multiplied over the texture, in 0xRRGGBB.</param>
public sealed record BlockDefinition(
    BlockId Id,
    string Name,
    uint Color,
    bool IsSolid,
    bool IsTransparent,
    float Alpha = 1f,
    BlockShape Shape = BlockShape.Cube,
    BlockTexture Texture = BlockTexture.Stone,
    BlockTexture? TopTexture = null,
    BlockTexture? BottomTexture = null,
    uint Tint = 0xFFFFFF)
{
    public BlockTexture Top => TopTexture ?? Texture;

    public BlockTexture Bottom => BottomTexture ?? Texture;

    /// <summary>
    /// True when the block must be drawn with alpha blending. Plants need this
    /// even at alpha 1 because their textures contain cut-out pixels.
    /// </summary>
    public bool UsesTransparentPass =>
        Alpha < 1f || Shape is BlockShape.Water or BlockShape.Cross or BlockShape.Glass or BlockShape.Torch;
}
