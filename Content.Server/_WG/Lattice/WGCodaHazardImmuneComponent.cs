namespace Content.Server._WG.Lattice;

/// <summary>A grid the Coda's ship hazards pass over: no hunters, rot, failures, flesh or leviathan. Some sites keep their own dangers.</summary>
[RegisterComponent]
public sealed partial class WGCodaHazardImmuneComponent : Component;

/// <summary>Something the Coda wants back: aboard a ship, it makes the hunters likelier to come.</summary>
[RegisterComponent]
public sealed partial class WGCodaLureComponent : Component
{
    /// <summary>How much likelier a hunter pack is on the ship carrying it.</summary>
    [DataField]
    public float HunterScale = 3f;
}
