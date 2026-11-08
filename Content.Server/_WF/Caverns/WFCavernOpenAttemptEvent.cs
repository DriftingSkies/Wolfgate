namespace Content.Server._WF.Caverns;

/// <summary>
/// Raised broadcast before the caverns open a ground onto the cavern over the tiles from <see cref="Min"/> to
/// <see cref="Max"/>, inclusive, for a mouth, a hole, stairs or a shaft. Cancelled where the ground must stay shut, such
/// as over an outpost's foundation.
/// </summary>
[ByRefEvent]
public record struct WFCavernOpenAttemptEvent(EntityUid Ground, Vector2i Min, Vector2i Max)
{
    public bool Cancelled;
}
