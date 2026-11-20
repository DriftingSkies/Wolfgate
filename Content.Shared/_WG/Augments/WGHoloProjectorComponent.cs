using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Augments;

/// <summary>
/// A holo projector in the chest: an action that casts copies of its body around the room it stands in and swaps the
/// body into the place of one of them.
/// </summary>
[RegisterComponent]
public sealed partial class WGHoloProjectorComponent : Component
{
    [DataField]
    public EntProtoId Action = "WGActionHoloProject";

    [ViewVariables]
    public EntityUid? ActionEntity;

    [DataField]
    public EntProtoId Decoy = "WGHoloDecoy";

    /// <summary>Spawned at the body and at each copy as they are cast.</summary>
    [DataField]
    public EntProtoId Flicker = "WGHoloDecoyPuff";

    [DataField]
    public SoundSpecifier CastSound = new SoundPathSpecifier("/Audio/Effects/stealthoff.ogg");

    /// <summary>How many copies are cast. The first stands where the body stood.</summary>
    [DataField]
    public int Copies = 2;

    /// <summary>How many tiles from the body, walking through the room, the copies and the body can land.</summary>
    [DataField]
    public int Range = 5;
}

/// <summary>A holographic copy of <see cref="Source"/>. It wanders, vanishes when struck, and wears out on its own.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGHoloDecoyComponent : Component
{
    /// <summary>The body whose look the copy takes.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Source;

    [DataField]
    public EntProtoId Puff = "WGHoloDecoyPuff";

    /// <summary>Client side: whether the source's sprite has been copied yet.</summary>
    [ViewVariables]
    public bool Copied;
}

public sealed partial class WGHoloProjectEvent : InstantActionEvent;
