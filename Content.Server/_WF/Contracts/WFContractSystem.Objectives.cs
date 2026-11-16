using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Systems;
using Content.Server.Anomaly.Components;
using Content.Server.Storage.Components;
using Content.Shared._WF.Contracts;
using Content.Shared._WF.Encounters;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Anomaly;
using Content.Shared.Anomaly.Components;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Stacks;
using Content.Shared.Storage.Components;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Contracts;

/// <summary>Rolling, starting, checking and clearing up each kind of objective.</summary>
public sealed partial class WFContractSystem
{
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private Content.Server._WF.Lattice.WFLatticeSystem _lattice = default!;
    [Dependency] private SharedAnomalySystem _anomalies = default!;
    [Dependency] private Content.Shared.Storage.EntitySystems.SharedEntityStorageSystem _entityStorage = default!;

    /// <summary>How close a player must be to a wreck to keep it in the world once its contract ends.</summary>
    private const float WreckKeepRange = 300f;

    /// <summary>
    /// Tries at finding a site: the first half in the contract's own band, then each with the band widened by
    /// <see cref="SiteWidening"/>.
    /// </summary>
    private const int SitePasses = 8;

    private const float SiteWidening = 1.3f;

    /// <summary>Open space a bounty or wreck needs round it.</summary>
    private const float SiteClearance = 250f;

    /// <summary>How far a site must be from every player, past the range any bounty target warns or opens fire at.</summary>
    public const float PlayerClearance = 2000f;

    /// <summary>How far an escort's rendezvous must be from any station.</summary>
    private const float EscortClearance = 800f;

    /// <summary>How far every space site keeps from a reality anchor, so a fight or a wreck never reaches one.</summary>
    public const float AnchorClearance = 2000f;

    /// <summary>Highest severity a survey's anomaly starts at, so it does not go critical before the survey team arrives.</summary>
    private const float SurveyStartSeverity = 0.3f;

    private void InitializeObjectives()
    {
        SubscribeLocalEvent<WFContractItemComponent, ComponentShutdown>(OnItemShutdown);
        SubscribeLocalEvent<WFContractItemComponent, ExaminedEvent>(OnItemExamined);
        SubscribeLocalEvent<WFContractItemComponent, StorageAfterCloseEvent>(OnCrateClosed);
        SubscribeLocalEvent<WFEncounterResolvedEvent>(OnEncounterResolved);
        InitializeOperations();
    }

    /// <summary>Fixes an offer's ends, counts and pay from its template. False if the round cannot give it.</summary>
    private bool TryRoll(WFContract contract, WFContractPrototype template, Dictionary<string, EntityUid> homes, EntityUid board)
    {
        var boardTag = _proto.Index(template.Contractor).Board;
        switch (template.Objective)
        {
            case WFContractCourier courier:
            {
                if (EndStation(courier.From, contract.Home, board, courier.Destinations, homes, null, boardTag) is not { } from
                    || EndStation(courier.To, contract.Home, board, courier.Destinations, homes, from, boardTag) is not { } to
                    || from == to)
                    return false;

                contract.Origin = courier.From == WFContractEnd.Board ? null : from;
                contract.Destination = to;
                contract.Count = _random.Next(courier.Count.X, Math.Max(courier.Count.X, courier.Count.Y) + 1);
                contract.Distance = Distance(from, to);
                contract.Reward += courier.PerPackage * contract.Count + DistancePay(courier.PerKilometre, contract.Distance);
                return true;
            }
            case WFContractSupply supply:
            {
                if (supply.Goods.Count == 0)
                    return false;

                contract.Destination = contract.Home;
                var lines = Enumerable.Range(0, supply.Goods.Count).ToList();
                _random.Shuffle(lines);
                if (supply.Pick > 0)
                    lines = lines.Take(supply.Pick).OrderBy(i => i).ToList();
                else
                    lines.Sort();

                var value = 0.0;
                foreach (var line in lines)
                {
                    var goods = supply.Goods[line];
                    var count = _random.Next(goods.Count.X, Math.Max(goods.Count.X, goods.Count.Y) + 1);
                    contract.Goods.Add((line, count));
                    value += UnitValue(goods) * count;
                }

                contract.Distance = Distance(board, contract.Home);
                contract.Reward += (int) Math.Min(value * supply.Premium, supply.GoodsCap ?? template.Reward.Y)
                                   + DistancePay(supply.PerKilometre, contract.Distance);
                return true;
            }
            case WFContractBounty bounty:
            {
                var options = bounty.Encounters.Where(id => _proto.HasIndex(id)).ToList();
                if (options.Count == 0)
                    return false;

                if (!SiteAvailable(contract.Home, bounty.MinDistance, bounty.MaxDistance, bounty.StationClearance))
                    return false;

                contract.Encounter = _random.Pick(options);
                contract.Distance = (bounty.MinDistance + bounty.MaxDistance) / 2f;
                return true;
            }
            case WFContractRecovery recovery:
            {
                if (!_proto.HasIndex(recovery.Item) || !RollWreck(contract, recovery.Wrecks, recovery.MinDistance, recovery.MaxDistance, recovery.StationClearance))
                    return false;

                contract.Count = _random.Next(recovery.Count.X, Math.Max(recovery.Count.X, recovery.Count.Y) + 1);
                contract.Destination = recovery.To == WFContractEnd.Board ? board : contract.Home;
                contract.Distance = (recovery.MinDistance + recovery.MaxDistance) / 2f;
                return true;
            }
            case WFContractTow tow:
                contract.Destination = contract.Home;
                return RollWreck(contract, tow.Wrecks, tow.MinDistance, tow.MaxDistance, tow.StationClearance);
            case WFContractSurvey survey:
            {
                if (!survey.Anomalies.Any(id => _proto.HasIndex(id)) || !RollWreck(contract, survey.Wrecks, survey.MinDistance, survey.MaxDistance, survey.StationClearance))
                    return false;

                contract.Count = _random.Next(survey.Count.X, Math.Max(survey.Count.X, survey.Count.Y) + 1);
                contract.Reward += survey.PerAnomaly * contract.Count;
                return true;
            }
            case WFContractEscort escort:
            {
                var options = escort.Encounters.Where(id => _proto.HasIndex(id)).ToList();
                if (options.Count == 0
                    || EndStation(WFContractEnd.Other, contract.Home, board, escort.Destinations, homes, null, boardTag) is not { } to)
                    return false;

                contract.Encounter = _random.Pick(options);
                contract.Destination = to;
                contract.Distance = Distance(contract.Home, to);
                contract.Reward += DistancePay(escort.PerKilometre, contract.Distance);
                return true;
            }
        }

        if (TryRollOperation(contract, template, homes, board, boardTag) is { } rolled)
            return rolled;

        Log.Error($"Contract {template.ID} has an objective the board cannot run: {template.Objective.GetType().Name}.");
        return false;
    }

    /// <summary>Kilometres past this are not paid: every station is within about 12 km of Caelestinus.</summary>
    public const float MaxPaidKilometres = 12f;

    /// <summary>Pay for a run's distance, up to <see cref="MaxPaidKilometres"/>.</summary>
    public static int DistancePay(int perKilometre, float metres) =>
        (int) (perKilometre * Math.Min(metres / 1000f, MaxPaidKilometres));

    /// <summary>The station one end of a run is at; <paramref name="not"/> keeps the far end off the near one.</summary>
    private EntityUid? EndStation(WFContractEnd end, EntityUid home, EntityUid board, List<ProtoId<WFContractorPrototype>> others,
        Dictionary<string, EntityUid> homes, EntityUid? not, string boardTag)
    {
        switch (end)
        {
            case WFContractEnd.Contractor:
                return home;
            case WFContractEnd.Board:
                return board;
        }

        var candidates = (others.Count == 0 ? homes.Values : others.Where(id => homes.ContainsKey(id)).Select(id => homes[id]))
            .Where(station => station != home && station != not && OpenTo(station, boardTag, homes))
            .Distinct()
            .ToList();
        return candidates.Count == 0 ? null : _random.Pick(candidates);
    }

    /// <summary>Whether a station may end a run posted on a board: faction-only stations (Helios) take their own board's work alone.</summary>
    private bool OpenTo(EntityUid station, string boardTag, Dictionary<string, EntityUid> homes)
    {
        var boards = homes.Where(h => h.Value == station).Select(h => _proto.Index<WFContractorPrototype>(h.Key).Board).ToHashSet();
        return boards.Contains(WFContractBoards.Central) || boards.Contains(boardTag);
    }

    /// <summary>Market value of one of a supply line's goods.</summary>
    public double UnitValue(WFContractGoods goods)
    {
        if (goods.Value is { } value)
            return value;

        if (goods.Stack is { } stack && _proto.TryIndex(stack, out var stackProto)
            && _proto.TryIndex(stackProto.Spawn, out var single))
            return _pricing.GetEstimatedPrice(single);

        if (goods.Entity is { } entity && _proto.TryIndex(entity, out var entityProto))
            return _pricing.GetEstimatedPrice(entityProto);

        return 0;
    }

    /// <summary>What a supply line is called on the manifest.</summary>
    public string GoodsName(WFContractGoods goods)
    {
        if (goods.Name is { } name)
            return Loc.GetString(name);

        if (goods.Stack is { } stack && _proto.TryIndex(stack, out var stackProto))
            return Loc.GetString(stackProto.Name);

        if (goods.Entity is { } entity && _proto.TryIndex(entity, out var entityProto))
            return entityProto.Name;

        return goods.Tag?.Id ?? goods.Component ?? "?";
    }

    /// <summary>How many of a supply line an entity counts for: its stack size, or one.</summary>
    private int Matches(WFContractGoods goods, EntityUid uid)
    {
        if (goods.Stack is { } stack)
            return TryComp<StackComponent>(uid, out var stackComp) && stackComp.StackTypeId == stack ? stackComp.Count : 0;

        if (goods.Entity is { } entity)
        {
            if (MetaData(uid).EntityPrototype is not { } proto)
                return 0;

            if (proto.ID != entity && !_proto.EnumerateParents<EntityPrototype>(proto.ID).Any(parent => parent.ID == entity))
                return 0;

            return TryComp<StackComponent>(uid, out var count) ? count.Count : 1;
        }

        if (goods.Tag is { } tag && _tags.HasTag(uid, tag))
            return TryComp<StackComponent>(uid, out var count) ? count.Count : 1;

        if (goods.Component is { } component && _factory.TryGetRegistration(component, out var registration)
            && HasComp(uid, registration.Type))
            return TryComp<StackComponent>(uid, out var count) ? count.Count : 1;

        // Full only, so a magazine stripped to one round isn't worth a full one.
        if (goods.Ammo && TryComp<BallisticAmmoProviderComponent>(uid, out var ammo) && ammo.Count >= ammo.Capacity
            && !HasComp<GunComponent>(uid))
            return 1;

        return 0;
    }

    /// <summary>How much of each supply line a crate holds, in manifest order. An entity counts for the first line it fits.</summary>
    public List<int> Packed(WFContract contract, EntityUid crate)
    {
        var have = new List<int>(contract.Goods.Select(_ => 0));
        if (_proto.Index(contract.Prototype).Objective is not WFContractSupply supply
            || !TryComp<EntityStorageComponent>(crate, out var storage))
            return have;

        foreach (var uid in storage.Contents.ContainedEntities)
        {
            for (var i = 0; i < contract.Goods.Count; i++)
            {
                var count = Matches(supply.Goods[contract.Goods[i].Line], uid);
                if (count <= 0)
                    continue;

                have[i] += count;
                break;
            }
        }

        return have;
    }

    /// <summary>Puts a newly accepted contract's cargo or target in the world. False, leaving nothing behind, if it cannot.</summary>
    private bool TryStart(WFContract contract, EntityUid board)
    {
        var proto = _proto.Index(contract.Prototype);
        switch (proto.Objective)
        {
            case WFContractCourier courier:
            {
                // Parcels share a tile; each freight crate takes the next clear one.
                for (var i = 0; i < contract.Count; i++)
                {
                    if ((contract.Origin is { } origin ? Anchor(origin) : BoardDrop(board)) is not { } coords)
                        return false;

                    var package = Spawn(courier.Package, coords);
                    Track(contract, package);
                    _meta.SetEntityName(package, Loc.GetString("wf-contract-package-name", ("name", Name(package)),
                        ("destination", StationName(contract.Destination))));
                }

                return true;
            }
            case WFContractSupply supply:
            {
                if (BoardDrop(board) is not { } coords)
                    return false;

                var crate = Spawn(supply.Crate, coords);
                Track(contract, crate);
                _meta.SetEntityName(crate, Loc.GetString("wf-contract-crate-name", ("id", contract.Id),
                    ("destination", StationName(contract.Destination))));
                return true;
            }
            case WFContractBounty bounty:
            {
                if (contract.Encounter is not { } encounterId || !_proto.TryIndex(encounterId, out var encounterProto))
                    return false;

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    if (PickSite(contract.Home, bounty.MinDistance, bounty.MaxDistance, bounty.StationClearance) is not { } site)
                        continue;

                    if (!_encounters.TrySpawn(encounterProto, site, out var encounter, offBudget: true, tier: proto.Tier))
                    {
                        Log.Warning($"Bounty contract {contract.Id} could not start {encounterId} at {site}.");
                        continue;
                    }

                    contract.Site = site;
                    contract.Distance = SiteDistance(contract.Home, site);
                    contract.EncounterEntity = encounter;
                    return true;
                }

                return false;
            }
            case WFContractRecovery recovery:
            {
                if (LoadWreck(contract, recovery.MinDistance, recovery.MaxDistance, recovery.StationClearance) is not { } grid)
                    return false;

                for (var i = 0; i < Math.Max(1, contract.Count) && FreeTile(grid, contract.SiteBounds) is { } tile; i++)
                {
                    Track(contract, Spawn(recovery.Item, tile));
                }

                if (contract.Items.Count == 0)
                {
                    DropWreck(contract);
                    return false;
                }

                contract.Count = contract.Items.Count;
                Infest(contract, grid, recovery.Infestation);
                return true;
            }
            case WFContractTow tow:
            {
                if (LoadWreck(contract, tow.MinDistance, tow.MaxDistance, tow.StationClearance) is not { } grid)
                    return false;

                Infest(contract, grid, tow.Infestation);
                return true;
            }
            case WFContractSurvey survey:
            {
                if (LoadWreck(contract, survey.MinDistance, survey.MaxDistance, survey.StationClearance) is not { } grid)
                    return false;

                var grids = contract.PlanetSite != null ? new List<Entity<MapGridComponent>> { grid } : LoadTwins(contract, grid, survey.Derelicts);
                PlaceAnomalies(contract, grids, survey.Anomalies);
                if (contract.Anomalies.Count < contract.Count)
                {
                    DropWreck(contract);
                    return false;
                }

                foreach (var derelict in grids)
                {
                    Infest(contract, derelict, survey.Infestation);
                }

                return true;
            }
            case WFContractEscort escort:
            {
                if (contract.Encounter is not { } encounterId || !_proto.TryIndex(encounterId, out var encounterProto)
                    || contract.Destination is not { } destination
                    // Stops must be grids: a station entity fails the orders and the encounter removes itself.
                    || _station.GetLargestGrid(destination) is not { } stop)
                    return false;

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    if (PickSite(contract.Home, escort.MinDistance, escort.MaxDistance, EscortClearance) is not { } site
                        || !_encounters.TrySpawn(encounterProto, site, out var encounter, stops: new[] { stop }, offBudget: true,
                            tier: proto.Tier))
                        continue;

                    contract.Site = site;
                    contract.EncounterEntity = encounter;
                    return true;
                }

                return false;
            }
        }

        return TryStartOperation(contract, proto.Objective, board) ?? false;
    }

    private void Track(WFContract contract, EntityUid item)
    {
        EnsureComp<WFContractItemComponent>(item).Contract = contract.Id;
        contract.Items.Add(item);
    }

    /// <summary>Loads a contract's wreck in open space near its contractor, named for the contract.</summary>
    private Entity<MapGridComponent>? LoadWreck(WFContract contract, float min, float max, float clearance)
    {
        var proto = _proto.Index(contract.Prototype);
        if (proto.Objective.Planet is { } planet)
            return ClaimSite(contract, planet, proto.Tier);

        if (contract.Wreck is not { } wreck || PickSite(contract.Home, min, max, clearance) is not { } site
            || !_mapLoader.TryLoadGrid(site.MapId, wreck, out var grid, offset: site.Position, rot: _random.NextAngle()))
            return null;

        contract.Site = site;
        contract.WreckGrid = grid.Value.Owner;
        _meta.SetEntityName(grid.Value.Owner, Loc.GetString("wf-contract-wreck-name", ("id", contract.Id)));
        return grid.Value;
    }

    /// <summary>Deletes a wreck loaded for a contract that could not start.</summary>
    private void DropWreck(WFContract contract)
    {
        ReleaseSite(contract);
        if (contract.WreckGrid is { } wreck)
            QueueDel(wreck);

        foreach (var twin in contract.ExtraWrecks)
        {
            QueueDel(twin);
        }

        contract.WreckGrid = null;
        contract.ExtraWrecks.Clear();
        contract.Anomalies.Clear();
        contract.Targets.Clear();
    }

    /// <summary>Places hostile mobs on free tiles of a wreck or planet site.</summary>
    private void Infest(WFContract contract, Entity<MapGridComponent> grid, List<WFContractSpawn> infestation)
    {
        foreach (var spawn in infestation)
        {
            var count = _random.Next(spawn.Count.X, Math.Max(spawn.Count.X, spawn.Count.Y) + 1);
            for (var i = 0; i < count; i++)
            {
                if (contract.PlanetSite != null)
                    PlaceNative(contract, grid, spawn.Entity);
                else if (FreeTile(grid) is { } tile)
                    Spawn(spawn.Entity, tile);
            }
        }
    }

    /// <summary>Metres between a towed derelict's hull and its destination station's largest grid, or null if either is gone.</summary>
    public float? TowGap(WFContract contract)
    {
        if (contract.WreckGrid is not { } wreck || TerminatingOrDeleted(wreck) || !TryComp<MapGridComponent>(wreck, out var wreckGrid)
            || contract.Destination is not { } destination || _station.GetLargestGrid(destination) is not { } station
            || !TryComp<MapGridComponent>(station, out var stationGrid)
            || Transform(wreck).MapID != Transform(station).MapID)
            return null;

        var a = _transform.GetWorldMatrix(wreck).TransformBox(wreckGrid.LocalAABB);
        var b = _transform.GetWorldMatrix(station).TransformBox(stationGrid.LocalAABB);
        var dx = Math.Max(0f, Math.Max(a.Left - b.Right, b.Left - a.Right));
        var dy = Math.Max(0f, Math.Max(a.Bottom - b.Top, b.Bottom - a.Top));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Records survey anomalies any anomaly scanner has scanned, telling the holder of each new one.</summary>
    private void UpdateSurvey(WFContract contract)
    {
        var scanners = EntityQueryEnumerator<AnomalyScannerComponent>();
        while (scanners.MoveNext(out _, out var scanner))
        {
            if (scanner.ScannedAnomaly is not { } anomaly || !contract.Anomalies.Contains(anomaly) || !contract.Scanned.Add(anomaly))
                continue;

            _dirty = true;
            if (contract.Scanned.Count < contract.Count && contract.User is { } user)
            {
                Tell(new WFContractHolder(user, contract.Character), Loc.GetString("wf-contract-anomaly-surveyed",
                    ("title", Title(contract)), ("done", contract.Scanned.Count), ("count", contract.Count)));
            }
        }
    }

    /// <summary>Checks a live contract's objective and completes it once done.</summary>
    private void CheckObjective(WFContract contract)
    {
        var proto = _proto.Index(contract.Prototype);
        switch (proto.Objective)
        {
            case WFContractCourier:
                foreach (var item in contract.Items)
                {
                    if (TerminatingOrDeleted(item) || !TryComp<WFContractItemComponent>(item, out var comp) || comp.Delivered
                        || !OnStation(item, contract.Destination))
                        continue;

                    comp.Delivered = true;
                    contract.Delivered++;
                    QueueDel(item);
                    _dirty = true;
                    if (contract.Delivered < contract.Count && contract.User is { } user)
                    {
                        Tell(new WFContractHolder(user, contract.Character), Loc.GetString("wf-contract-package-delivered",
                            ("title", Title(contract)), ("done", contract.Delivered), ("count", contract.Count)));
                    }
                }

                if (contract.Delivered >= contract.Count)
                    Complete(contract);
                break;
            case WFContractSupply:
                if (contract.Items.FirstOrDefault() is { Valid: true } crate && Delivers(contract, crate))
                    Complete(contract);
                break;
            case WFContractBounty:
                if (contract.EncounterEntity is not { } encounter || TerminatingOrDeleted(encounter))
                    Void(contract, "wf-contract-reason-target-gone");
                break;
            case WFContractRecovery:
                foreach (var recovered in contract.Items)
                {
                    if (TerminatingOrDeleted(recovered) || !TryComp<WFContractItemComponent>(recovered, out var comp) || comp.Delivered
                        || !OnStation(recovered, contract.Destination))
                        continue;

                    // Kept, not deleted, so an artifact's nodes can still be counted at completion.
                    comp.Delivered = true;
                    contract.Delivered++;
                    _dirty = true;
                    if (contract.Delivered < contract.Items.Count && contract.User is { } user)
                        Tell(new WFContractHolder(user, contract.Character), Loc.GetString("wf-contract-recovered-one",
                            ("title", Title(contract)), ("done", contract.Delivered), ("count", contract.Items.Count)));
                }

                if (contract.Items.Count > 0 && contract.Delivered >= contract.Items.Count)
                    Complete(contract);
                break;
            case WFContractTow tow:
                if (contract.WreckGrid is not { } towed || TerminatingOrDeleted(towed))
                    Fail(contract, "wf-contract-reason-hull-lost");
                else if (TowGap(contract) is { } gap && gap <= tow.Range)
                {
                    _departures[towed] = _timing.CurTime;
                    Complete(contract);
                }
                break;
            default:
                CheckOperation(contract, proto.Objective);
                break;
            case WFContractSurvey:
                UpdateSurvey(contract);
                if (contract.Scanned.Count >= contract.Count)
                    Complete(contract);
                else if (contract.Anomalies.Any(a => !contract.Scanned.Contains(a) && TerminatingOrDeleted(a)))
                    Fail(contract, "wf-contract-reason-anomaly-collapsed");
                break;
            case WFContractEscort escort:
                if (contract.EncounterEntity is not { } run || TerminatingOrDeleted(run))
                    Void(contract, "wf-contract-reason-target-gone");
                else if (EscortedShip(contract, escort) is not { } ship || TerminatingOrDeleted(ship.Grid))
                    Fail(contract, "wf-contract-reason-escort-lost");
                else if (ship.Surrendered)
                    Fail(contract, "wf-contract-reason-escort-surrendered");
                else if (ship.Flown || Arrived(ship, contract.Destination))
                    Complete(contract);
                break;
        }
    }

    /// <summary>Whether an escorted ship is docked to one of its destination station's grids.</summary>
    private bool Arrived(Encounters.Components.WFEncounterShipState ship, EntityUid? destination)
    {
        if (destination == null)
            return false;

        if (ship.Virtual)
            return _crewObjectives.TryGetQueue(ship.Grid, ship.Group, out var left) && left.All(order => order.Kind != WFCrewObjectiveKind.Dock);

        var docks = EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid == ship.Grid && dock.DockedWith is { } other && OnStation(other, destination))
                return true;
        }

        return false;
    }

    /// <summary>The ship an escort contract protects, if its encounter is still running.</summary>
    private Encounters.Components.WFEncounterShipState? EscortedShip(WFContract contract, WFContractEscort escort)
    {
        return contract.EncounterEntity is { } encounter && TryComp<Encounters.Components.WFEncounterComponent>(encounter, out var comp)
               && comp.Ships.TryGetValue(escort.Ship, out var ship)
            ? ship
            : null;
    }

    /// <summary>Whether a supply crate is closed on its station with the whole manifest inside.</summary>
    private bool Delivers(WFContract contract, EntityUid crate)
    {
        if (TerminatingOrDeleted(crate) || !TryComp<EntityStorageComponent>(crate, out var storage) || storage.Open
            || !OnStation(crate, contract.Destination))
            return false;

        var have = Packed(contract, crate);
        return contract.Goods.Select((line, i) => have[i] >= line.Count).All(met => met);
    }

    /// <summary>Takes back what a contract put in the world once it ends.</summary>
    private void Cleanup(WFContract contract)
    {
        var proto = _proto.Index(contract.Prototype);
        foreach (var item in contract.Items)
        {
            if (TerminatingOrDeleted(item) || !TryComp<WFContractItemComponent>(item, out var comp))
                continue;

            var set = comp.Delivered;
            comp.Delivered = true;
            // An undelivered supply crate is the holder's to keep.
            if (proto.Objective is WFContractSupply && contract.Status != WFContractStatus.Completed)
            {
                RemComp<WFContractItemComponent>(item);
                _meta.SetEntityName(item, Loc.GetString("wf-contract-crate-void-name"));
                continue;
            }

            if (proto.Objective is WFContractSupply)
                TakeDelivery(contract, item);

            if (proto.Objective is WFContractDeploy && set && contract.Status == WFContractStatus.Completed)
            {
                RemComp<WFContractItemComponent>(item);
                _departures[item] = _timing.CurTime + BuoyLifetime;
                continue;
            }

            QueueDel(item);
        }

        contract.Items.Clear();
        ReleaseSite(contract);
        ReleaseHeld(contract);
        contract.Anomalies.Clear();
        ClearMarkers(contract);
        WithdrawRaiders(contract);
        foreach (var twin in contract.ExtraWrecks)
        {
            if (!TerminatingOrDeleted(twin) && !PlayerNear(twin, WreckKeepRange))
                QueueDel(twin);
        }

        if (contract.WreckGrid is { } wreck && !TerminatingOrDeleted(wreck) && !PlayerNear(wreck, WreckKeepRange))
            QueueDel(wreck);
    }

    /// <summary>Takes a delivered crate's manifest goods and puts everything else, players above all, out beside it.</summary>
    private void TakeDelivery(WFContract contract, EntityUid crate)
    {
        if (_proto.Index(contract.Prototype).Objective is not WFContractSupply supply
            || !TryComp<EntityStorageComponent>(crate, out var storage))
            return;

        var needed = contract.Goods.Select(g => g.Count).ToList();
        var faction = contract.Destination is { } station && HousesFaction(station) ? station : (EntityUid?) null;
        // A member supplying their own faction would find the goods in its receiving crate and could deliver them
        // again; the faction uses them up instead.
        var companies = faction is { } receiving ? ReceivingCompanies(receiving, contract) : null;
        if (companies != null && contract.Company is { } own && companies.Any(c => c.Id == own))
            faction = null;
        EntityUid? received = null;
        foreach (var uid in storage.Contents.ContainedEntities.ToList())
        {
            if (HasComp<MobStateComponent>(uid) || HasComp<MindContainerComponent>(uid))
                continue;

            for (var i = 0; i < needed.Count; i++)
            {
                if (needed[i] <= 0 || Matches(supply.Goods[contract.Goods[i].Line], uid) is not (> 0 and var count))
                    continue;

                needed[i] -= count;
                if (faction is not { } home)
                    Del(uid);
                else
                    Receive(home, uid, companies!, ref received);
                break;
            }
        }

        _entityStorage.EmptyContents(crate);
    }

    private void OnItemShutdown(Entity<WFContractItemComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Delivered || !_contracts.TryGetValue(ent.Comp.Contract, out var contract)
            || contract.Status != WFContractStatus.Active)
            return;

        contract.Items.Remove(ent);
        Fail(contract, "wf-contract-reason-lost");
    }

    private void OnItemExamined(Entity<WFContractItemComponent> ent, ref ExaminedEvent args)
    {
        if (!_contracts.TryGetValue(ent.Comp.Contract, out var contract) || contract.Status != WFContractStatus.Active)
            return;

        using (args.PushGroup(nameof(WFContractItemComponent)))
        {
            args.PushMarkup(Loc.GetString("wf-contract-item-examine", ("id", contract.Id), ("title", Title(contract)),
                ("destination", StationName(contract.Destination)), ("holder", contract.Character)));
            if (_proto.Index(contract.Prototype).Objective is not WFContractSupply supply)
                return;

            var have = Packed(contract, ent);
            for (var i = 0; i < contract.Goods.Count; i++)
            {
                var (line, count) = contract.Goods[i];
                args.PushMarkup(Loc.GetString("wf-contract-crate-line", ("name", GoodsName(supply.Goods[line])),
                    ("have", Math.Min(have[i], count)), ("count", count), ("done", have[i] >= count)));
            }
        }
    }

    /// <summary>A supply crate closed on its station says what it still lacks, or is taken.</summary>
    private void OnCrateClosed(Entity<WFContractItemComponent> ent, ref StorageAfterCloseEvent args)
    {
        if (!_contracts.TryGetValue(ent.Comp.Contract, out var contract) || contract.Status != WFContractStatus.Active
            || _proto.Index(contract.Prototype).Objective is not WFContractSupply supply
            || !OnStation(ent, contract.Destination))
            return;

        if (Delivers(contract, ent))
        {
            Complete(contract);
            return;
        }

        var have = Packed(contract, ent);
        var missing = contract.Goods
            .Select((line, i) => (line, have: have[i]))
            .Where(entry => entry.have < entry.line.Count)
            .Select(entry => Loc.GetString("wf-contract-crate-missing-line", ("name", GoodsName(supply.Goods[entry.line.Line])),
                ("count", entry.line.Count - entry.have)));
        _popup.PopupEntity(Loc.GetString("wf-contract-crate-short", ("missing", string.Join(", ", missing))), ent);
    }

    private void OnEncounterResolved(ref WFEncounterResolvedEvent args)
    {
        var encounter = args.Encounter;
        var contract = _contracts.Values.FirstOrDefault(c => c.EncounterEntity == encounter && c.Status == WFContractStatus.Active);
        if (contract == null)
            return;

        if (_proto.Index(contract.Prototype).Objective is WFContractEscort escort)
        {
            // A surrender ends the run too, and is the escort's failure, not the target leaving.
            if (EscortedShip(contract, escort) is { Surrendered: true })
                Fail(contract, "wf-contract-reason-escort-surrendered");
            else if (args.Resolution == WFEncounterResolution.Ended)
                Void(contract, "wf-contract-reason-target-gone");
            else if (EscortedShip(contract, escort) is { } ship && !TerminatingOrDeleted(ship.Grid)
                     && (ship.Flown || Arrived(ship, contract.Destination)))
                Complete(contract);
            else if (args.Resolution is WFEncounterResolution.Destroyed or WFEncounterResolution.Expired)
                Fail(contract, "wf-contract-reason-escort-lost");
            return;
        }

        // Destroying a picket's raiders completes it; their leaving doesn't.
        if (_proto.Index(contract.Prototype).Objective is WFContractPicket)
        {
            if (args.Resolution == WFEncounterResolution.Destroyed)
                Complete(contract);
            return;
        }

        switch (args.Resolution)
        {
            case WFEncounterResolution.Destroyed:
                ClaimantsInterfered(contract, encounter);
                Complete(contract);
                break;
            case WFEncounterResolution.Expired:
            case WFEncounterResolution.Completed:
                Fail(contract, "wf-contract-reason-escaped");
                break;
            default:
                Void(contract, "wf-contract-reason-target-gone");
                break;
        }
    }

    /// <summary>Whether an entity is aboard one of a station's grids, carried or not.</summary>
    private bool OnStation(EntityUid uid, EntityUid? station)
    {
        return station != null && _station.GetOwningStation(uid) == station;
    }

    private bool PlayerNear(EntityUid uid, float range)
    {
        return PlayerNear(_transform.GetMapCoordinates(uid), range, ghosts: true);
    }

    /// <summary>Whether a player is within range of a point; ghosts count only if asked.</summary>
    private bool PlayerNear(MapCoordinates at, float range, bool ghosts = false)
    {
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var xform))
        {
            if (!ghosts && HasComp<GhostComponent>(player))
                continue;

            if (xform.MapID == at.MapId && (_transform.GetWorldPosition(xform) - at.Position).Length() <= range)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Open space at a distance from a station, clear of grids, stations, gravity wells, reality anchors and players;
    /// a crowded band is tried again, then widened.
    /// </summary>
    private MapCoordinates? PickSite(EntityUid station, float min, float max, float clearance, bool quiet = false)
    {
        if (_station.GetLargestGrid(station) is not { } grid)
            return null;

        var centre = _transform.GetMapCoordinates(grid);
        var far = Math.Max(min, max);
        for (var pass = 0; pass < SitePasses; pass++)
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var point = centre.Position + _random.NextAngle().ToVec() * _random.NextFloat(min, far);
                if (_scheduler.IsClearSpace(centre.MapId, point, SiteClearance, clearance)
                    && !_lattice.NearAnchor(centre.MapId, point, AnchorClearance)
                    && !PlayerNear(new MapCoordinates(point, centre.MapId), PlayerClearance))
                    return new MapCoordinates(point, centre.MapId);
            }

            if (pass + 1 >= SitePasses / 2)
                far *= SiteWidening;
        }

        if (!quiet)
            Log.Warning($"No clear contract site {min}-{max} m from {ToPrettyString(station)}.");
        return null;
    }

    /// <summary>Whether a hostile site can be found now, so a crowded sector posts no bounty it couldn't start.</summary>
    private bool SiteAvailable(EntityUid station, float min, float max, float clearance)
    {
        // One search per band per tick; a crowded sector otherwise pays the full search for every offer it rolls.
        if (_siteChecksTick != _timing.CurTick)
        {
            _siteChecks.Clear();
            _siteChecksTick = _timing.CurTick;
        }

        var key = (station, min, max, clearance);
        if (!_siteChecks.TryGetValue(key, out var found))
        {
            found = PickSite(station, min, max, clearance, quiet: true) != null;
            _siteChecks[key] = found;
        }

        return found;
    }

    private readonly Dictionary<(EntityUid, float, float, float), bool> _siteChecks = new();
    private Robust.Shared.Timing.GameTick _siteChecksTick;

    /// <summary>Metres from a station's largest grid to a site.</summary>
    private float SiteDistance(EntityUid station, MapCoordinates site)
    {
        return _station.GetLargestGrid(station) is { } grid
            ? (_transform.GetMapCoordinates(grid).Position - site.Position).Length()
            : 0f;
    }

    /// <summary>A random floor tile of a wreck clear of tables and machines, else any walkable one.</summary>
    private EntityCoordinates? FreeTile(Entity<MapGridComponent> grid, Box2i? bounds = null)
    {
        // Spawns in the same tick share one scan, each taking its own tile.
        if (_freeTiles is not { } cache || cache.Grid != grid.Owner || cache.Bounds != bounds || cache.Tick != _timing.CurTick)
        {
            cache = (grid.Owner, bounds, _timing.CurTick, new List<Vector2i>(), new List<Vector2i>());
            foreach (var tile in SiteTiles(grid, bounds))
            {
                if (_turf.IsSpace(tile) || _turf.IsTileBlocked(grid, tile.GridIndices, CollisionGroup.Impassable, grid.Comp))
                    continue;

                cache.Walkable.Add(tile.GridIndices);
                if (!_turf.IsTileBlocked(grid, tile.GridIndices, CargoBlockers, grid.Comp))
                    cache.Clear.Add(tile.GridIndices);
            }

            _freeTiles = cache;
        }

        // A cramped wreck may have no clear floor.
        var pool = cache.Clear.Count > 0 ? cache.Clear : cache.Walkable;
        if (pool.Count == 0)
            return null;

        var picked = _random.Pick(pool);
        cache.Clear.Remove(picked);
        if (cache.Walkable.Count > 1)
            cache.Walkable.Remove(picked);
        return _map.GridTileToLocal(grid, grid.Comp, picked);
    }

    private (EntityUid Grid, Box2i? Bounds, Robust.Shared.Timing.GameTick Tick, List<Vector2i> Clear, List<Vector2i> Walkable)? _freeTiles;

    /// <summary>Every tile of a wreck, or the ground tiles within a planet site's bounds.</summary>
    private IEnumerable<TileRef> SiteTiles(Entity<MapGridComponent> grid, Box2i? bounds)
    {
        if (bounds is not { } box)
        {
            foreach (var tile in _map.GetAllTiles(grid, grid.Comp))
                yield return tile;
            yield break;
        }

        for (var x = box.Left; x < box.Right; x++)
        {
            for (var y = box.Bottom; y < box.Top; y++)
            {
                var tile = _map.GetTileRef(grid, grid.Comp, new Vector2i(x, y));
                if (!tile.Tile.IsEmpty)
                    yield return tile;
            }
        }
    }
}
