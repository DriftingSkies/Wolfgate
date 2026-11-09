using Content.Server.Storage.Components;
using Content.Shared._WF.MappingTools;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Lock;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Wires;
using Robust.Shared.Player;

namespace Content.Server._WF.MappingTools;

public sealed partial class MappingToolsSystem
{
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private SharedWiresSystem _wires = default!;
    [Dependency] private WeldableSystem _weldable = default!;

    private void InitializeToggles()
    {
        SubscribeNetworkEvent<MappingToolsToggleEvent>(OnToggle);
    }

    private void OnToggle(MappingToolsToggleEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            Toggle(args.SenderSession, ev.Target, ev.Toggle);
    }

    /// <summary>
    /// Flips bolts, a weld, a lock, a panel or a door on one entity as one undoable edit, straight to the end state:
    /// no tool, wait, power or user, so it works on paused session maps.
    /// </summary>
    public void Toggle(ICommonSession session, NetEntity target, MappingToggle toggle)
    {
        if (!TryGetEntity(target, out var found) || !_protection.CanTouch(found.Value) ||
            !TryToggleState(found.Value, toggle, out var current) ||
            !TryGetOnMap<MetaDataComponent>(target, out var uid, out var map, out _))
        {
            RefuseLink(session, "wf-mapping-tool-bad");
            return;
        }

        if (ToggleRefusal(uid, toggle, current) is { } refusal)
        {
            RefuseLink(session, refusal);
            return;
        }

        if (!CanEditMaps(session, [map], 1))
            return;

        var value = !current;
        ApplyToggle(uid, toggle, value);
        if (!TryToggleState(uid, toggle, out var now) || now != value)
        {
            RefuseLink(session, "wf-mapping-tool-refused");
            return;
        }

        var edit = new MappingEdit($"wf-mapping-tools-edit-{toggle.ToString().ToLowerInvariant()}");
        edit.Toggles.Add(new MappingToggleChange(GetRef(uid), toggle, current, value));
        Commit(session, edit);

        var state = Loc.GetString($"wf-mapping-tool-state-{toggle.ToString().ToLowerInvariant()}",
            ("on", value ? "yes" : "no"));
        _popup.PopupCursor(Loc.GetString("wf-mapping-tool-done", ("name", Name(uid)), ("state", state)), session);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{session:player} used mapping tools to set {toggle} {value} on {ToPrettyString(uid):entity}");
    }

    /// <summary>The state a toggle reads on an entity, false when the entity doesn't have it.</summary>
    public bool TryToggleState(EntityUid uid, MappingToggle toggle, out bool on)
    {
        on = false;
        switch (toggle)
        {
            case MappingToggle.Bolts when TryComp(uid, out DoorBoltComponent? bolts):
                on = bolts.BoltsDown;
                return true;
            case MappingToggle.Weld when TryComp(uid, out WeldableComponent? weld):
                on = weld.IsWelded;
                return true;
            case MappingToggle.Lock when TryComp(uid, out LockComponent? lockComp):
                on = lockComp.Locked;
                return true;
            case MappingToggle.Panel when TryComp(uid, out WiresPanelComponent? panel):
                on = panel.Open;
                return true;
            case MappingToggle.Door when TryComp(uid, out DoorComponent? door):
                on = door.State is DoorState.Open or DoorState.Opening;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Why a toggle can't flip on this entity now, as a Fluent id, or null.</summary>
    private string? ToggleRefusal(EntityUid uid, MappingToggle toggle, bool current)
    {
        TryComp(uid, out DoorComponent? door);
        return toggle switch
        {
            MappingToggle.Weld when !current && (door != null && door.State != DoorState.Closed ||
                                                 TryComp(uid, out EntityStorageComponent? storage) && storage.Open) =>
                "wf-mapping-tool-close-first",
            MappingToggle.Door when door?.State == DoorState.Welded => "wf-mapping-tool-unweld-first",
            MappingToggle.Panel when TryComp(uid, out WiresPanelComponent? panel) &&
                                     !_wires.CanTogglePanel((uid, panel), null) => "wf-mapping-tool-refused",
            _ => null,
        };
    }

    /// <summary>Sets a toggle's state without checks; Tool mode and its undo both go through here.</summary>
    private void ApplyToggle(EntityUid uid, MappingToggle toggle, bool value)
    {
        if (TerminatingOrDeleted(uid))
            return;

        switch (toggle)
        {
            case MappingToggle.Bolts when TryComp(uid, out DoorBoltComponent? bolts):
                _door.WFSetBoltsQuiet((uid, bolts), value);
                break;
            case MappingToggle.Weld when TryComp(uid, out WeldableComponent? weld):
                _weldable.SetWeldedState(uid, value, weld);
                break;
            case MappingToggle.Lock when TryComp(uid, out LockComponent? lockComp):
                if (value)
                    _lock.Lock(uid, null, lockComp);
                else
                    _lock.Unlock(uid, null, lockComp);
                break;
            case MappingToggle.Panel when TryComp(uid, out WiresPanelComponent? panel):
                _wires.TogglePanel(uid, panel, value);
                break;
            case MappingToggle.Door when TryComp(uid, out DoorComponent? door):
                _door.WFSetRestingState((uid, door), value);
                break;
        }
    }
}
