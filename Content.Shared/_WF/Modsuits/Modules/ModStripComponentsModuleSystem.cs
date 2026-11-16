using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModStripComponentsModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModStripComponentsModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModStripComponentsModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
    }

    [Dependency] private ModSuitSystem _modSuit = default!;
    private void OnActivated(Entity<ModStripComponentsModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (!TryComp<ModSuitComponent>(args.Suit, out var suit))
            return;

        foreach (var part in suit.PartEntities.Values)
        {
            if (TerminatingOrDeleted(part))
                continue;

            foreach (var name in ent.Comp.Components)
            {
                if (Factory.TryGetRegistration(name, out var registration) && HasComp(part, registration.Type))
                    RemComp(part, registration.Type);
            }
        }
    }
    private void OnDeactivated(Entity<ModStripComponentsModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (!TryComp<ModSuitComponent>(args.Suit, out var suit))
            return;

        foreach (var part in suit.PartEntities.Values)
        {
            foreach (var name in ent.Comp.Components)
            {
                _modSuit.RestoreComponent(part, name);
            }
        }
    }
}
