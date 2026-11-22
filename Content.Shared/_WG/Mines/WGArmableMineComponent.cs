using Content.Shared.DoAfter;
using Content.Shared.Tools;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.Mines;

/// <summary>A mine that is a safe item until set on a floor tile, arms after a delay, and is disarmed with a tool.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(WGArmableMineSystem))]
public sealed partial class WGArmableMineComponent : Component
{
    [DataField, AutoNetworkedField]
    public WGMineState State = WGMineState.Safe;

    /// <summary>When an arming mine goes live.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan ArmAt;

    [DataField]
    public TimeSpan SetDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan ArmDelay = TimeSpan.FromSeconds(3);

    /// <summary>Base disarm time, before the tool's speed.</summary>
    [DataField]
    public float DisarmDelay = 5f;

    [DataField]
    public ProtoId<ToolQualityPrototype> DisarmQuality = "Pulsing";

    [DataField]
    public SoundSpecifier? ArmSound;

    /// <summary>Played when someone steps on the armed mine; it goes off when they step off.</summary>
    [DataField]
    public SoundSpecifier? StepSound;
}

[Serializable, NetSerializable]
public enum WGMineState : byte
{
    Safe,
    Arming,
    Armed,
}

[Serializable, NetSerializable]
public sealed partial class WGMineSetDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public NetCoordinates Coordinates;

    private WGMineSetDoAfterEvent()
    {
    }

    public WGMineSetDoAfterEvent(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }

    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed partial class WGMineDisarmDoAfterEvent : SimpleDoAfterEvent;
