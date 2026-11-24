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

        new(BlockId.Stone, "Stone", 0x7F7F7F, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Stone),
        new(BlockId.Grass, "Grass", 0x5A9E3B, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.GrassSide, TopTexture: BlockTexture.GrassTop, BottomTexture: BlockTexture.Dirt),
        new(BlockId.Dirt, "Dirt", 0x8B5A2B, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Dirt),
        new(BlockId.Cobblestone, "Cobblestone", 0x6E6E6E, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Cobblestone),
        new(BlockId.WoodenPlanks, "Wooden Planks", 0xB08A52, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Planks),
        new(BlockId.Sapling, "Sapling", 0x4C9A2A, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.Sapling),
        new(BlockId.Bedrock, "Bedrock", 0x333333, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Bedrock),
        new(BlockId.Water, "Water", 0x3F76E4, IsSolid: false, IsTransparent: true,
            Alpha: 0.72f, Shape: BlockShape.Water, Texture: BlockTexture.Water),
        new(BlockId.Lava, "Lava", 0xE27019, IsSolid: false, IsTransparent: false,
            Shape: BlockShape.Cube, Texture: BlockTexture.Lava),
        new(BlockId.Sand, "Sand", 0xD9C48A, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Sand),
        new(BlockId.Gravel, "Gravel", 0x8A8580, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Gravel),
        new(BlockId.GoldOre, "Gold Ore", 0x9C8B52, IsSolid: true, IsTransparent: false, Texture: BlockTexture.GoldOre),
        new(BlockId.IronOre, "Iron Ore", 0x9A8F83, IsSolid: true, IsTransparent: false, Texture: BlockTexture.IronOre),
        new(BlockId.CoalOre, "Coal Ore", 0x6B6B6B, IsSolid: true, IsTransparent: false, Texture: BlockTexture.CoalOre),
        new(BlockId.Wood, "Wood", 0x6B4A2B, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.LogSide, TopTexture: BlockTexture.LogTop, BottomTexture: BlockTexture.LogTop),
        new(BlockId.Leaves, "Leaves", 0x2E6B1F, IsSolid: true, IsTransparent: true, Texture: BlockTexture.Leaves),
        new(BlockId.Sponge, "Sponge", 0xC7C24B, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Sponge),
        new(BlockId.Glass, "Glass", 0xCFE8F0, IsSolid: true, IsTransparent: true,
            Shape: BlockShape.Glass, Texture: BlockTexture.Glass),
        new(BlockId.LapisOre, "Lapis Lazuli Ore", 0x4A5A9C, IsSolid: true, IsTransparent: false, Texture: BlockTexture.LapisOre),
        new(BlockId.LapisBlock, "Lapis Lazuli Block", 0x2E4B9C, IsSolid: true, IsTransparent: false, Texture: BlockTexture.LapisBlock),
        new(BlockId.Sandstone, "Sandstone", 0xD6C68D, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.SandstoneSide, TopTexture: BlockTexture.SandstoneTop, BottomTexture: BlockTexture.SandstoneTop),
        new(BlockId.TallGrass, "Tall Grass", 0x5FA53D, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.TallGrass),
        new(BlockId.DeadBush, "Dead Bush", 0x8A6A32, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.DeadBush),
        new(BlockId.Wool, "White Wool", 0xE9ECEC, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0xE9ECEC),
        new(BlockId.Dandelion, "Dandelion", 0xF2D63A, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.Dandelion),
        new(BlockId.Rose, "Rose", 0xD33A3A, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.Rose),
        new(BlockId.BrownMushroom, "Brown Mushroom", 0x9A7A55, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.BrownMushroom),
        new(BlockId.RedMushroom, "Red Mushroom", 0xC43A32, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.RedMushroom),
        new(BlockId.GoldBlock, "Gold Block", 0xF2D545, IsSolid: true, IsTransparent: false, Texture: BlockTexture.GoldBlock),
        new(BlockId.IronBlock, "Iron Block", 0xD8D8D8, IsSolid: true, IsTransparent: false, Texture: BlockTexture.IronBlock),
        new(BlockId.Bricks, "Bricks", 0x9A4B3E, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Bricks),
        new(BlockId.TNT, "TNT", 0xC33A32, IsSolid: true, IsTransparent: false, Texture: BlockTexture.TNT),
        new(BlockId.Bookshelf, "Bookshelf", 0x9A7A4A, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Bookshelf, TopTexture: BlockTexture.Planks, BottomTexture: BlockTexture.Planks),
        new(BlockId.MossStone, "Moss Stone", 0x6E7E52, IsSolid: true, IsTransparent: false, Texture: BlockTexture.MossStone),
        new(BlockId.Obsidian, "Obsidian", 0x241B3A, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Obsidian),
        new(BlockId.Torch, "Torch", 0xF2C14B, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Torch, Texture: BlockTexture.Torch),
        new(BlockId.DiamondOre, "Diamond Ore", 0x5FD8D8, IsSolid: true, IsTransparent: false, Texture: BlockTexture.DiamondOre),
        new(BlockId.DiamondBlock, "Diamond Block", 0x62E5E5, IsSolid: true, IsTransparent: false, Texture: BlockTexture.DiamondBlock),
        new(BlockId.CraftingTable, "Crafting Table", 0xA8895B, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.CraftingTableSide, TopTexture: BlockTexture.CraftingTableTop, BottomTexture: BlockTexture.Planks),
        new(BlockId.Farmland, "Farmland", 0x6B4A2B, IsSolid: true, IsTransparent: false,
            Shape: BlockShape.Farmland, Texture: BlockTexture.FarmlandSide,
            TopTexture: BlockTexture.FarmlandTop, BottomTexture: BlockTexture.Dirt),
        new(BlockId.Furnace, "Furnace", 0x777777, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.FurnaceFront, TopTexture: BlockTexture.FurnaceSide, BottomTexture: BlockTexture.FurnaceSide),
        new(BlockId.SnowLayer, "Snow", 0xF0F5F5, IsSolid: false, IsTransparent: false,
            Shape: BlockShape.SnowLayer, Texture: BlockTexture.Snow),
        new(BlockId.Ice, "Ice", 0x9CC6EA, IsSolid: true, IsTransparent: true,
            Alpha: 0.78f, Shape: BlockShape.Cube, Texture: BlockTexture.Ice),
        new(BlockId.SnowBlock, "Snow Block", 0xF0F5F5, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Snow),
        new(BlockId.Cactus, "Cactus", 0x3F7A34, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.CactusSide, TopTexture: BlockTexture.CactusTop, BottomTexture: BlockTexture.CactusTop),
        new(BlockId.Clay, "Clay", 0x9FA6B1, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Clay),
        new(BlockId.SugarCane, "Sugar Cane", 0x7EBF5A, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.SugarCane),
        new(BlockId.Pumpkin, "Pumpkin", 0xD88A26, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.PumpkinSide, TopTexture: BlockTexture.PumpkinTop, BottomTexture: BlockTexture.PumpkinTop),
        new(BlockId.Netherrack, "Netherrack", 0x7A2E2E, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Netherrack),
        new(BlockId.SoulSand, "Soul Sand", 0x7A6350, IsSolid: true, IsTransparent: false, Texture: BlockTexture.SoulSand),
        new(BlockId.Glowstone, "Glowstone", 0xE0C15A, IsSolid: true, IsTransparent: false, Texture: BlockTexture.Glowstone),
        new(BlockId.JackOLantern, "Jack o'Lantern", 0xE08A26, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.JackOLantern, TopTexture: BlockTexture.PumpkinTop, BottomTexture: BlockTexture.PumpkinTop),

        // Flowing water stages.
        new(BlockId.WaterFlow1, "Flowing Water 1", 0x3F76E4, IsSolid: false, IsTransparent: true,
            Alpha: 0.68f, Shape: BlockShape.Water, Texture: BlockTexture.Water),
        new(BlockId.WaterFlow2, "Flowing Water 2", 0x3F76E4, IsSolid: false, IsTransparent: true,
            Alpha: 0.66f, Shape: BlockShape.Water, Texture: BlockTexture.Water),
        new(BlockId.WaterFlow3, "Flowing Water 3", 0x3F76E4, IsSolid: false, IsTransparent: true,
            Alpha: 0.64f, Shape: BlockShape.Water, Texture: BlockTexture.Water),
        new(BlockId.WaterFlow4, "Flowing Water 4", 0x3F76E4, IsSolid: false, IsTransparent: true,
            Alpha: 0.62f, Shape: BlockShape.Water, Texture: BlockTexture.Water),
        new(BlockId.WaterFlow5, "Flowing Water 5", 0x3F76E4, IsSolid: false, IsTransparent: true,
            Alpha: 0.60f, Shape: BlockShape.Water, Texture: BlockTexture.Water),
        new(BlockId.WaterFlow6, "Flowing Water 6", 0x3F76E4, IsSolid: false, IsTransparent: true,
            Alpha: 0.58f, Shape: BlockShape.Water, Texture: BlockTexture.Water),
        new(BlockId.WaterFlow7, "Flowing Water 7", 0x3F76E4, IsSolid: false, IsTransparent: true,
            Alpha: 0.56f, Shape: BlockShape.Water, Texture: BlockTexture.Water),
        new(BlockId.WaterFalling, "Falling Water", 0x3F76E4, IsSolid: false, IsTransparent: true,
            Alpha: 0.72f, Shape: BlockShape.Water, Texture: BlockTexture.Water),

        // Flowing-lava stages, mirroring the water flow stages (source lava is
        // BlockId.Lava itself; higher stages are weaker flow).
        new(BlockId.LavaFlow1, "Flowing Lava 1", 0xE27019, IsSolid: false, IsTransparent: false,
            Alpha: 1f, Shape: BlockShape.Cube, Texture: BlockTexture.Lava),
        new(BlockId.LavaFlow2, "Flowing Lava 2", 0xE27019, IsSolid: false, IsTransparent: false,
            Alpha: 1f, Shape: BlockShape.Cube, Texture: BlockTexture.Lava),
        new(BlockId.LavaFlow3, "Flowing Lava 3", 0xE27019, IsSolid: false, IsTransparent: false,
            Alpha: 1f, Shape: BlockShape.Cube, Texture: BlockTexture.Lava),
        new(BlockId.LavaFlow4, "Flowing Lava 4", 0xE27019, IsSolid: false, IsTransparent: false,
            Alpha: 1f, Shape: BlockShape.Cube, Texture: BlockTexture.Lava),
        new(BlockId.LavaFlow5, "Flowing Lava 5", 0xE27019, IsSolid: false, IsTransparent: false,
            Alpha: 1f, Shape: BlockShape.Cube, Texture: BlockTexture.Lava),
        new(BlockId.LavaFlow6, "Flowing Lava 6", 0xE27019, IsSolid: false, IsTransparent: false,
            Alpha: 1f, Shape: BlockShape.Cube, Texture: BlockTexture.Lava),
        new(BlockId.LavaFlow7, "Flowing Lava 7", 0xE27019, IsSolid: false, IsTransparent: false,
            Alpha: 1f, Shape: BlockShape.Cube, Texture: BlockTexture.Lava),

        // Crop growth stages share one texture and change tint as they ripen.
        new(BlockId.Wheat1, "Wheat", 0x6FBF3F, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.Wheat, Tint: 0x7ACB45),
        new(BlockId.Wheat2, "Wheat 2", 0x78C348, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.Wheat, Tint: 0x88C94C),
        new(BlockId.Wheat3, "Wheat 3", 0x81C750, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.Wheat, Tint: 0x98C750),
        new(BlockId.Wheat4, "Wheat 4", 0x8ACB58, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.Wheat, Tint: 0xA8C553),
        new(BlockId.Wheat5, "Wheat 5", 0x93CF60, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.Wheat, Tint: 0xBAC259),
        new(BlockId.Wheat6, "Wheat 6", 0x9CD368, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.Wheat, Tint: 0xCBBF5E),
        new(BlockId.Wheat7, "Wheat 7", 0xA5D770, IsSolid: false, IsTransparent: true,
            Shape: BlockShape.Cross, Texture: BlockTexture.Wheat, Tint: 0xDCC060),

        // Dyed wool uses the white wool texture with a per-vertex tint.
        new(BlockId.WoolOrange, "Orange Wool", 0xD87F33, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0xD87F33),
        new(BlockId.WoolYellow, "Yellow Wool", 0xE5E533, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0xE5E533),
        new(BlockId.WoolLime, "Lime Wool", 0x7FCC19, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x7FCC19),
        new(BlockId.WoolLightBlue, "Light Blue Wool", 0x6699D8, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x6699D8),
        new(BlockId.WoolBlue, "Blue Wool", 0x334CB2, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x334CB2),
        new(BlockId.WoolPurple, "Purple Wool", 0x7F3FB2, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x7F3FB2),
        new(BlockId.WoolMagenta, "Magenta Wool", 0xC354CD, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0xC354CD),
        new(BlockId.WoolPink, "Pink Wool", 0xEB8EC7, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0xEB8EC7),
        new(BlockId.WoolBlack, "Black Wool", 0x1E1E1E, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x1E1E1E),
        new(BlockId.WoolGray, "Gray Wool", 0x4C4C4C, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x4C4C4C),
        new(BlockId.WoolBrown, "Brown Wool", 0x664C33, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x664C33),
        new(BlockId.WoolGreen, "Green Wool", 0x4C7F32, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x4C7F32),
        new(BlockId.WoolCyan, "Cyan Wool", 0x4C9999, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x4C9999),
        new(BlockId.WoolLightGray, "Light Gray Wool", 0x999999, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x999999),
        new(BlockId.WoolRed, "Red Wool", 0x993333, IsSolid: true, IsTransparent: false,
            Texture: BlockTexture.Wool, Tint: 0x993333),
    }.AsReadOnly();

    private static readonly IReadOnlyDictionary<BlockId, BlockDefinition> BlocksById =
        BlockList.ToDictionary(block => block.Id);

    private static readonly IReadOnlyDictionary<string, BlockDefinition> BlocksByName =
        BlockList.ToDictionary(block => block.Name, StringComparer.OrdinalIgnoreCase);

    // Light queries are hot paths, so they use flat tables instead of
    // dictionary lookups. The tables are indexed by (int)BlockId.
    private static readonly bool[] OpaqueLookup = BuildLookup(
        block => block.IsSolid && !block.IsTransparent);

    private static readonly byte[] EmissionLookup = BuildEmissionLookup();

    private static bool[] BuildLookup(Func<BlockDefinition, bool> predicate)
    {
        var table = new bool[HighestBlockId()];

        foreach (var block in BlockList)
        {
            table[(int)block.Id] = predicate(block);
        }

        return table;
    }

    private static byte[] BuildEmissionLookup()
    {
        var table = new byte[HighestBlockId()];
        table[(int)BlockId.Torch] = 14;
        table[(int)BlockId.Glowstone] = 15;
        table[(int)BlockId.JackOLantern] = 15;
        table[(int)BlockId.Lava] = 15;
        table[(int)BlockId.LavaFlow1] = 15;
        table[(int)BlockId.LavaFlow2] = 15;
        table[(int)BlockId.LavaFlow3] = 15;
        table[(int)BlockId.LavaFlow4] = 15;
        table[(int)BlockId.LavaFlow5] = 15;
        table[(int)BlockId.LavaFlow6] = 15;
        table[(int)BlockId.LavaFlow7] = 15;
        return table;
    }

    private static int HighestBlockId()
    {
        var highest = 0;

        foreach (var block in BlockList)
        {
            highest = Math.Max(highest, (int)block.Id);
        }

        return highest + 1;
    }

    /// <summary>Every registered block, including air.</summary>
    public static IReadOnlyList<BlockDefinition> All => BlockList;

    /// <summary>
    /// Blocks shown in the creative inventory. Simulation-only water levels and
    /// intermediate crop stages are hidden, but every buildable block is here.
    /// </summary>
    public static IReadOnlyList<BlockDefinition> CreativePalette { get; } = BlockList
        .Where(block =>
            block.Id != BlockId.Air &&
            !IsWaterFlow(block.Id) &&
            !IsLavaFlow(block.Id) &&
            block.Id != BlockId.Water &&
            block.Id != BlockId.WaterFalling &&
            block.Id != BlockId.Wheat2 &&
            block.Id != BlockId.Wheat3 &&
            block.Id != BlockId.Wheat4 &&
            block.Id != BlockId.Wheat5 &&
            block.Id != BlockId.Wheat6 &&
            block.Id != BlockId.Wheat7)
        .ToArray();

    /// <summary>Backwards-compatible alias used by the old hotbar code.</summary>
    public static IReadOnlyList<BlockDefinition> Placeable => CreativePalette;

    public static BlockDefinition Get(BlockId id) =>
        BlocksById.TryGetValue(id, out var block)
            ? block
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Block is not registered.");

    public static bool TryGet(BlockId id, out BlockDefinition block) =>
        BlocksById.TryGetValue(id, out block!);

    public static bool TryGetByName(string name, out BlockDefinition block) =>
        BlocksByName.TryGetValue(name, out block!);

    public static bool IsWater(BlockId id) =>
        id == BlockId.Water ||
        id == BlockId.WaterFalling ||
        IsWaterFlow(id);

    public static bool IsWaterFlow(BlockId id) =>
        id >= BlockId.WaterFlow1 && id <= BlockId.WaterFlow7;

    public static bool IsLava(BlockId id) =>
        id == BlockId.Lava || IsLavaFlow(id);

    public static bool IsLavaFlow(BlockId id) =>
        id >= BlockId.LavaFlow1 && id <= BlockId.LavaFlow7;

    /// <summary>
    /// How much sky light a block filters while still letting it through.
    /// Leaves cast a soft shadow instead of letting full daylight hit the ground.
    /// </summary>
    public static byte SkyAttenuation(BlockId id) =>
        id == BlockId.Leaves ? (byte)3 : (byte)0;

    public static bool IsFluid(BlockId id) => IsWater(id) || IsLava(id);

    public static bool IsCrop(BlockId id) => id >= BlockId.Wheat1 && id <= BlockId.Wheat7;

    public static bool IsPlant(BlockId id) =>
        id == BlockId.Sapling ||
        id == BlockId.TallGrass ||
        id == BlockId.DeadBush ||
        id == BlockId.Dandelion ||
        id == BlockId.Rose ||
        id == BlockId.BrownMushroom ||
        id == BlockId.RedMushroom ||
        id == BlockId.SugarCane ||
        IsCrop(id);

    /// <summary>True when a placed block may overwrite this cell.</summary>
    public static bool IsReplaceable(BlockId id) =>
        id == BlockId.Air || IsWater(id) || IsPlant(id);

    /// <summary>True when the selection ray may stop on this block.</summary>
    public static bool IsTargetable(BlockId id) =>
        id != BlockId.Air && !IsWater(id) && !IsLava(id);

    public static bool IsOpaque(BlockId id)
    {
        var index = (int)id;
        return (uint)index < (uint)OpaqueLookup.Length && OpaqueLookup[index];
    }

    public static float Hardness(BlockId id) => id switch
    {
        BlockId.Bedrock => float.PositiveInfinity,
        BlockId.Obsidian => 8f,
        BlockId.Stone or BlockId.Cobblestone or BlockId.MossStone => 1.5f,
        BlockId.Wood or BlockId.WoodenPlanks or BlockId.Bricks => 2f,
        BlockId.Dirt or BlockId.Grass or BlockId.Sand or BlockId.Gravel or BlockId.Clay => 0.5f,
        BlockId.Glass => 0.3f,
        BlockId.Leaves or BlockId.TallGrass or BlockId.DeadBush => 0.2f,
        BlockId.Sandstone => 0.8f,
        _ => 1f,
    };
    public static byte LightEmission(BlockId id)
    {
        var index = (int)id;
        return (uint)index < (uint)EmissionLookup.Length ? EmissionLookup[index] : (byte)0;
    }

    public static int WaterLevel(BlockId id)
    {
        if (id == BlockId.Water || id == BlockId.WaterFalling)
        {
            return 0;
        }

        return IsWaterFlow(id) ? (int)id - (int)BlockId.WaterFlow1 + 1 : -1;
    }

    public static BlockId WaterBlockForLevel(int level)
    {
        if (level <= 0)
        {
            return BlockId.Water;
        }

        var clamped = Math.Min(level, 7);
        return (BlockId)((int)BlockId.WaterFlow1 + clamped - 1);
    }

    /// <summary>Resolves a flowing-lava level to its block id (source is Lava).</summary>
    public static BlockId LavaBlockForLevel(int level)
    {
        if (level <= 0)
        {
            return BlockId.Lava;
        }

        var clamped = Math.Min(level, 7);
        return (BlockId)((int)BlockId.LavaFlow1 + clamped - 1);
    }

    /// <summary>
    /// Shared fluid level: water sources/falling are 0, flowing stages are
    /// 1..7, and non-fluids report -1.
    /// </summary>
    public static int FluidLevel(BlockId id)
    {
        if (id == BlockId.Water || id == BlockId.WaterFalling)
        {
            return 0;
        }

        if (IsWaterFlow(id))
        {
            return (int)id - (int)BlockId.WaterFlow1 + 1;
        }

        if (id == BlockId.Lava)
        {
            return 0;
        }

        if (IsLavaFlow(id))
        {
            return (int)id - (int)BlockId.LavaFlow1 + 1;
        }

        return -1;
    }

    /// <summary>Advances a crop one stage; mature crops stay put.</summary>
    public static BlockId NextPlantStage(BlockId id)
    {
        if (id >= BlockId.Wheat7)
        {
            return id;
        }

        return IsCrop(id) ? (BlockId)((int)id + 1) : id;
    }
}
