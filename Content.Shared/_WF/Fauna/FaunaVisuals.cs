using Robust.Shared.Serialization;

namespace Content.Shared._WF.Fauna;

/// <summary>
/// Appearance key set while a fauna mob with an aggro cry has a living target.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaAggroVisuals : byte
{
    Aggro,
}

/// <summary>
/// Appearance key for whether an ambushing mob has shown itself.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaAmbushVisuals : byte
{
    Revealed,
}

/// <summary>
/// Appearance key for how worn a fauna mob's breakable shell is.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaBreakableShellVisuals : byte
{
    Stage,
}

/// <summary>
/// How worn a breakable shell is. Dead covers the corpse, so one visualizer drives the whole sprite.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaBreakableShellStage : byte
{
    Whole,
    Cracked,
    Broken,
    Dead,
}

/// <summary>
/// Appearance keys of a charging fauna mob: <see cref="Charging"/> while it charges, and <see cref="Windup"/> as a
/// world direction away from its target while it winds up, or zero.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaChargeVisuals : byte
{
    Charging,
    Windup,
}

/// <summary>
/// Appearance key set while an enraged fauna mob is enraged.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaEnrageVisuals : byte
{
    Enraged,
}

/// <summary>
/// Appearance key holding what a gourmonger is doing.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaGourmongerVisuals : byte
{
    State,
}

/// <summary>
/// What a gourmonger is doing, for <see cref="FaunaGourmongerVisuals.State"/>.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaGourmongerState : byte
{
    Idle,
    Eating,
    Sniffing,
}

/// <summary>
/// Appearance key set while a fauna mob is inflated.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaInflateVisuals : byte
{
    Inflated,
}

/// <summary>
/// Appearance key holding the <see cref="FaunaLineAttackState"/> of a fauna line attack.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaLineAttackVisuals : byte
{
    State,
}

/// <summary>
/// Whether a line attack is idle, winding up or firing.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaLineAttackState : byte
{
    Idle,
    Charging,
    Firing,
}

/// <summary>
/// Appearance keys set while a fauna mob winds up a projectile burst, and while it fires a volley or spiral.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaProjectileBurstVisuals : byte
{
    Windup,
    Firing,
}

/// <summary>
/// Appearance key set while a shelled fauna mob lies flipped on its back.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaShellVisuals : byte
{
    Flipped,
}

/// <summary>
/// Appearance key set while a fauna goliath's tentacle action is off cooldown.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaTentacleVisuals : byte
{
    Ready,
}

/// <summary>
/// Appearance key holding the sprite state a worm head or segment shows.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaWormVisuals : byte
{
    State,
}

/// <summary>
/// Sprite layer the worm state is drawn on.
/// </summary>
[Serializable, NetSerializable]
public enum FaunaWormVisualLayers : byte
{
    Base,
}
