using System.Collections.ObjectModel;

namespace Minecraft.Shared.Blocks;

/// <summary>
/// The one place where blocks are registered.
/// Add one <see cref="BlockDefinition"/> below and it becomes available to
/// both the client (palette/rendering) and the server (world and commands).
/// </summary>
public static class BlockRegistry
{
    private static readonly ReadOnlyCollection<BlockDefinition> BlockList = new List<BlockDefinition>
    {
        new(BlockId.Air, "Air", 0x000000, IsSolid: false, IsTransparent: true),
        new(BlockId.Stone, "Stone", 0x7F7F7F, IsSolid: true, IsTransparent: false),
        new(BlockId.Dirt, "Dirt", 0x8B5A2B, IsSolid: true, IsTransparent: false),
        new(BlockId.Grass, "Grass", 0x4C9A2A, IsSolid: true, IsTransparent: false),
        new(BlockId.Sand, "Sand", 0xD9C48A, IsSolid: true, IsTransparent: false),
        new(BlockId.Wood, "Wood", 0x6B4A2B, IsSolid: true, IsTransparent: false),
        new(BlockId.Leaves, "Leaves", 0x2E6B1F, IsSolid: true, IsTransparent: true),
        new(BlockId.Water, "Water", 0x2A6FDB, IsSolid: false, IsTransparent: true),
    }.AsReadOnly();

    private static readonly IReadOnlyDictionary<BlockId, BlockDefinition> BlocksById =
        BlockList.ToDictionary(block => block.Id);

    private static readonly IReadOnlyDictionary<string, BlockDefinition> BlocksByName =
        BlockList.ToDictionary(block => block.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every registered block, including air.</summary>
    public static IReadOnlyList<BlockDefinition> All => BlockList;

    /// <summary>Blocks the player can actually place (everything except air).</summary>
    public static IReadOnlyList<BlockDefinition> Placeable { get; } =
        BlockList.Where(block => block.Id != BlockId.Air).ToArray();

    public static BlockDefinition Get(BlockId id) =>
        BlocksById.TryGetValue(id, out var block)
            ? block
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Block is not registered.");

    public static bool TryGet(BlockId id, out BlockDefinition block) =>
        BlocksById.TryGetValue(id, out block!);

    public static bool TryGetByName(string name, out BlockDefinition block) =>
        BlocksByName.TryGetValue(name, out block!);
}
