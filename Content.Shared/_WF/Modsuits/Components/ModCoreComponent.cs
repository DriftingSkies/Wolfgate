using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Modsuits.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ModCoreComponent : Component;

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
