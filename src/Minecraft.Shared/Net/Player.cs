namespace LetsCreateNetworkGame.OpenGL.Library;

/// <summary>A named player, matching the upstream library model.</summary>
public sealed class Player
{
    public string Username { get; set; } = string.Empty;

    public Position Position { get; set; } = new();

    public Player()
    {
    }

    public Player(string username, Position position)
    {
        Username = username;
        Position = position;
    }
}
