using System.Numerics;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Warheads;

/// <summary>
/// Sent to every client on the map when a warhead goes off, for the flash, shockwave, fireball and cloud.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFNuclearDetonationEvent : EntityEventArgs
{
    public NetEntity Map;
    public Vector2 Position;
    public TimeSpan Start;
    public float FlashRange;
    public float FireballRadius;
    public float ShockwaveRange;
    public float ShockwaveSpeed;
    public float ShakeStrength;
    public float Duration;

    /// <summary>On a planet: draw a mushroom cloud rather than a fireball in vacuum.</summary>
    public bool Mushroom;
}

[Serializable, NetSerializable]
public sealed partial class WFWarheadArmDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class WFWarheadDisarmDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class WFWarheadSafetyDoAfterEvent : SimpleDoAfterEvent;
