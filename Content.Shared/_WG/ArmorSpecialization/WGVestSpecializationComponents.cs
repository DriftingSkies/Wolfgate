using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.ArmorSpecialization;

// Specializations for vests and armour without spaceproofing: they do what a sealed suit can't.

/// <summary>Fast hands: the wearer's do-afters (reloads, cuffs, bandages, tools) finish sooner.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGFastHandsComponent : Component
{
    /// <summary>Multiplies do-after delays.</summary>
    [DataField, AutoNetworkedField]
    public float Multiplier = 0.8f;
}

/// <summary>Low signature: motion trackers don't pick up the wearer.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGLowSignatureComponent : Component;

/// <summary>Unrestricted: nothing binds the wearer's shoulders, so the guns they hold spread less.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGUnrestrictedComponent : Component
{
    /// <summary>Multiplies the spread of guns the wearer holds.</summary>
    [DataField, AutoNetworkedField]
    public float Spread = 0.8f;
}

/// <summary>Second wind: once the wearer is badly hurt, a burst of speed to get clear, then not again until healed.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGSecondWindComponent : Component
{
    /// <summary>Total damage on the wearer that sets it off, short of the 100 that puts a mob in crit.</summary>
    [DataField]
    public FixedPoint2 Threshold = 70;

    [DataField]
    public float SpeedMultiplier = 1.3f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(6);
}

/// <summary>Second wind's charge and burst, kept on the wearer so swapping vests neither resets nor moves them.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WGSecondWindStateComponent : Component
{
    /// <summary>When the burst ends; zero when it isn't running.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan ActiveUntil;

    /// <summary>The burst's speed, taken from the vest that set it off.</summary>
    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 1f;

    /// <summary>Spent until the wearer heals below half this threshold.</summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 Threshold;

    [DataField, AutoNetworkedField]
    public bool Spent;
}
