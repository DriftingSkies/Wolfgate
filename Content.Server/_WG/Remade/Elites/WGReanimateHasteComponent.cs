namespace Content.Server._WG.Remade.Elites;

/// <summary>An Infector that turns corpses faster than the plain kind: its conversions take this share of the time.</summary>
[RegisterComponent]
public sealed partial class WGReanimateHasteComponent : Component
{
    [DataField]
    public float Share = 0.55f;
}

/// <summary>A conversion that has already been sped up.</summary>
[RegisterComponent]
public sealed partial class WGReanimateHastenedComponent : Component;
