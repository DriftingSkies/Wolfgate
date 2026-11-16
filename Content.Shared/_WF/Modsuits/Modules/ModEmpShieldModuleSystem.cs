using Content.Shared._WF.Modsuits.Components;
using Content.Shared.Emp;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModEmpShieldModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSuitComponent, EmpAttemptEvent>(OnSuitEmpAttempt);
    }

    private void OnSuitEmpAttempt(Entity<ModSuitComponent> ent, ref EmpAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        foreach (var module in ent.Comp.ModulesContainer.ContainedEntities)
        {
            if (!TryComp<ModEmpShieldModuleComponent>(module, out var shield)
                || !TryComp<ModModuleComponent>(module, out var moduleComp)
                || shield.RequireActive && !moduleComp.Active)
                continue;

            args.Cancelled = true;
            return;
        }
    }
}
