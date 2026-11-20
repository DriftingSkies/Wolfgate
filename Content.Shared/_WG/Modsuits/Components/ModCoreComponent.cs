using Content.Shared._WG.Modsuits.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Modsuits.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ModCoreComponent : Component
{
    /// <summary>
    /// Cell capacity in joules given to a cell in this core, indexed by its machine-part rating minus one.
    /// A small cell becomes a standard MOD cell, a hyper cell the TG hyper cell.
    /// </summary>
    [DataField]
    public List<float> CellChargeByRating = new() { 10_000f, 25_000f, 100_000f, 300_000f };

    /// <summary>Capacity given to a cell without a machine-part rating.</summary>
    [DataField]
    public float DefaultCellCharge = 10_000f;
}

/// <summary>Remembers a power cell's real capacity while a MOD core has it scaled.</summary>
[RegisterComponent]
public sealed partial class ModsuitCellComponent : Component
{
    [DataField, ViewVariables]
    public float OriginalMaxCharge;

    [DataField, ViewVariables]
    public float ScaledMaxCharge;
}

[RegisterComponent, NetworkedComponent]
[Access(typeof(ModCoreSystem))]
public sealed partial class ModCoreRefuelableComponent : Component
{
    [DataField]
    public EntityWhitelist Fuel = new();

    /// <summary>Entity prototype IDs that also count as fuel.</summary>
    [DataField]
    public List<string> FuelPrototypes = new();

    [DataField]
    public float ChargePerFuel = 200f;

    [DataField]
    public SoundSpecifier RefuelSound = new SoundPathSpecifier("/Audio/Effects/refill.ogg");
}
