using System.Linq;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.RadialSelector;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>Lets a device module switch between several deployable tools.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModDeviceSelectorComponent : Component
{
    [DataField(required: true)]
    public List<RadialSelectorEntry> Entries = new();

    [DataField]
    public EntProtoId SelectAction = "ActionModSelectDevice";

    [DataField, AutoNetworkedField]
    public EntityUid? SelectActionEntity;
}

public sealed partial class ModSelectDeviceActionEvent : InstantActionEvent;

public sealed partial class ModDeviceSelectorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private ModDeviceModuleSystem _device = default!;
    [Dependency] private ModSuitSystem _modSuit = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<ModDeviceSelectorComponent> ent, ref MapInitEvent args)
    {
        if (!_actionContainer.EnsureAction(ent, ref ent.Comp.SelectActionEntity, ent.Comp.SelectAction))
            return;

        _actions.SetEntityIcon(ent.Comp.SelectActionEntity.Value, ent.Owner);
        Dirty(ent);
    }

    [SubscribeLocalEvent]
    private void OnWearerChanged(Entity<ModDeviceSelectorComponent> ent, ref ModSuitWearerChangedEvent args)
    {
        if (args.OldWearer is { } old)
            RemoveSelectAction(ent, old);

        if (args.NewWearer is { } wearer)
            GrantSelectAction(ent, wearer);
    }

    [SubscribeLocalEvent]
    private void OnInstalled(Entity<ModDeviceSelectorComponent> ent, ref ModModuleInstalledEvent args)
    {
        if (TryComp<ModSuitComponent>(args.Suit, out var suit) && suit.Wearer is { } wearer)
            GrantSelectAction(ent, wearer);
    }

    [SubscribeLocalEvent]
    private void OnRemoved(Entity<ModDeviceSelectorComponent> ent, ref ModModuleRemovedEvent args)
    {
        if (TryGetSelectAction(ent, out var action) && action.Comp.AttachedEntity is { } user)
            RemoveSelectAction(ent, user);
    }

    /// <summary>Gets the module's select action if it exists.</summary>
    private bool TryGetSelectAction(Entity<ModDeviceSelectorComponent> ent, out Entity<BaseActionComponent> action)
    {
        action = default;
        if (ent.Comp.SelectActionEntity is not { } actionUid
            || !TryComp<BaseActionComponent>(actionUid, out var actionComp))
            return false;

        action = (actionUid, actionComp);
        return true;
    }

    private void GrantSelectAction(Entity<ModDeviceSelectorComponent> ent, EntityUid wearer)
    {
        if (_timing.ApplyingState
            || !TryGetSelectAction(ent, out var action)
            || action.Comp.AttachedEntity == wearer)
            return;

        _actions.AddAction(wearer, action.Owner, ent.Owner, action: action.Comp);
    }

    private void RemoveSelectAction(Entity<ModDeviceSelectorComponent> ent, EntityUid user)
    {
        if (_timing.ApplyingState
            || !TryGetSelectAction(ent, out var action)
            || action.Comp.AttachedEntity != user)
            return;

        _actions.RemoveProvidedAction(user, ent, action.Owner);
    }

    [SubscribeLocalEvent]
    private void OnSelectAction(Entity<ModDeviceSelectorComponent> ent, ref ModSelectDeviceActionEvent args)
    {
        if (args.Handled
            || !_modSuit.TryGetModuleSuit(ent, out var suit)
            || suit.Comp.Wearer != args.Performer
            || !_modSuit.CanAccess(suit, args.Performer))
            return;

        args.Handled = true;
        if (_net.IsClient)
            return;

        _ui.SetUiState(ent.Owner, RadialSelectorUiKey.Key, new RadialSelectorState(ent.Comp.Entries, true));
        _ui.OpenUi(ent.Owner, RadialSelectorUiKey.Key, args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnSelected(Entity<ModDeviceSelectorComponent> ent, ref RadialSelectorSelectedMessage args)
    {
        _ui.CloseUi(ent.Owner, RadialSelectorUiKey.Key, args.Actor);

        var selected = args.SelectedItem;
        if (!_modSuit.TryGetModuleSuit(ent, out var suit)
            || suit.Comp.Wearer != args.Actor
            || !TryComp<ModDeviceModuleComponent>(ent, out var device)
            || ent.Comp.Entries.All(entry => entry.Prototype != selected)
            || !_proto.HasIndex<EntityPrototype>(selected))
            return;

        _device.SetDevice((ent.Owner, device), selected);
    }
}
