using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipModules;

/// <summary>Advanced gyros: while fitted, every rotating ship gun on the hull turns <see cref="TurnFactor"/> times as fast.</summary>
public sealed partial class WGGunGyrosEffect : WGShipModuleEffect
{
    /// <summary>Multiplier on each gun's turn rate.</summary>
    [DataField]
    public float TurnFactor = 1.5f;

    /// <summary>Played at a gun as it starts a wide swing.</summary>
    [DataField]
    public SoundSpecifier? SwingSound;

    /// <summary>Degrees off its aim a gun must be for its swing to whir and glow.</summary>
    [DataField]
    public float SwingAngle = 20f;
}

/// <summary>A gun under gyros started a wide swing: clients spin a ring round it.</summary>
[Serializable, NetSerializable]
public sealed class WGGunGyroSwingEvent : EntityEventArgs
{
    public NetEntity Gun;

    public WGGunGyroSwingEvent(NetEntity gun)
    {
        Gun = gun;
    }
}
