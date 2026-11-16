using Content.Server.Nutrition.Components;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Modules;
using Content.Shared._WF.Modsuits.Systems;

namespace Content.Server._WF.Modsuits;

/// <summary>Opens the helmet's mouth hole while the module is active.</summary>
public sealed partial class ModMouthholeModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModMouthholeModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModMouthholeModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
    }

    [Dependency] private ModSuitSystem _modSuit = default!;
    private void OnActivated(Entity<ModMouthholeModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (GetPart(ent, args.Suit) is { } part)
            RemComp<IngestionBlockerComponent>(part);
    }
    private void OnDeactivated(Entity<ModMouthholeModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (GetPart(ent, args.Suit) is { } part)
            _modSuit.RestoreComponent(part, Factory.GetComponentName<IngestionBlockerComponent>());
    }

    private EntityUid? GetPart(Entity<ModMouthholeModuleComponent> ent, EntityUid suit)
    {
        return TryComp<ModSuitComponent>(suit, out var suitComp) ? _modSuit.GetPart((suit, suitComp), ent.Comp.Slot) : null;
    }
}
