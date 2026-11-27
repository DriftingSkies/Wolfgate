using Content.Shared._WG.Modsuits.Components;
using Content.Shared.Emp;

namespace Content.Shared._WG.Modsuits.Modules;

public sealed partial class ModEmpShieldModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSuitComponent, EmpAttemptEvent>(OnSuitEmpAttempt);
        SubscribeLocalEvent<ModSuitComponent, ContainedEmpAttemptEvent>(OnSuitContainedEmpAttempt);
    }

    private void OnSuitEmpAttempt(Entity<ModSuitComponent> ent, ref EmpAttemptEvent args)
    {
        if (HasShield(ent))
            args.Cancelled = true;
    }

    /// <summary>Shields the core, its cell and the installed modules from an EMP aimed at them directly.</summary>
    private void OnSuitContainedEmpAttempt(Entity<ModSuitComponent> ent, ref ContainedEmpAttemptEvent args)
    {
        if (HasShield(ent))
            args.Cancelled = true;
    }

    private bool HasShield(Entity<ModSuitComponent> ent)
    {
        foreach (var module in ent.Comp.ModulesContainer.ContainedEntities)
        {
            // The ward belongs to the suit item, so it works even when the suit is folded on the floor.
            if (HasComp<ModEmpShieldModuleComponent>(module))
                return true;
        }

        return false;
    }
}
