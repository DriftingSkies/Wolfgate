namespace Content.Server._WG.NpcCrew.Components;

/// <summary>
/// What a roster crewman wore when he came on post, slot by slot. Stripped (for surgery, by a looter, at death), he
/// goes and gets each piece back from where it was left, once he is up and has nothing more pressing.
/// </summary>
[RegisterComponent]
public sealed partial class WGCrewOutfitComponent : Component
{
    [ViewVariables]
    public Dictionary<string, EntityUid> Worn = new();

    /// <summary>The slot he is fetching for, and when he gives up on it.</summary>
    [ViewVariables]
    public string? Fetching;

    [ViewVariables]
    public TimeSpan Until;

    [ViewVariables]
    public TimeSpan NextLook;

    /// <summary>Pieces he gave up on, and until when he leaves them.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Skipped = new();
}
