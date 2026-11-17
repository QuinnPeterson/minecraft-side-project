namespace Minecraft.Shared.Blocks;

/// <summary>
/// Every block type the game knows about.
/// To add a block, add a member here and a matching entry in <see cref="BlockRegistry"/>.
/// The client and server both read from the registry, so the block shows up in both.
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
}
