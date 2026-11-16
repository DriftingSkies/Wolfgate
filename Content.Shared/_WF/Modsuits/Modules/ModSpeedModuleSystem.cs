using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Movement.Systems;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModSpeedModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSpeedModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModSpeedModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<ModSpeedModuleComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    [Dependency] private ModSuitSystem _modSuit = default!;
    private void OnActivated(Entity<ModSpeedModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        Refresh(args.Suit);
    }
    private void OnDeactivated(Entity<ModSpeedModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        Refresh(args.Suit);
    }
    private void OnRefreshSpeed(Entity<ModSpeedModuleComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.WalkModifier, ent.Comp.SprintModifier);
    }

    private void Refresh(EntityUid suit)
    {
        if (TryComp<ModSuitComponent>(suit, out var suitComp))
            _modSuit.RefreshWearerSpeed((suit, suitComp));
    }
}
