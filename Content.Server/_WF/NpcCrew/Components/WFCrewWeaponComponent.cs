namespace Content.Server._WF.NpcCrew.Components;

/// <summary>The weapon a crewman has drawn and the equipment slot it goes back into, and what he knows of the fight.</summary>
[RegisterComponent]
public sealed partial class WFCrewWeaponComponent : Component
{
    [ViewVariables]
    public EntityUid? Drawn;

    [ViewVariables]
    public string? HolsterSlot;

    /// <summary>When the reload/holster sweep next looks at this crewman; jittered so the crew don't share a tick.</summary>
    [ViewVariables]
    public TimeSpan NextCheck;

    /// <summary>When he last had each hostile in sight.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Sighted = new();

    /// <summary>When he first turned his attention to each hostile, seen or not.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Noticed = new();

    /// <summary>The hostile whose approach he covers, while <see cref="BracedUntil"/> has not passed.</summary>
    [ViewVariables]
    public EntityUid? Braced;

    /// <summary>When he stops counting as braced unless the brace is renewed.</summary>
    [ViewVariables]
    public TimeSpan BracedUntil;

    /// <summary>When he started covering <see cref="Braced"/> without seeing it.</summary>
    [ViewVariables]
    public TimeSpan BraceSince;

    /// <summary>When the brace next checks whether it still holds.</summary>
    [ViewVariables]
    public TimeSpan NextBraceCheck;

    /// <summary>When he may next break contact to patch himself up.</summary>
    [ViewVariables]
    public TimeSpan NextFallBack;

    /// <summary>When he stops patching himself up behind cover.</summary>
    [ViewVariables]
    public TimeSpan PatchUntil;

    /// <summary>When he next treats himself behind cover.</summary>
    [ViewVariables]
    public TimeSpan NextPatch;
}
