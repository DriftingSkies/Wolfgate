using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Augments;

/// <summary>
/// Optical camouflage under the skin: an action that turns the body invisible for a while, until it moves.
/// Attacking, shooting, getting hurt by someone or an EMP drops it early, and it needs time to cool down after.
/// </summary>
[RegisterComponent]
public sealed partial class WGOpticalCamoComponent : Component
{
    [DataField]
    public EntProtoId Action = "WGActionOpticalCamo";

    [ViewVariables]
    public EntityUid? ActionEntity;

    /// <summary>How long the camouflage holds if nothing breaks it.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);

    /// <summary>How long it takes to come back once it drops.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    /// <summary>How long it stays down after an EMP.</summary>
    [DataField]
    public TimeSpan EmpCooldown = TimeSpan.FromSeconds(120);

    /// <summary>The stealth put on the body while the camouflage is on: how hidden it is and what moving costs.</summary>
    [DataField(required: true)]
    public ComponentRegistry Components = new();

    [DataField]
    public SoundSpecifier? OnSound = new SoundPathSpecifier("/Audio/Effects/stealthoff.ogg");

    [DataField]
    public SoundSpecifier? OffSound = new SoundPathSpecifier("/Audio/Effects/stealthoff.ogg");

    /// <summary>When the camouflage runs out, while it is on.</summary>
    [ViewVariables]
    public TimeSpan? EndsAt;
}

public sealed partial class WGToggleOpticalCamoEvent : InstantActionEvent;
