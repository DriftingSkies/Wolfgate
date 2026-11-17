using System.Linq;
using System.Numerics;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Server._WG.NpcCrew.Commands;

public sealed partial class WGCrewCommand
{
    [Dependency] private WGCrewSetupSystem _setup = default!;
    [Dependency] private WGCrewObjectiveSystem _objectives = default!;
    [Dependency] private AdminVesselSpawnSystem _vessels = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const string ArenaVessel = "WGDredger";
    private const string ArenaPrefix = "arena";
    private const float ArenaSpacing = 200f;

    /// <summary>
    /// wg_crew arena [vessel]: three crewed ships of one battlegroup beside the caller. The lead holds, one escorts it, one docks with it.
    /// wg_crew arena clear: removes the arena's crews and ships.
    /// </summary>
    private void Arena(IConsoleShell shell, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteLine(Help);
            return;
        }

        if (args.Length == 2 && args[1] == "clear")
        {
            ArenaClear(shell);
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } player
            || EntityManager.GetComponent<TransformComponent>(player).MapID is var map && map == MapId.Nullspace)
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-no-player"));
            return;
        }

        var id = args.Length == 2 ? args[1] : ArenaVessel;
        if (!_prototypes.TryIndex<VesselPrototype>(id, out var vessel) || vessel.Abstract)
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-arena-unknown-vessel", ("vessel", id)));
            return;
        }

        var query = EntityManager.EntityQueryEnumerator<WGCrewComponent>();
        while (query.MoveNext(out _, out var existing))
        {
            if (existing.Battlegroup.StartsWith(ArenaPrefix, StringComparison.Ordinal))
            {
                shell.WriteLine(Loc.GetString("cmd-wg_crew-arena-exists"));
                return;
            }
        }

        var origin = _transform.GetWorldPosition(player);
        var ships = new (string Group, Vector2 Offset, bool Captain)[]
        {
            ("arena-lead", new Vector2(0, ArenaSpacing), true),
            ("arena-escort", new Vector2(-ArenaSpacing, 0), false),
            ("arena-dock", new Vector2(ArenaSpacing, 0), false),
        };

        var spawned = new List<(EntityUid Grid, string Group)>();
        var ready = true;
        foreach (var (group, offset, captain) in ships)
        {
            if (!_vessels.TrySpawnVessel(vessel, map, origin + offset, player, out var grid))
            {
                shell.WriteError(Loc.GetString("cmd-wg_crew-arena-vessel-failed", ("vessel", id)));
                foreach (var (ship, shipGroup) in spawned)
                {
                    _setup.ClearCrew(ship, shipGroup);
                    EntityManager.QueueDeleteEntity(ship);
                }

                shell.WriteLine(Loc.GetString("cmd-wg_crew-arena-aborted", ("count", spawned.Count)));
                return;
            }

            spawned.Add((grid.Value, group));
            var mission = new WGCrewMission { Group = group, Callsign = group, Battlegroup = ArenaPrefix };
            if (!_setup.TrySpawn(grid.Value, _setup.Plan(grid.Value, 2, captain), mission, out var crew))
            {
                shell.WriteError(Loc.GetString("cmd-wg_crew-arena-crew-failed", ("group", group)));
                ready = false;
            }

            shell.WriteLine(Loc.GetString("cmd-wg_crew-arena-ship",
                ("group", group),
                ("grid", EntityManager.GetNetEntity(grid.Value)),
                ("count", crew.Count)));
        }

        var lead = EntityManager.GetNetEntity(spawned[0].Grid);
        var escorted = _objectives.SetQueue(spawned[1].Grid, ships[1].Group, new List<WGCrewObjective>
        {
            new() { Kind = WGCrewObjectiveKind.Escort, Target = lead },
        });
        if (!escorted)
            shell.WriteError(Loc.GetString("cmd-wg_crew-arena-queue-failed", ("group", ships[1].Group)));

        var docking = _objectives.SetQueue(spawned[2].Grid, ships[2].Group, new List<WGCrewObjective>
        {
            new() { Kind = WGCrewObjectiveKind.Dock, Target = lead },
        });
        if (!docking)
            shell.WriteError(Loc.GetString("cmd-wg_crew-arena-queue-failed", ("group", ships[2].Group)));

        if (ready && escorted && docking)
            shell.WriteLine(Loc.GetString("cmd-wg_crew-arena-done"));
    }

    /// <summary>wg_crew arena clear: deletes arena crews, cancels their queues and deletes the ships that hosted arena-* crews.</summary>
    private void ArenaClear(IConsoleShell shell)
    {
        var crews = new List<(EntityUid Grid, string Group)>();
        var ships = new HashSet<EntityUid>();
        var query = EntityManager.EntityQueryEnumerator<WGCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            var arenaShip = crew.Group.StartsWith(ArenaPrefix + "-", StringComparison.Ordinal);
            if (!arenaShip && !crew.Battlegroup.StartsWith(ArenaPrefix, StringComparison.Ordinal))
                continue;

            if (_setup.CrewGrid(uid, crew, xform) is not { } grid)
                continue;

            crews.Add((grid, crew.Group));
            if (arenaShip)
                ships.Add(grid);
        }

        if (crews.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_crew-arena-none"));
            return;
        }

        var removed = 0;
        foreach (var (grid, group) in crews.Distinct())
            removed += _setup.ClearCrew(grid, group);

        foreach (var ship in ships)
            EntityManager.QueueDeleteEntity(ship);

        shell.WriteLine(Loc.GetString("cmd-wg_crew-arena-cleared", ("crew", removed), ("ships", ships.Count)));
    }

    private IEnumerable<string> VesselIds()
    {
        return _prototypes.EnumeratePrototypes<VesselPrototype>()
            .Where(vessel => !vessel.Abstract)
            .Select(vessel => vessel.ID)
            .Order();
    }
}
