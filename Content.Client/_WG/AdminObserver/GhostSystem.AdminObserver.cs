namespace Content.Client.Ghost;

public sealed partial class GhostSystem
{
    /// <summary>Whether this client is drawing ghosts other than its own.</summary>
    public bool WGGhostsVisible => GhostVisibility;
}
