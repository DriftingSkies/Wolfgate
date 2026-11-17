using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Tether.Harpoon;

/// <summary>
/// The harpoon a <see cref="ShipHarpoonTurretComponent"/> fires. It only sinks in when it is shot well: fast
/// enough, and close enough to square on to the surface it hits. A glancing hit drops it as a loose item.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipHarpoonComponent : Component
{
    /// <summary>Slowest impact that can still drive the barbs in, in metres per second.</summary>
    [DataField]
    public float MinEmbedSpeed = 12f;

    /// <summary>Widest angle between the flight path and the surface normal that still counts as square on.</summary>
    [DataField]
    public Angle MaxIncidence = Angle.FromDegrees(50);

    /// <summary>Played when it skips off a surface instead of biting.</summary>
    [DataField]
    public SoundSpecifier GlanceSound = new SoundPathSpecifier("/Audio/Effects/clang.ogg");

    /// <summary>How long prying an embedded harpoon out takes.</summary>
    [DataField]
    public TimeSpan PryTime = TimeSpan.FromSeconds(4);

    /// <summary>A shot that misses is gone instead of lying loose: it is destroyed on a glance or at the end of its cable.</summary>
    [DataField]
    public bool Expendable;

    /// <summary>How long an expendable harpoon stays in the air with nothing to hit.</summary>
    [DataField]
    public TimeSpan FlightTime = TimeSpan.FromSeconds(8);

    /// <summary>The turret that fired it, while it is still tied to one.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Turret;

    /// <summary>Set once the harpoon has bitten into something.</summary>
    [DataField, AutoNetworkedField]
    public bool Embedded;
}

/// <summary>Appearance key set while a harpoon is sunk into something.</summary>
[Serializable, NetSerializable]
public enum ShipHarpoonVisuals : byte
{
    Embedded,
}

/// <summary>Sprite layers of a harpoon.</summary>
[Serializable, NetSerializable]
public enum ShipHarpoonVisualLayers : byte
{
    Base,
}
