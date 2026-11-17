using System.IO;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Eui;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization;

namespace Content.Server._WG.ShipAdmin;

/// <summary>Server half of the admin ship panel: one admin's filter, selection and last result.</summary>
public sealed partial class WGShipAdminEui : BaseEui
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IRobustSerializer _serializer = default!;

    private readonly WGShipAdminSystem _ships;

    private bool _sendList = true;
    private byte[] _sentDetail = Array.Empty<byte>();
    private string _result = string.Empty;
    private bool _resultError;
    private int _resultSeq;
    private EntityUid? _scanned;
    private int? _value;
    private WGShipAdminDamage? _damage;

    public WGShipAdminFilter Filter { get; private set; }
    public EntityUid? Selected { get; private set; }

    public WGShipAdminEui(EntityUid? selected = null)
    {
        IoCManager.InjectDependencies(this);
        _ships = _systems.GetEntitySystem<WGShipAdminSystem>();
        Selected = selected;
    }

    public override void Opened()
    {
        _ships.PanelOpened(this);
        _admin.OnPermsChanged += OnPermsChanged;
        StateDirty();
    }

    public override void Closed()
    {
        _admin.OnPermsChanged -= OnPermsChanged;
        _ships.PanelClosed(this);
    }

    public override EuiStateBase GetNewState()
    {
        var state = new WGShipAdminEuiState
        {
            Filter = Filter,
            ViewerFlags = _ships.Flags(Player),
            Result = _result,
            ResultError = _resultError,
            ResultSeq = _resultSeq,
        };

        if (_sendList)
        {
            state.Ships = _ships.BuildList(Filter, out state.Truncated);
            _sendList = false;
        }

        state.Selected = Detail();
        _sentDetail = Pack(state.Selected);
        return state;
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_admin.HasAdminFlag(Player, WGShipAdminPermissions.Base))
        {
            Close();
            return;
        }

        switch (msg)
        {
            case WGShipAdminSelectMessage select:
                Select(_entMan.TryGetEntity(select.Grid, out var picked) ? picked : null);
                break;
            case WGShipAdminListMessage list when Enum.IsDefined(list.Filter):
                Filter = list.Filter;
                RefreshList();
                break;
            case WGShipAdminActionMessage action when Enum.IsDefined(action.Action):
                if (TryTarget(action.Grid, action.Action, out var grid))
                    _ships.Run(this, grid, action.Action, action.Value, action.Notify);
                break;
            case WGShipAdminTransferMessage transfer when Enum.IsDefined(transfer.Faction):
                if (TryTarget(transfer.Grid, WGShipAdminAction.Transfer, out var given))
                    Finish(_ships.Transfer(Player, given, transfer, out var transferred), transferred);
                break;
            case WGShipAdminOwnerMessage owner:
                if (!TryTarget(owner.Grid, owner.Add ? WGShipAdminAction.AddOwner : WGShipAdminAction.RemoveOwner, out var owned))
                    break;

                if (owner.Add && owner.User is { } added)
                    Finish(_ships.AddOwner(Player, owned, added, owner.Notify, out var addResult), addResult);
                else if (!owner.Add)
                    Finish(_ships.RemoveOwner(Player, owned, owner.User, _entMan.GetEntity(owner.Card), owner.Notify, out var removeResult), removeResult);
                break;
            case WGShipAdminFixMessage fix:
                if (TryTarget(fix.Grid, WGShipAdminAction.Fix, out var fixedShip))
                    Finish(_ships.StartFix(Player, fixedShip, fix.Parts, out var fixResult), fixResult);
                break;
            case WGShipAdminMoveMessage move when Enum.IsDefined(move.Kind):
                var moveAction = move.Kind switch
                {
                    WGShipAdminMoveKind.ToMe => WGShipAdminAction.BringToMe,
                    WGShipAdminMoveKind.DockAt => WGShipAdminAction.DockAt,
                    _ => WGShipAdminAction.SendTo,
                };
                if (TryTarget(move.Grid, moveAction, out var movedShip))
                    Finish(_ships.Move(Player, movedShip, move, out var moveResult), moveResult);
                break;
            case WGShipAdminRenameMessage rename:
                if (TryTarget(rename.Grid, WGShipAdminAction.Rename, out var renamed))
                    Finish(_ships.Rename(Player, renamed, rename.Name ?? string.Empty, out var renameResult), renameResult);
                break;
            case WGShipAdminIffMessage iff:
                if (TryTarget(iff.Grid, WGShipAdminAction.Iff, out var signed))
                    Finish(_ships.SetIff(Player, signed, iff, out var iffResult), iffResult);
                break;
            case WGShipAdminShieldMessage shield:
                if (TryTarget(shield.Grid, WGShipAdminAction.Shields, out var shielded))
                    Finish(_ships.SetShieldStrength(Player, shielded, shield.Percent, out var shieldResult), shieldResult);
                break;
            case WGShipAdminProtectMessage protect:
                if (TryTarget(protect.Grid, WGShipAdminAction.Protect, out var guarded))
                    Finish(_ships.SetProtect(Player, guarded, protect.Protect, protect.On, out var protectResult), protectResult);
                break;
            case WGShipAdminRefitMessage refit:
                if (TryTarget(refit.Grid, WGShipAdminAction.Refit, out var refitted))
                    Finish(_ships.StartRefit(Player, refitted, refit.Refit ?? string.Empty, refit.Tier, out var refitResult), refitResult);
                break;
            case WGShipAdminModifierMessage modifier:
                if (TryTarget(modifier.Grid, WGShipAdminAction.Modifier, out var modified))
                    Finish(_ships.ApplyModifier(Player, modified, modifier.Modifier ?? string.Empty, out var modifierResult), modifierResult);
                break;
            case WGShipAdminModeMessage mode when Enum.IsDefined(mode.Mode):
                if (TryTarget(mode.Grid, WGShipAdminAction.SetMode, out var moded))
                    Finish(_ships.SetMode(Player, moded, mode.Mode, mode.Notify, out var modeResult), modeResult);
                break;
            case WGShipAdminAllowMessage allow:
                if (!TryTarget(allow.Grid, allow.Add != null ? WGShipAdminAction.AllowAdd : WGShipAdminAction.AllowRemove, out var listed))
                    break;

                if (allow.Add is { } allowed)
                    Finish(_ships.AllowAdd(Player, listed, allowed, allow.Notify, out var allowResult), allowResult);
                else if (allow.Remove is { } key)
                    Finish(_ships.AllowRemove(Player, listed, key, allow.Notify, out var denyResult), denyResult);
                break;
        }
    }

    /// <summary>Shows an action's outcome and sends the list again, since most actions change a row.</summary>
    public void Finish(bool done, string result)
    {
        _sendList = true;
        SetResult(result, !done);
    }

    /// <summary>Selects a ship, or none, and sends its detail.</summary>
    public void Select(EntityUid? grid)
    {
        // A scan's findings go stale once the admin looks away from the ship.
        if (Selected != grid)
        {
            _scanned = null;
            _value = null;
            _damage = null;
        }

        Selected = grid;
        StateDirty();
    }

    /// <summary>Sends the list again with the next state.</summary>
    public void RefreshList()
    {
        _sendList = true;
        StateDirty();
    }

    /// <summary>Sends the selected ship's detail again if anything in it changed.</summary>
    public void RefreshDetail()
    {
        if (!Pack(Detail()).AsSpan().SequenceEqual(_sentDetail))
            StateDirty();
    }

    /// <summary>Shows the outcome of an action in the panel.</summary>
    public void SetResult(string result, bool error = false)
    {
        _result = result;
        _resultError = error;
        _resultSeq++;
        StateDirty();
    }

    /// <summary>Keeps a scan's findings for as long as the ship stays selected.</summary>
    public void Scanned(EntityUid grid, int value, WGShipAdminDamage damage)
    {
        _scanned = grid;
        _value = value;
        _damage = damage;
    }

    /// <summary>
    /// The grid a message names, when it still exists and the viewer may run the action on it; otherwise the
    /// refusal goes to the result line.
    /// </summary>
    public bool TryTarget(NetEntity target, WGShipAdminAction action, out EntityUid grid)
    {
        grid = default;
        if (!_ships.Allowed(Player, action))
        {
            SetResult(Loc.GetString("wg-ship-admin-result-no-permission"), true);
            return false;
        }

        if (!_entMan.TryGetEntity(target, out var found) || !_entMan.HasComponent<MapGridComponent>(found))
        {
            SetResult(Loc.GetString("wg-ship-admin-result-gone"), true);
            return false;
        }

        grid = found.Value;
        return true;
    }

    private WGShipAdminDetail? Detail()
    {
        if (Selected is not { } grid)
            return null;

        var scanned = _scanned == grid;
        var detail = _ships.BuildDetail(grid, scanned ? _value : null, scanned ? _damage : null);
        if (detail == null)
            Selected = null;

        return detail;
    }

    private byte[] Pack(WGShipAdminDetail? detail)
    {
        if (detail == null)
            return Array.Empty<byte>();

        using var stream = new MemoryStream();
        _serializer.Serialize(stream, detail);
        return stream.ToArray();
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player != Player)
            return;

        if (_admin.HasAdminFlag(Player, WGShipAdminPermissions.Base))
            StateDirty();
        else
            Close();
    }
}
