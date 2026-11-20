using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Database;
using Content.Server.Shuttles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    /// <summary>
    /// Moves a ship at once to a clear spot: next to the admin, at a position on its own map, or next to or docked
    /// with another grid. It is undocked first and comes to rest where it lands.
    /// </summary>
    public bool Move(ICommonSession admin, EntityUid grid, WGShipAdminMoveMessage move, out string result)
    {
        if (!CanMove(grid, out result))
            return false;

        EntityCoordinates? spot = null;
        EntityUid? other = null;
        switch (move.Kind)
        {
            case WGShipAdminMoveKind.ToMe:
                if (admin.AttachedEntity is not { Valid: true } body || Transform(body).MapUid is not { } here)
                {
                    result = Loc.GetString("wg-ship-admin-result-move-no-body");
                    return false;
                }

                spot = new EntityCoordinates(here, _transform.GetWorldPosition(body));
                break;
            case WGShipAdminMoveKind.ToPosition:
                if (Transform(grid).MapUid is not { } map || !float.IsFinite(move.Position.X) || !float.IsFinite(move.Position.Y))
                {
                    result = Loc.GetString("wg-ship-admin-result-move-bad-position");
                    return false;
                }

                spot = new EntityCoordinates(map, move.Position);
                break;
            default:
                if (!TryGetEntity(move.Target, out other) || other == grid || !HasComp<MapGridComponent>(other))
                {
                    result = Loc.GetString("wg-ship-admin-result-move-bad-target");
                    return false;
                }

                break;
        }

        _docking.UndockDocks(grid);
        var docked = false;
        bool moved;
        if (spot != null)
            moved = _shuttle.TryFTLProximity((grid, null), spot.Value);
        else if (move.Kind == WGShipAdminMoveKind.DockAt && TryComp<ShuttleComponent>(grid, out var shuttle))
            moved = docked = _shuttle.TryFTLDock(grid, shuttle, other!.Value);
        else
            moved = _shuttle.TryFTLProximity(grid, other!.Value);

        // No dock fitted: leave it alongside rather than where it was.
        if (!moved && move.Kind == WGShipAdminMoveKind.DockAt)
            moved = _shuttle.TryFTLProximity(grid, other!.Value);

        if (!moved)
        {
            result = Loc.GetString("wg-ship-admin-result-move-failed");
            return false;
        }

        Stop(grid);
        var where = other != null ? Name(other.Value) : Loc.GetString("wg-ship-admin-move-spot", ("map", Name(spot!.Value.EntityId)));
        Audit(admin, LogImpact.Medium, grid, $"moved it ({move.Kind}) to {where}; docked: {docked}");
        result = Loc.GetString(docked ? "wg-ship-admin-result-docked" : "wg-ship-admin-result-moved", ("ship", Name(grid)), ("where", where));
        return true;
    }

    /// <summary>Every dock of a grid, by name, with what is docked to it.</summary>
    public List<WGShipAdminDock> ListDocks(EntityUid target)
    {
        var docks = new List<WGShipAdminDock>();
        var unnamed = 0;
        foreach (var dock in _docking.GetDocks(target))
        {
            unnamed++;
            var name = dock.Comp.Name is { } label
                ? Loc.GetString(label)
                : Loc.GetString("wg-ship-admin-dock-unnamed", ("number", unnamed));
            var holder = dock.Comp.DockedWith is { } other ? Transform(other).GridUid : null;
            docks.Add(new WGShipAdminDock
            {
                Dock = GetNetEntity(dock),
                Name = name,
                DockedWith = holder is { } held ? Name(held) : string.Empty,
                DockedWithGrid = holder is { } heldGrid ? GetNetEntity(heldGrid) : null,
            });
        }

        docks.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return docks;
    }

    /// <summary>
    /// Docks a ship at one dock of another grid, trying each of the ship's free docks against it. With no dock
    /// named it docks at whichever fits, or sets down alongside.
    /// </summary>
    public bool DockAt(ICommonSession admin, EntityUid grid, EntityUid target, EntityUid? dock, out string result)
    {
        if (dock == null)
            return Move(admin, grid, new WGShipAdminMoveMessage(GetNetEntity(grid), WGShipAdminMoveKind.DockAt, GetNetEntity(target), Vector2.Zero), out result);

        if (!CanMove(grid, out result))
            return false;

        if (target == grid || !HasComp<MapGridComponent>(target)
            || !TryComp<DockingComponent>(dock, out var targetDock) || Transform(dock.Value).GridUid != target)
        {
            result = Loc.GetString("wg-ship-admin-result-move-bad-target");
            return false;
        }

        if (targetDock.DockedWith is { } held && Transform(held).GridUid != grid)
        {
            result = Loc.GetString("wg-ship-admin-result-dock-occupied", ("dock", DockName(dock.Value, targetDock)), ("with", Name(Transform(held).GridUid ?? held)));
            return false;
        }

        // Undocked first, so the ship's own docks and the grid it hangs on don't stand in the way. The berth is
        // remembered, and the ship goes back into it when nothing fits.
        var berth = new List<(Entity<DockingComponent> Own, Entity<DockingComponent> Other)>();
        foreach (var own in _docking.GetDocks(grid))
        {
            if (own.Comp.DockedWith is { } other && TryComp<DockingComponent>(other, out var otherDock))
                berth.Add((own, (other, otherDock)));
        }

        _docking.UndockDocks(grid);
        DockingConfig? config = null;
        foreach (var own in _docking.GetDocks(grid))
        {
            config = _docking.GetDockingConfig(grid, target, own.Owner, own.Comp, dock.Value, targetDock);
            if (config != null)
                break;
        }

        if (config == null)
        {
            foreach (var (own, other) in berth)
            {
                if (!TerminatingOrDeleted(own) && !TerminatingOrDeleted(other) && !own.Comp.Docked && !other.Comp.Docked)
                    _docking.Dock(own, other);
            }

            result = Loc.GetString("wg-ship-admin-result-dock-no-fit", ("dock", DockName(dock.Value, targetDock)), ("ship", Name(grid)));
            return false;
        }

        _shuttle.FTLDock((grid, Transform(grid)), config);
        Stop(grid);
        Audit(admin, LogImpact.Medium, grid, $"docked it at {ToPrettyString(dock.Value)} on {ToPrettyString(target)}");
        result = Loc.GetString("wg-ship-admin-result-docked-at", ("ship", Name(grid)), ("dock", DockName(dock.Value, targetDock)), ("where", Name(target)));
        return true;
    }

    private string DockName(EntityUid dock, DockingComponent comp)
    {
        return comp.Name is { } label ? Loc.GetString(label) : Name(dock);
    }

    /// <summary>
    /// Stop Knotting: frees a ship that is caught on another grid or wedged in debris. It is undocked, its pilots
    /// cleared and it is set down at rest in the nearest clear space, clear of every grid including its old spot.
    /// </summary>
    public bool Unstick(ICommonSession admin, EntityUid grid, out string result)
    {
        if (!CanMove(grid, out result))
            return false;

        var xform = Transform(grid);
        if (xform.MapUid is not { } map)
        {
            result = Loc.GetString("wg-ship-admin-result-move-failed");
            return false;
        }

        _docking.UndockDocks(grid);
        ReleaseBindings(grid);
        Stop(grid);
        var here = new EntityCoordinates(map, _transform.GetWorldPosition(xform));
        if (!_shuttle.TryFTLProximity((grid, xform), here))
        {
            result = Loc.GetString("wg-ship-admin-result-move-failed");
            return false;
        }

        Stop(grid);
        Audit(admin, LogImpact.Medium, grid, "unstuck it (Stop Knotting)");
        result = Loc.GetString("wg-ship-admin-result-unstuck", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Releases every dock on the ship.</summary>
    public bool Undock(ICommonSession admin, EntityUid grid, out string result)
    {
        _docking.UndockDocks(grid);
        Audit(admin, LogImpact.Medium, grid, "undocked it");
        result = Loc.GetString("wg-ship-admin-result-undocked", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Brings a drifting or spinning ship to rest.</summary>
    public bool Halt(ICommonSession admin, EntityUid grid, out string result)
    {
        Stop(grid);
        Audit(admin, LogImpact.Low, grid, "halted it");
        result = Loc.GetString("wg-ship-admin-result-halted", ("ship", Name(grid)));
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
        result = Loc.GetString("wg-ship-admin-result-pilots-cleared", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Sets whether the ship travels along when a ship it is docked to jumps.</summary>
    public bool SetFtlLock(ICommonSession admin, EntityUid grid, bool enabled, out string result)
    {
        _shuttleConsoles.SetFTLLock(grid, enabled);
        Audit(admin, LogImpact.Low, grid, $"set its FTL lock to {enabled}");
        result = Loc.GetString(enabled ? "wg-ship-admin-result-ftl-lock-on" : "wg-ship-admin-result-ftl-lock-off", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Ends the wait after a jump, so the ship can jump again at once.</summary>
    public bool ResetFtl(ICommonSession admin, EntityUid grid, out string result)
    {
        if (!TryComp<FTLComponent>(grid, out var ftl) || ftl.State != FTLState.Cooldown)
        {
            result = Loc.GetString(ftl == null ? "wg-ship-admin-result-ftl-ready" : "wg-ship-admin-result-ftl-jumping");
            return false;
        }

        RemCompDeferred<FTLComponent>(grid);
        _shuttleConsoles.RefreshShuttleConsoles(grid);
        Audit(admin, LogImpact.Low, grid, "reset its FTL cooldown");
        result = Loc.GetString("wg-ship-admin-result-ftl-reset", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Whether the panel may move this grid: a ship, not a planet or a station, and not in the middle of a jump.</summary>
    private bool CanMove(EntityUid grid, out string result)
    {
        result = string.Empty;
        if (!CanBeOwned(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-move-station");
            return false;
        }

        if (TryComp<FTLComponent>(grid, out var ftl) && ftl.State != FTLState.Cooldown)
        {
            result = Loc.GetString("wg-ship-admin-result-ftl-jumping");
            return false;
        }

        // A hop to or from a planet lands the ship at its own recorded spot, whatever is done to it meanwhile.
        if (_hop.IsHopping(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-hopping");
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
