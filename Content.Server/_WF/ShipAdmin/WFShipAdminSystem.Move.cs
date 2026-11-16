using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Database;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._WF.ShipAdmin;

public sealed partial class WFShipAdminSystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    /// <summary>
    /// Moves a ship at once to a clear spot: next to the admin, at a position on its own map, or next to or docked
    /// with another grid. It is undocked first and comes to rest where it lands.
    /// </summary>
    public bool Move(ICommonSession admin, EntityUid grid, WFShipAdminMoveMessage move, out string result)
    {
        if (!CanMove(grid, out result))
            return false;

        EntityCoordinates? spot = null;
        EntityUid? other = null;
        switch (move.Kind)
        {
            case WFShipAdminMoveKind.ToMe:
                if (admin.AttachedEntity is not { Valid: true } body || Transform(body).MapUid is not { } here)
                {
                    result = Loc.GetString("wf-ship-admin-result-move-no-body");
                    return false;
                }

                spot = new EntityCoordinates(here, _transform.GetWorldPosition(body));
                break;
            case WFShipAdminMoveKind.ToPosition:
                if (Transform(grid).MapUid is not { } map || !float.IsFinite(move.Position.X) || !float.IsFinite(move.Position.Y))
                {
                    result = Loc.GetString("wf-ship-admin-result-move-bad-position");
                    return false;
                }

                spot = new EntityCoordinates(map, move.Position);
                break;
            default:
                if (!TryGetEntity(move.Target, out other) || other == grid || !HasComp<MapGridComponent>(other))
                {
                    result = Loc.GetString("wf-ship-admin-result-move-bad-target");
                    return false;
                }

                break;
        }

        _docking.UndockDocks(grid);
        var docked = false;
        bool moved;
        if (spot != null)
            moved = _shuttle.TryFTLProximity((grid, null), spot.Value);
        else if (move.Kind == WFShipAdminMoveKind.DockAt && TryComp<ShuttleComponent>(grid, out var shuttle))
            moved = docked = _shuttle.TryFTLDock(grid, shuttle, other!.Value);
        else
            moved = _shuttle.TryFTLProximity(grid, other!.Value);

        // No dock fitted: leave it alongside rather than where it was.
        if (!moved && move.Kind == WFShipAdminMoveKind.DockAt)
            moved = _shuttle.TryFTLProximity(grid, other!.Value);

        if (!moved)
        {
            result = Loc.GetString("wf-ship-admin-result-move-failed");
            return false;
        }

        Stop(grid);
        var where = other != null ? Name(other.Value) : Loc.GetString("wf-ship-admin-move-spot", ("map", Name(spot!.Value.EntityId)));
        Audit(admin, LogImpact.Medium, grid, $"moved it ({move.Kind}) to {where}; docked: {docked}");
        result = Loc.GetString(docked ? "wf-ship-admin-result-docked" : "wf-ship-admin-result-moved", ("ship", Name(grid)), ("where", where));
        return true;
    }

    /// <summary>Releases every dock on the ship.</summary>
    public bool Undock(ICommonSession admin, EntityUid grid, out string result)
    {
        _docking.UndockDocks(grid);
        Audit(admin, LogImpact.Medium, grid, "undocked it");
        result = Loc.GetString("wf-ship-admin-result-undocked", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Brings a drifting or spinning ship to rest.</summary>
    public bool Halt(ICommonSession admin, EntityUid grid, out string result)
    {
        Stop(grid);
        Audit(admin, LogImpact.Low, grid, "halted it");
        result = Loc.GetString("wf-ship-admin-result-halted", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Takes everyone off the ship's helms.</summary>
    public bool ClearPilots(ICommonSession admin, EntityUid grid, out string result)
    {
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out _, out var console, out var xform))
        {
            if (xform.GridUid == grid)
                _shuttleConsoles.ClearPilots(console);
        }

        Audit(admin, LogImpact.Low, grid, "cleared its pilots");
        result = Loc.GetString("wf-ship-admin-result-pilots-cleared", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Sets whether the ship travels along when a ship it is docked to jumps.</summary>
    public bool SetFtlLock(ICommonSession admin, EntityUid grid, bool enabled, out string result)
    {
        _shuttleConsoles.SetFTLLock(grid, enabled);
        Audit(admin, LogImpact.Low, grid, $"set its FTL lock to {enabled}");
        result = Loc.GetString(enabled ? "wf-ship-admin-result-ftl-lock-on" : "wf-ship-admin-result-ftl-lock-off", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Ends the wait after a jump, so the ship can jump again at once.</summary>
    public bool ResetFtl(ICommonSession admin, EntityUid grid, out string result)
    {
        if (!TryComp<FTLComponent>(grid, out var ftl) || ftl.State != FTLState.Cooldown)
        {
            result = Loc.GetString(ftl == null ? "wf-ship-admin-result-ftl-ready" : "wf-ship-admin-result-ftl-jumping");
            return false;
        }

        RemCompDeferred<FTLComponent>(grid);
        _shuttleConsoles.RefreshShuttleConsoles(grid);
        Audit(admin, LogImpact.Low, grid, "reset its FTL cooldown");
        result = Loc.GetString("wf-ship-admin-result-ftl-reset", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Whether the panel may move this grid: a ship, not a planet or a station, and not in the middle of a jump.</summary>
    private bool CanMove(EntityUid grid, out string result)
    {
        result = string.Empty;
        if (!CanBeOwned(grid))
        {
            result = Loc.GetString("wf-ship-admin-result-move-station");
            return false;
        }

        if (TryComp<FTLComponent>(grid, out var ftl) && ftl.State != FTLState.Cooldown)
        {
            result = Loc.GetString("wf-ship-admin-result-ftl-jumping");
            return false;
        }

        return true;
    }

    private void Stop(EntityUid grid)
    {
        if (!TryComp<PhysicsComponent>(grid, out var body))
            return;

        _physics.SetLinearVelocity(grid, Vector2.Zero, body: body);
        _physics.SetAngularVelocity(grid, 0f, body: body);
    }
}
