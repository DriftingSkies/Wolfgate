using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Augments;

/// <summary>
/// Eyes that cycle their vision through a set of bands from one action: off, each band in turn, then off. A band is
/// either thermal, on the body's <see cref="Content.Shared._White.Overlays.ThermalVisionComponent"/>, or night vision,
/// on its <see cref="Content.Shared.Overlays.NightVisionComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WGThermalTintsComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public List<WGThermalTint> Tints = new();

    /// <summary>The tint in use, or -1 while the vision is off.</summary>
    [DataField, AutoNetworkedField]
    public int Index = -1;

    [DataField]
    public EntProtoId Action = "WGActionCycleThermalTint";

    [ViewVariables]
    public EntityUid? ActionEntity;

    /// <summary>Played on every switch to a tint.</summary>
    [DataField]
    public SoundSpecifier? SwapSound = new SoundPathSpecifier("/Audio/_White/Items/Goggles/activate.ogg");

    /// <summary>Played over <see cref="SwapSound"/>: the lens settling on the new band.</summary>
    [DataField]
    public SoundSpecifier? ChirpSound = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg",
        AudioParams.Default.WithPitchScale(1.35f).WithVolume(-4f));

    [DataField]
    public SoundSpecifier? OffSound = new SoundPathSpecifier("/Audio/_White/Items/Goggles/deactivate.ogg");
}

/// <summary>One band the eyes can see in.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class WGThermalTint
{
    /// <summary>Night vision instead of thermal.</summary>
    [DataField]
    public bool NightVision;

    /// <summary>The colour warm bodies are drawn in, or the night vision's phosphor.</summary>
    [DataField(required: true)]
    public Color Color;

    /// <summary>The colour the rest of a thermal view is washed towards.</summary>
    [DataField]
    public Vector3 Tint;
}

public sealed partial class WGCycleThermalTintEvent : InstantActionEvent;

/// <summary>Raised on the body on the client, once, when its eyes switch band; drives the swap glitch.</summary>
[ByRefEvent]
public readonly record struct WGThermalTintSwappedEvent(Color Color);
