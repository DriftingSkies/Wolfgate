namespace Content.Server._WF.Outposts.Components;

/// <summary>
/// On a cavern map: the tiles under outposts' claims, where its biome lays foundation rock in place of its own. Kept by
/// <see cref="WFOutpostSystem"/>.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFOutpostFoundationComponent : Component
{
    /// <summary>Each claim's tiles, with exclusive upper edges.</summary>
    [ViewVariables]
    public List<Box2i> Zones = new();

    /// <summary>Whether a claim covers a tile.</summary>
    public bool Covers(Vector2i tile)
    {
        foreach (var zone in Zones)
        {
            if (tile.X >= zone.Left && tile.X < zone.Right && tile.Y >= zone.Bottom && tile.Y < zone.Top)
                return true;
        }

        return false;
    }
}
