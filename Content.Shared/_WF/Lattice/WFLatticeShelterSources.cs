namespace Content.Shared._WF.Lattice;

/// <summary>A machine that, powered and anchored, holds a little anchored space round the ship it stands on.</summary>
[RegisterComponent]
public sealed partial class WFLatticeStabiliserComponent : Component
{
    /// <summary>Share of the ship's Drift gain it keeps off.</summary>
    [DataField]
    public float Shelter = 0.8f;
}

/// <summary>A charm that shelters whoever wears it from some of the Drift.</summary>
[RegisterComponent]
public sealed partial class WFLatticeCharmComponent : Component
{
    [DataField]
    public float Shelter = 0.5f;
}
