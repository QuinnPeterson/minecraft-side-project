namespace Minecraft.Shared.Blocks;

/// <summary>
/// Every block type the game knows about.
/// To add a block, add a member here and a matching entry in <see cref="BlockRegistry"/>.
/// The client and server both read from the registry, so the block shows up in both.
/// Values are stable network identifiers: only append new members.
///
/// The base 0-95 ids follow the Minecraft Beta / TrueCraft block ids so the
/// shared block catalog can be compared with the reference implementation.
/// Simulation-only variants (flowing water, crop growth stages) and dyed wool
/// live in the 200+ range where vanilla had no block ids.
/// </summary>
public enum BlockId : ushort
{
    Air = 0,
    Stone = 1,
    Grass = 2,
    Dirt = 3,
    Cobblestone = 4,
    WoodenPlanks = 5,
    Sapling = 6,
    Bedrock = 7,
    Water = 8,
    Lava = 10,
    Sand = 12,
    Gravel = 13,
    GoldOre = 14,
    IronOre = 15,
    CoalOre = 16,
    Wood = 17,
    Leaves = 18,
    Sponge = 19,
    Glass = 20,
    LapisOre = 21,
    LapisBlock = 22,
    Sandstone = 24,
    TallGrass = 31,
    DeadBush = 32,
    Wool = 35,
    Dandelion = 37,
    Rose = 38,
    BrownMushroom = 39,
    RedMushroom = 40,
    GoldBlock = 41,
    IronBlock = 42,
    Bricks = 45,
    TNT = 46,
    Bookshelf = 47,
    MossStone = 48,
    Obsidian = 49,
    Torch = 50,
    DiamondOre = 56,
    DiamondBlock = 57,
    CraftingTable = 58,
    Farmland = 60,
    Furnace = 61,
    SnowLayer = 78,
    Ice = 79,
    SnowBlock = 80,
    Cactus = 81,
    Clay = 82,
    SugarCane = 83,
    Pumpkin = 86,
    Netherrack = 87,
    SoulSand = 88,
    Glowstone = 89,
    JackOLantern = 91,

    // Flowing-water stages. Water is the infinite source (level 0); higher
    // levels are weaker flow. Keeping levels as block ids avoids adding a
    // metadata field to the wire format.
    WaterFlow1 = 200,
    WaterFlow2 = 201,
    WaterFlow3 = 202,
    WaterFlow4 = 203,
    WaterFlow5 = 204,
    WaterFlow6 = 205,
    WaterFlow7 = 206,

    // Water that is falling but is not an infinite source.
    WaterFalling = 207,

    // Growable crop stages.
    Wheat1 = 210,
    Wheat2 = 211,
    Wheat3 = 212,
    Wheat4 = 213,
    Wheat5 = 214,
    Wheat6 = 215,
    Wheat7 = 216,

    // Dyed wool. White wool reuses the beta id above.
    WoolOrange = 220,
    WoolYellow = 221,
    WoolLime = 222,
    WoolLightBlue = 223,
    WoolBlue = 224,
    WoolPurple = 225,
    WoolMagenta = 226,
    WoolPink = 227,
    WoolBlack = 228,
    WoolGray = 229,
    WoolBrown = 230,
    WoolGreen = 231,
    WoolCyan = 232,
    WoolLightGray = 233,
    WoolRed = 234,
}
