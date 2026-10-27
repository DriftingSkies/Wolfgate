namespace Content.Server._WF.NpcPry;

/// <summary>Tracks an NPC's recent pry attempts on one door so it gives up instead of retrying every tick.</summary>
[RegisterComponent]
public sealed partial class WFNpcPryMemoryComponent : Component
{
    [ViewVariables]
    public EntityUid? Door;

    [ViewVariables]
    public int Attempts;

    [ViewVariables]
    public TimeSpan LastAttempt;

    /// <summary>The NPC won't pry <see cref="Door"/> again before this time.</summary>
    [ViewVariables]
    public TimeSpan GiveUpUntil;
}
