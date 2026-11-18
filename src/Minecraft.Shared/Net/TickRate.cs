namespace Minecraft.Shared.Net;

/// <summary>Fixed network tick rate used by both the client and the server.</summary>
public static class TickRate
{
    public const int TicksPerSecond = 60;

    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1000.0 / TicksPerSecond);
}
