namespace Content.Server._WG.Fragments;

/// <summary>
/// A fragment's heart; breaking it freezes which factions count as aboard for the joint bonus. Only someone standing on
/// the fragment within <see cref="Reach"/> can hurt it, so ship guns and bombs set off outside do nothing.
/// </summary>
[RegisterComponent]
public sealed partial class WGFragmentHeartComponent : Component
{
    /// <summary>An attacker must be on the heart's grid and within this many metres of it for its damage to land.</summary>
    [DataField]
    public float Reach = 20f;
}
