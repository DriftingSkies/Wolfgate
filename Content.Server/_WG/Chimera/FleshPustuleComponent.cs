namespace Content.Server._WG.Chimera;

/// <summary>A one-shot flesh tick nest. Bursted is persisted so streamed biome traps cannot refill.</summary>
[RegisterComponent]
public sealed partial class WGFleshPustuleComponent : Component
{
    [DataField]
    public bool Bursted;

    [DataField]
    public float SpillQuantity = 15f;
}

/// <summary>Tracks ticks created by pustules for the bounded population guard.</summary>
[RegisterComponent]
public sealed partial class WGFleshPustuleSpawnedTickComponent : Component
{
    [DataField]
    public EntityUid Ground;
}
