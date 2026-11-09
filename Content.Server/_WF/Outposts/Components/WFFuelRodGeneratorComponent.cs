using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts.Components;

/// <summary>
/// Fuels a <see cref="Content.Shared.Power.Generator.FuelGeneratorComponent"/> from one reactor fuel rod loaded into it,
/// burned down over time. The generator's eject button takes the rod out.
/// </summary>
[RegisterComponent, Access(typeof(WFFuelRodGeneratorSystem))]
public sealed partial class WFFuelRodGeneratorComponent : Component
{
    public const string ContainerId = "wf_fuel_rod";

    /// <summary>The rod it takes.</summary>
    [DataField]
    public EntProtoId Rod = "UraniumReactorFuelRod";

    /// <summary>The fuel a fresh rod holds, in the generator's fuel units.</summary>
    [DataField]
    public float RodFuel = 100f;
}

/// <summary>What is left of a fuel rod once a generator has started burning it.</summary>
[RegisterComponent, Access(typeof(WFFuelRodGeneratorSystem))]
public sealed partial class WFFuelRodChargeComponent : Component
{
    [DataField]
    public float Fuel;

    /// <summary>The fuel it held when fresh, for the examine text.</summary>
    [DataField]
    public float Full = 100f;
}
