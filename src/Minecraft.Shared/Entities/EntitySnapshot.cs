namespace Minecraft.Shared.Entities;

public enum EntityType : byte
{
    Item = 0,
    Pig = 1,
    Cow = 2,
    Sheep = 3,
    Ghast = 4,
    Pigman = 5,
    FallingBlock = 6,
}

public readonly record struct EntitySnapshot(int Id, EntityType Type, float X, float Y, float Z, int Data);