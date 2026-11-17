using Content.Server._Crescent.Hardpoint;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.ShipRepair;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.Drydock;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private FireControlSystem _fireControl = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private GridPowerSystem _gridPower = default!;
    [Dependency] private HardpointSystem _hardpoints = default!;
    [Dependency] private IConsoleHost _console = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private ShipRepairSystem _shipRepair = default!;
    [Dependency] private ShuttleConsoleSystem _shuttleConsoles = default!;
    [Dependency] private WGCrewWorkSystem _crewWork = default!;
    [Dependency] private WGDrydockSystem _drydock = default!;

    /// <summary>How many hull tiles, parts and heals one fix applies in a tick, so a wrecked capital ship doesn't stall the server.</summary>
    public const int FixItemsPerTick = 150;

    private readonly Dictionary<EntityUid, FixJob> _fixes = new();

    /// <summary>A fix under way on one ship: the hull and damage work still queued, then the parts that take one call each.</summary>
    private sealed class FixJob
    {
        public ICommonSession Admin = default!;
        public WGShipAdminFixParts Parts;
        public Queue<WGDrydockWorkItem> Items = new();
        public int Total;
        public int Tiles;
        public int Rebuilt;
        public int Healed;

        /// <summary>The refit line and tier this job brings the ship up to, when it is a refit and not a fix.</summary>
        public WGShipRefitPrototype? Refit;
        public int RefitTier;
    }

    /// <summary>Whether a fix is running on the ship, and how far it is.</summary>
    public bool TryGetFix(EntityUid grid, out int done, out int total)
    {
        done = 0;
        total = 0;
        if (!_fixes.TryGetValue(grid, out var job))
            return false;

        total = job.Total;
        done = job.Total - job.Items.Count;
        return true;
    }

    /// <summary>What a fix would restore: hull tiles gone, parts gone and parts damaged.</summary>
    public WGShipAdminDamage Survey(EntityUid grid)
    {
        int tiles = 0, parts = 0, damaged = 0;
        foreach (var item in _drydock.BuildFreeRepairItems(grid))
        {
            switch (item.Kind)
            {
                case WGDrydockWorkKind.Tile:
                    tiles++;
                    break;
                case WGDrydockWorkKind.Entity:
                    parts++;
                    break;
                case WGDrydockWorkKind.Heal:
                    damaged++;
                    break;
            }
        }

        return new WGShipAdminDamage(tiles, parts, damaged);
    }

    /// <summary>
    /// Starts a fix. It never deletes anything a player moved or rebuilt: hull and parts come back only where the
    /// repair snapshot says something is missing. The work is spread over ticks and ends with a result in the
    /// admin's panel.
    /// </summary>
    public bool StartFix(ICommonSession admin, EntityUid grid, WGShipAdminFixParts parts, out string result)
    {
        parts &= WGShipAdminFixParts.All;
        if (parts == WGShipAdminFixParts.None)
        {
            result = Loc.GetString("wg-ship-admin-result-fix-nothing");
            return false;
        }

        if (_fixes.ContainsKey(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-fix-running");
            return false;
        }

        var job = new FixJob { Admin = admin, Parts = parts };
        var hull = (parts & WGShipAdminFixParts.Hull) != 0;
        var heal = (parts & WGShipAdminFixParts.Structures) != 0;
        if (hull || heal)
        {
            var mounted = new List<WGDrydockWorkItem>();
            foreach (var item in _drydock.BuildFreeRepairItems(grid))
            {
                var wanted = item.Kind == WGDrydockWorkKind.Heal ? heal : hull;
                if (!wanted)
                    continue;

                // Guns and thrusters mount on parts that may be in this same list, so they are built last.
                if (item.Mounted)
                    mounted.Add(item);
                else
                    job.Items.Enqueue(item);
            }

            foreach (var item in mounted)
                job.Items.Enqueue(item);
        }

        job.Total = job.Items.Count;
        _fixes[grid] = job;
        Audit(admin, LogImpact.Medium, grid, $"started a fix of {parts} ({job.Total} hull and damage items)");

        result = Loc.GetString("wg-ship-admin-result-fix-started", ("ship", Name(grid)), ("count", job.Total));
        if (hull && !HasComp<ShipRepairDataComponent>(grid))
            result = $"{result} {Loc.GetString("wg-ship-admin-note-no-snapshot")}";

        return true;
    }

    /// <summary>Replaces the ship's repair snapshot with how it stands now.</summary>
    public bool Snapshot(ICommonSession admin, EntityUid grid, out string result)
    {
        // A running fix holds keys into the snapshot it was built from; a new one would point them at other parts.
        if (_fixes.ContainsKey(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-fix-running");
            return false;
        }

        _shipRepair.GenerateRepairData(grid);
        Audit(admin, LogImpact.Medium, grid, "re-took its repair snapshot");
        result = Loc.GetString("wg-ship-admin-result-snapshot", ("ship", Name(grid)));
        return true;
    }

    /// <summary>
    /// Puts the ship back as its repair snapshot has it. Unlike a fix this deletes and rebuilds every part that
    /// moved since the snapshot, and re-lays every hull tile.
    /// </summary>
    public bool Restore(ICommonSession admin, EntityUid grid, out string result)
    {
        if (!HasComp<ShipRepairDataComponent>(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-no-snapshot");
            return false;
        }

        if (_fixes.ContainsKey(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-fix-running");
            return false;
        }

        // Mono keeps this as the body of its repairgrid command, which takes the grid's own id.
        _shipRepair.RepairGridCmd(_console.LocalShell, string.Empty, new[] { grid.ToString() });
        Audit(admin, LogImpact.High, grid, "restored it to its repair snapshot");
        result = Loc.GetString("wg-ship-admin-result-restored", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Works through the running fixes, a budget of hull and damage items a tick each.</summary>
    private void UpdateFixes()
    {
        if (_fixes.Count == 0)
            return;

        List<EntityUid>? finished = null;
        foreach (var (grid, job) in _fixes)
        {
            if (TerminatingOrDeleted(grid))
            {
                (finished ??= new List<EntityUid>()).Add(grid);
                continue;
            }

            for (var i = 0; i < FixItemsPerTick && job.Items.TryDequeue(out var item); i++)
            {
                // A part that left the ship since it was queued, as on a piece that broke off, is no longer this ship's.
                if (item.Kind is WGDrydockWorkKind.Heal or WGDrydockWorkKind.Upgrade
                    && (!TryComp(item.Target, out TransformComponent? xform) || xform.GridUid != grid))
                    continue;

                // So is a floor a player laid there since: the hull under it is no longer missing.
                if (item.Kind == WGDrydockWorkKind.Tile && !_drydock.IsHullMissing(grid, item))
                    continue;

                _drydock.ApplyItem(grid, item);
                switch (item.Kind)
                {
                    case WGDrydockWorkKind.Tile:
                        job.Tiles++;
                        break;
                    case WGDrydockWorkKind.Entity:
                        job.Rebuilt++;
                        break;
                    case WGDrydockWorkKind.Heal:
                        job.Healed++;
                        break;
                }
            }

            if (job.Items.Count > 0)
                continue;

            FinishFix(grid, job);
            (finished ??= new List<EntityUid>()).Add(grid);
        }

        if (finished == null)
            return;

        foreach (var grid in finished)
            _fixes.Remove(grid);
    }

    /// <summary>The parts of a fix that take one call each, run once the hull is whole again.</summary>
    private void FinishFix(EntityUid grid, FixJob job)
    {
        if (job.Refit is { } refit)
        {
            // The ship owns the tier now, for as many units as reach it; a later repair restores them at that tier.
            _refits.SetRefit(grid, refit.ID, job.RefitTier, _refits.Survey(grid, refit, job.RefitTier).UnitsAtTier);
            Audit(job.Admin, LogImpact.Medium, grid, $"finished a free refit of {refit.ID} to tier {job.RefitTier} ({job.Total} parts)");
            if (_panels.TryGetValue(job.Admin, out var refitPanel))
            {
                refitPanel.Finish(true, Loc.GetString("wg-ship-admin-result-refitted",
                    ("ship", Name(grid)),
                    ("refit", Loc.GetString(refit.Name)),
                    ("tier", job.RefitTier)));
            }

            return;
        }

        var parts = job.Parts;
        if ((parts & WGShipAdminFixParts.Fires) != 0)
        {
            var burning = EntityQueryEnumerator<FlammableComponent, TransformComponent>();
            while (burning.MoveNext(out var uid, out var flammable, out var xform))
            {
                if (xform.GridUid == grid && flammable.OnFire)
                    _flammable.Extinguish(uid, flammable);
            }
        }

        if ((parts & WGShipAdminFixParts.Air) != 0)
            _atmos.WgFixGridAtmos(grid);

        if ((parts & WGShipAdminFixParts.Power) != 0)
            _gridPower.SetPower(true, grid, false);

        if ((parts & WGShipAdminFixParts.Fuel) != 0)
            _crewWork.Refuel(grid);

        if ((parts & WGShipAdminFixParts.Bolts) != 0)
        {
            var bolts = EntityQueryEnumerator<DoorBoltComponent, TransformComponent>();
            while (bolts.MoveNext(out var uid, out var bolt, out var xform))
            {
                // A door the owner sealed is bolted on purpose.
                if (xform.GridUid == grid && bolt.BoltsDown && _accessRules.GetRule(uid) != WGDoorAccessRule.Sealed)
                    _doors.SetBoltsDown((uid, bolt), false);
            }
        }

        if ((parts & WGShipAdminFixParts.Consoles) != 0)
        {
            var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
            while (consoles.MoveNext(out _, out var console, out var xform))
            {
                if (xform.GridUid == grid)
                    _shuttleConsoles.ClearPilots(console);
            }

            _shuttleConsoles.RefreshShuttleConsoles(grid);
            _fireControl.ForceServerReconnectionOnGrid(grid);
            _hardpoints.QueueHardpointRefresh(grid);
        }

        Audit(job.Admin, LogImpact.Medium, grid,
            $"finished a fix of {parts}: {job.Tiles} hull tiles, {job.Rebuilt} parts rebuilt, {job.Healed} healed");

        if (_panels.TryGetValue(job.Admin, out var panel))
        {
            panel.Finish(true, Loc.GetString("wg-ship-admin-result-fixed",
                ("ship", Name(grid)),
                ("tiles", job.Tiles),
                ("parts", job.Rebuilt),
                ("healed", job.Healed)));
        }
    }
}
