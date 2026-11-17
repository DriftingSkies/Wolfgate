namespace Content.Client._WG.ScratchTickets;

/// <summary>Keeps a ticket's half-scratched foil between openings of its window.</summary>
[RegisterComponent]
public sealed partial class WGScratchFoilComponent : Component
{
    /// <summary>The foil's opacity per pixel, row by row.</summary>
    public byte[] Alpha = Array.Empty<byte>();
}
