namespace Minecraft.Shared.Worlds;

/// <summary>
/// Shared day/night curve. Keeping this in one place makes the client's sky
/// colours and the lighting engine's sky light agree at every hour.
/// </summary>
public static class Daylight
{
    /// <summary>Sun elevation on a -1..1 scale (6:00 sunrise, 18:00 sunset).</summary>
    public static float SunElevation(float hours) =>
        (float)Math.Sin((hours - 6f) / 24f * Math.PI * 2f);

    /// <summary>
    /// How bright the sky is on a 0..1 scale. The curve reaches full daylight
    /// a couple of hours after sunrise and drops to night shortly after sunset.
    /// </summary>
    public static float Level(float hours) =>
        Math.Clamp((SunElevation(hours) + 0.05f) * 2.2f, 0f, 1f);

    /// <summary>
    /// Strength of the warm dawn/dusk glow (0..1), peaking when the sun is at
    /// the horizon and fading out as it climbs or dips below it.
    /// </summary>
    public static float SunsetStrength(float hours) =>
        Math.Clamp(1f - Math.Abs(SunElevation(hours)) / 0.45f, 0f, 1f);
}
