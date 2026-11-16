using Content.Shared.Emp;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModEmpPulseModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModEmpPulseModuleComponent, ModModuleUsedEvent>(OnUsed);
    }

    [Dependency] private SharedEmpSystem _emp = default!;
    private void OnUsed(Entity<ModEmpPulseModuleComponent> ent, ref ModModuleUsedEvent args)
    {
        _emp.EmpPulse(Transform(args.User).Coordinates, ent.Comp.Range, ent.Comp.EnergyConsumption, ent.Comp.Duration, args.User);
        args.Handled = true;
    }
}
