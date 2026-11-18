namespace Minecraft.Shared.Blocks;

/// <summary>
/// Every block type the game knows about.
/// To add a block, add a member here and a matching entry in <see cref="BlockRegistry"/>.
/// The client and server both read from the registry, so the block shows up in both.
/// Values are stable network/save identifiers: only append new members.
/// </summary>
public enum BlockId : ushort
{
    Air = 0,
    Stone = 1,
    Dirt = 2,
    Grass = 3,
    Sand = 4,
    Wood = 5,
    Leaves = 6,
    Water = 7,

    // Flowing-water stages. Water is the infinite source (level 0); higher
    // levels are weaker flow. Keeping levels as block ids avoids adding a
    // metadata field to the wire format.
    WaterFlow1 = 8,
    WaterFlow2 = 9,
    WaterFlow3 = 10,
    WaterFlow4 = 11,
    WaterFlow5 = 12,
    WaterFlow6 = 13,
    WaterFlow7 = 14,

    // Growable plant stages.
    Wheat1 = 15,
    Wheat2 = 16,
    Wheat3 = 17,
    Wheat4 = 18,
    Wheat5 = 19,
    Wheat6 = 20,
    Wheat7 = 21,

    // Water that is falling but is not an infinite source. It behaves like a
    // level-0 flow for rendering and spreading, but can decay when unsupported.
    WaterFalling = 22,
}
