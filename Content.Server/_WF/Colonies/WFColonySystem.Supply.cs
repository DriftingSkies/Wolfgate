using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.Planets;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Colonies;

/// <summary>
/// Supply runs: a colony's imports come on a freighter. It flies in from the sector, holds over the world outside its
/// gravity well and sends what the colony is owed down a pod at a time, then flies off. A run that never arrives brings
/// nothing, and the next one is slower to set out.
/// </summary>
public sealed partial class WFColonySystem
{
    [Dependency] private WFEncounterSystem _encounters = default!;

    /// <summary>The encounter a supply run flies as.</summary>
    public static readonly ProtoId<WFEncounterPrototype> SupplyRun = "WFEncounterColonySupply";

    /// <summary>Metres past a world's gravity well a run holds at, and how close to that point counts as there.</summary>
    private const float SupplyStandoff = 250f;
    private const float SupplyReach = 400f;

    /// <summary>The share of its orbit range a world's gravity well reaches to, as the encounters place by.</summary>
    private const float WellMargin = 1.25f;

    private static readonly TimeSpan PodInterval = TimeSpan.FromSeconds(30);

    /// <summary>Whether a colony's imports can come by supply run: runs on, encounters on, and its body on the sector map.</summary>
    private bool CanFlySupply(Entity<WFColonyComponent> colony)
    {
        return _cfg.GetCVar(ColonyCVars.SupplyRuns) && _cfg.GetCVar(EncountersCVars.Enabled)
            && HasComp<WFSectorPlanetComponent>(colony) && Transform(colony).MapID == _ticker.DefaultMap;
    }

    /// <summary>Sends a colony's next run when it is due, and brings the pods down from one holding over the world.</summary>
    private void TendSupplyRun(Entity<WFColonyComponent> colony, List<OutpostEntry> entries)
    {
        var now = _timing.CurTime;
        if (colony.Comp.Run is not { } uid)
        {
            if (now >= colony.Comp.NextRun && !colony.Comp.Blockaded && ImportHub(colony, entries, out _) != null)
                TryLaunchSupplyRun(colony);
            return;
        }

        if (!TryComp<WFColonySupplyRunComponent>(uid, out var run) || !TryComp<WFEncounterComponent>(uid, out var encounter)
            || !encounter.Ships.TryGetValue(run.Lead, out var ship) || TerminatingOrDeleted(ship.Grid)
            || encounter.Resolution is { } ended && ended != WFEncounterResolution.Completed)
        {
            LoseSupplyRun(colony);
            return;
        }

        if (!run.Arrived)
        {
            if (encounter.Resolution != null)
            {
                LoseSupplyRun(colony);
                return;
            }

            if ((_transform.GetWorldPosition(ship.Grid) - run.Station).Length() > SupplyReach)
                return;

            run.Arrived = true;
            run.NextPod = now;
            _market.Radio(Loc.GetString("wf-colony-run-arrived", ("ship", Name(ship.Grid)), ("world", Name(colony))));
        }

        if (now < run.NextPod || colony.Comp.Blockaded)
            return;

        run.NextPod = now + PodInterval;
        // It sends pods down while it holds there with something aboard the colony is owed; with no hub ready they wait.
        if (encounter.Resolution == null && run.Hold >= 1f && _market.Owed(colony) >= 1f)
        {
            run.Hold -= DropImports(colony, entries, MathF.Min(run.Hold, _cfg.GetCVar(ColonyCVars.ImportCrates)));
            return;
        }

        colony.Comp.Run = null;
        colony.Comp.NextRun = now + TimeSpan.FromSeconds(_cfg.GetCVar(ColonyCVars.SupplyInterval));
    }

    /// <summary>A run that was stopped on its way leaves the colony waiting twice as long for the next.</summary>
    private void LoseSupplyRun(Entity<WFColonyComponent> colony)
    {
        colony.Comp.Run = null;
        colony.Comp.NextRun = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(ColonyCVars.SupplyInterval) * 2);
        _market.Radio(Loc.GetString("wf-colony-run-lost", ("world", Name(colony))));
    }

    /// <summary>
    /// Sends a freighter for what a colony is owed: it appears out in the sector, flies to a point over the world past
    /// its gravity well, holds there and flies back out.
    /// </summary>
    public bool TryLaunchSupplyRun(Entity<WFColonyComponent> colony)
    {
        var owed = _market.Owed(colony);
        if (owed < _cfg.GetCVar(ColonyCVars.SupplyMinimum) || !_proto.TryIndex(SupplyRun, out var prototype) || prototype.Ships.Count == 0)
            return false;

        var range = 2000f;
        if (TryComp<WFSectorPlanetComponent>(colony, out var planet) && TryGetEntity(planet.OrbitMap, out var orbit)
            && TryComp<WFOrbitLayerComponent>(orbit, out var layer))
            range = layer.Range;

        var centre = _transform.GetWorldPosition(colony);
        var bearing = _random.NextAngle().ToVec();
        var station = centre + bearing * (range * WellMargin + SupplyStandoff);
        var start = station + bearing * _random.NextFloat(2500f, 3500f);
        var map = Transform(colony).MapID;
        if (!_encounters.TrySpawn(prototype, new MapCoordinates(start, map), out var uid, offBudget: true))
            return false;

        var lead = prototype.Ships[0].Key;
        var orders = new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = station, Range = SupplyReach / 2f },
            new() { Kind = WFCrewObjectiveKind.Hold, Duration = _cfg.GetCVar(ColonyCVars.SupplyHold) },
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = start, Range = SupplyReach },
        };
        if (!_encounters.SetOrders(uid, lead, orders))
        {
            _encounters.End(uid);
            return false;
        }

        var run = AddComp<WFColonySupplyRunComponent>(uid);
        run.World = colony.Comp.World;
        run.Lead = lead;
        run.Station = station;
        run.Hold = MathF.Min(owed, _cfg.GetCVar(ColonyCVars.SupplyCrates));
        colony.Comp.Run = uid;
        _market.Radio(Loc.GetString("wf-colony-run-launched", ("world", Name(colony)), ("crates", (int) run.Hold)));
        _log.Info($"Supply run {ToPrettyString(uid)} set out for {colony.Comp.World} with {run.Hold:F0} crates.");
        return true;
    }
}
