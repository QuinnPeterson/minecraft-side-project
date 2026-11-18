namespace LetsCreateNetworkGame.OpenGL.Library;

/// <summary>
/// Upstream-style player position, extended with the Z axis and view angles so
/// it can represent a player in the 3D world.
/// </summary>
public sealed class Position
{
    public float XPosition { get; set; }
    public float YPosition { get; set; }
    public float ZPosition { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }

    public Position()
    {
    }

    public Position(float x, float y, float z, float yaw = 0f, float pitch = 0f)
    {
        XPosition = x;
        YPosition = y;
        ZPosition = z;
        Yaw = yaw;
        Pitch = pitch;
    }
}
