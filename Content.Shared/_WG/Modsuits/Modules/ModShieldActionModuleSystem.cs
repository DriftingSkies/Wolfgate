using Content.Shared._WG.Modsuits.Components;
using Content.Shared.Actions;

namespace Content.Shared._WG.Modsuits.Modules;

/// <summary>Grants a module's shield action once while it is active, and takes it back when it is not.</summary>
public sealed partial class ModShieldActionModuleSystem : EntitySystem
{
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModShieldActionModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModShieldActionModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<ModShieldActionModuleComponent, ModSuitWearerChangedEvent>(OnWearerChanged);
    }

    private void OnActivated(Entity<ModShieldActionModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        Grant(ent, args.Suit, args.Wearer);
    }

    private void OnDeactivated(Entity<ModShieldActionModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        Remove(ent, args.Suit, args.Wearer);
    }

    private void OnWearerChanged(Entity<ModShieldActionModuleComponent> ent, ref ModSuitWearerChangedEvent args)
    {
        if (!TryComp<ModModuleComponent>(ent, out var module)
            || !module.Active
            || module.Suit is not { } suit)
            return;

        Remove(ent, suit, args.OldWearer);
        Grant(ent, suit, args.NewWearer);
    }

    private void Grant(Entity<ModShieldActionModuleComponent> ent, EntityUid suit, EntityUid? wearer)
    {
        if (wearer is not { } user
            || !_actionContainer.EnsureAction(suit, ref ent.Comp.ActionEntity, out var action, ent.Comp.Action))
            return;

        if (action.AttachedEntity != user)
            _actions.AddAction(user, ent.Comp.ActionEntity.Value, suit, action: action);
    }

    private void Remove(Entity<ModShieldActionModuleComponent> ent, EntityUid suit, EntityUid? wearer)
    {
        if (wearer is not { } user || ent.Comp.ActionEntity is not { } action)
            return;

        _actions.RemoveProvidedAction(user, suit, action);
    }
}
