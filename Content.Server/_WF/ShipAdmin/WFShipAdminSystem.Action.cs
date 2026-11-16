using Content.Shared._WF.ShipAdmin;
using Content.Shared.Actions;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ShipAdmin;

public sealed partial class WFShipAdminSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;

    private static readonly EntProtoId OpenAction = "WFActionShipmaster";
    private static readonly TimeSpan ActionInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>The body each admin holds the action on, and the action; an admin over no ship has no entry.</summary>
    private readonly Dictionary<ICommonSession, (EntityUid Body, EntityUid Action)> _openActions = new();
    private readonly List<ICommonSession> _staleActions = new();
    private TimeSpan _actionTimer;

    /// <summary>Whether the admin holds the Shipmaster action right now.</summary>
    public bool HasOpenAction(ICommonSession admin)
    {
        return _openActions.ContainsKey(admin);
    }

    /// <summary>
    /// The ship an admin's body is over, if any. A map that is its own grid, a planet's surface, is not a ship:
    /// someone on a planet is always over it.
    /// </summary>
    public EntityUid? ShipUnder(ICommonSession admin)
    {
        if (admin.AttachedEntity is not { Valid: true } body || !TryComp(body, out TransformComponent? xform))
            return null;

        return xform.GridUid is { } grid && !HasComp<MapComponent>(grid) ? grid : null;
    }

    /// <summary>
    /// Gives each active admin the Shipmaster action while they are over a ship and takes it back when they leave
    /// it, lose the flag or change body. Polled twice a second over the admins alone, so nothing rides on movement.
    /// </summary>
    private void UpdateActions(float frameTime)
    {
        _actionTimer += TimeSpan.FromSeconds(frameTime);
        if (_actionTimer < ActionInterval)
            return;

        _actionTimer = TimeSpan.Zero;
        foreach (var (admin, held) in _openActions)
        {
            if (admin.AttachedEntity != held.Body || !_admin.HasAdminFlag(admin, WFShipAdminPermissions.Base) || ShipUnder(admin) == null)
                _staleActions.Add(admin);
        }

        foreach (var admin in _staleActions)
        {
            if (!_openActions.Remove(admin, out var held))
                continue;

            if (TerminatingOrDeleted(held.Action))
                continue;

            if (!TerminatingOrDeleted(held.Body))
                _actions.RemoveAction(held.Body, held.Action);

            QueueDel(held.Action);
        }

        _staleActions.Clear();
        foreach (var admin in _admin.ActiveAdmins)
        {
            if (_openActions.ContainsKey(admin) || !_admin.HasAdminFlag(admin, WFShipAdminPermissions.Base) || ShipUnder(admin) == null)
                continue;

            if (_actions.AddAction(admin.AttachedEntity!.Value, OpenAction) is { } action)
                _openActions[admin] = (admin.AttachedEntity.Value, action);
        }
    }

    private void OnOpenAction(WFShipAdminOpenActionEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.Performer, out var actor))
            return;

        var admin = actor.PlayerSession;
        if (!_admin.HasAdminFlag(admin, WFShipAdminPermissions.Base) || ShipUnder(admin) is not { } ship)
            return;

        Open(admin, ship);
        args.Handled = true;
    }
}
