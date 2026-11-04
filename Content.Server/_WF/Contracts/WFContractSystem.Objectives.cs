using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Systems;
using Content.Server.Storage.Components;
using Content.Shared._WF.Contracts;
using Content.Shared._WF.Encounters;
using Content.Shared.Examine;
using Content.Shared.Physics;
using Content.Shared.Stacks;
using Content.Shared.Storage.Components;
using Content.Shared.Tag;
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

    /// <summary>How close a player must be to a wreck to keep it in the world once its contract ends.</summary>
    private const float WreckKeepRange = 300f;

    /// <summary>Open space a bounty or wreck needs round it.</summary>
    private const float SiteClearance = 250f;

    /// <summary>How far an escort's rendezvous must be from any station.</summary>
    private const float EscortClearance = 800f;

    private void InitializeObjectives()
    {
        SubscribeLocalEvent<WFContractItemComponent, ComponentShutdown>(OnItemShutdown);
        SubscribeLocalEvent<WFContractItemComponent, ExaminedEvent>(OnItemExamined);
        SubscribeLocalEvent<WFContractItemComponent, StorageAfterCloseEvent>(OnCrateClosed);
        SubscribeLocalEvent<WFEncounterResolvedEvent>(OnEncounterResolved);
    }

    /// <summary>Fixes an offer's ends, counts and pay from its template. False if the round cannot give it.</summary>
    private bool TryRoll(WFContract contract, WFContractPrototype template, Dictionary<string, EntityUid> homes, EntityUid board)
    {
        switch (template.Objective)
        {
            case WFContractCourier courier:
            {
                if (EndStation(courier.From, contract.Home, board, courier.Destinations, homes, null) is not { } from
                    || EndStation(courier.To, contract.Home, board, courier.Destinations, homes, from) is not { } to
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

                contract.Encounter = _random.Pick(options);
                contract.Distance = (bounty.MinDistance + bounty.MaxDistance) / 2f;
                return true;
            }
            case WFContractRecovery recovery:
            {
                if (recovery.Wrecks.Count == 0 || !_proto.HasIndex(recovery.Item))
                    return false;

                contract.Wreck = _random.Pick(recovery.Wrecks);
                contract.Destination = recovery.To == WFContractEnd.Board ? board : contract.Home;
                contract.Distance = (recovery.MinDistance + recovery.MaxDistance) / 2f;
                return true;
            }
            case WFContractEscort escort:
            {
                var options = escort.Encounters.Where(id => _proto.HasIndex(id)).ToList();
                if (options.Count == 0
                    || EndStation(WFContractEnd.Other, contract.Home, board, escort.Destinations, homes, null) is not { } to)
                    return false;

                contract.Encounter = _random.Pick(options);
                contract.Destination = to;
                contract.Distance = Distance(contract.Home, to);
                contract.Reward += DistancePay(escort.PerKilometre, contract.Distance);
                return true;
            }
        }

        Log.Error($"Contract {template.ID} has an objective the board cannot run: {template.Objective.GetType().Name}.");
        return false;
    }

    /// <summary>Kilometres past this are not paid: every station is within about 12 km of Caelestinus.</summary>
    public const float MaxPaidKilometres = 12f;

    /// <summary>Pay for a run's distance, up to <see cref="MaxPaidKilometres"/>.</summary>
    public static int DistancePay(int perKilometre, float metres) =>
        (int) (perKilometre * Math.Min(metres / 1000f, MaxPaidKilometres));

    /// <summary>The station one end of a courier run is at. <paramref name="not"/> keeps the far end off the near one.</summary>
    private EntityUid? EndStation(WFContractEnd end, EntityUid home, EntityUid board, List<ProtoId<WFContractorPrototype>> others,
        Dictionary<string, EntityUid> homes, EntityUid? not)
    {
        switch (end)
        {
            case WFContractEnd.Contractor:
                return home;
            case WFContractEnd.Board:
                return board;
        }

        var candidates = (others.Count == 0 ? homes.Values : others.Where(id => homes.ContainsKey(id)).Select(id => homes[id]))
            .Where(station => station != home && station != not)
            .Distinct()
            .ToList();
        return candidates.Count == 0 ? null : _random.Pick(candidates);
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
                var at = contract.Origin is { } origin ? Anchor(origin) : BoardDrop(board);
                if (at is not { } coords)
                    return false;

                for (var i = 0; i < contract.Count; i++)
                {
                    var offset = contract.Count == 1 ? Vector2.Zero : _random.NextVector2(0.35f);
                    var package = Spawn(courier.Package, coords.Offset(offset));
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

                    if (!_encounters.TrySpawn(encounterProto, site, out var encounter, offBudget: true))
                        continue;

                    contract.Site = site;
                    contract.EncounterEntity = encounter;
                    return true;
                }

                return false;
            }
            case WFContractRecovery recovery:
            {
                if (contract.Wreck is not { } wreck
                    || PickSite(contract.Home, recovery.MinDistance, recovery.MaxDistance, recovery.StationClearance) is not { } site
                    || !_mapLoader.TryLoadGrid(site.MapId, wreck, out var grid, offset: site.Position, rot: _random.NextAngle()))
                    return false;

                contract.Site = site;
                contract.WreckGrid = grid.Value.Owner;
                _meta.SetEntityName(grid.Value.Owner, Loc.GetString("wf-contract-wreck-name", ("id", contract.Id)));
                if (FreeTile(grid.Value) is not { } tile)
                {
                    QueueDel(grid.Value.Owner);
                    return false;
                }

                var item = Spawn(recovery.Item, tile);
                Track(contract, item);
                return true;
            }
            case WFContractEscort escort:
            {
                if (contract.Encounter is not { } encounterId || !_proto.TryIndex(encounterId, out var encounterProto)
                    || contract.Destination is not { } destination)
                    return false;

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    if (PickSite(contract.Home, escort.MinDistance, escort.MaxDistance, EscortClearance) is not { } site
                        || !_encounters.TrySpawn(encounterProto, site, out var encounter, stops: new[] { destination }, offBudget: true))
                        continue;

                    contract.Site = site;
                    contract.EncounterEntity = encounter;
                    return true;
                }

                return false;
            }
        }

        return false;
    }

    private void Track(WFContract contract, EntityUid item)
    {
        EnsureComp<WFContractItemComponent>(item).Contract = contract.Id;
        contract.Items.Add(item);
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
                if (contract.Items.FirstOrDefault() is { Valid: true } recovered && !TerminatingOrDeleted(recovered)
                    && OnStation(recovered, contract.Destination))
                    Complete(contract);
                break;
            case WFContractEscort escort:
                if (EscortedShip(contract, escort) is not { } ship || TerminatingOrDeleted(ship.Grid))
                    Fail(contract, "wf-contract-reason-escort-lost");
                else if (ship.Flown)
                    Complete(contract);
                break;
        }
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

            comp.Delivered = true;
            // A supply crate that was not delivered is the holder's to keep, with what they packed.
            if (proto.Objective is WFContractSupply && contract.Status != WFContractStatus.Completed)
            {
                RemComp<WFContractItemComponent>(item);
                _meta.SetEntityName(item, Loc.GetString("wf-contract-crate-void-name"));
                continue;
            }

            QueueDel(item);
        }

        contract.Items.Clear();
        ClearMarkers(contract);
        if (contract.WreckGrid is { } wreck && !TerminatingOrDeleted(wreck) && !PlayerNear(wreck, WreckKeepRange))
            QueueDel(wreck);
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
            // Ended by an admin or the round: nobody's fault. Otherwise its ship either made it or it did not.
            if (args.Resolution == WFEncounterResolution.Ended)
                Void(contract, "wf-contract-reason-target-gone");
            else if (EscortedShip(contract, escort) is { Flown: true } ship && !TerminatingOrDeleted(ship.Grid))
                Complete(contract);
            else if (args.Resolution is WFEncounterResolution.Destroyed or WFEncounterResolution.Expired)
                Fail(contract, "wf-contract-reason-escort-lost");
            return;
        }

        ClaimantsInterfered(contract, encounter);

        switch (args.Resolution)
        {
            case WFEncounterResolution.Destroyed:
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
        var at = _transform.GetMapCoordinates(uid);
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var xform))
        {
            if (xform.MapID == at.MapId && (_transform.GetWorldPosition(xform) - at.Position).Length() <= range)
                return true;
        }

        return false;
    }

    /// <summary>
    /// A point in open sector space a set distance from a station, clear of every grid, every station by
    /// <paramref name="clearance"/> and any planet's gravity well.
    /// </summary>
    private MapCoordinates? PickSite(EntityUid station, float min, float max, float clearance)
    {
        if (_station.GetLargestGrid(station) is not { } grid)
            return null;

        var centre = _transform.GetMapCoordinates(grid);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var point = centre.Position + _random.NextAngle().ToVec() * _random.NextFloat(min, Math.Max(min, max));
            if (_scheduler.IsClearSpace(centre.MapId, point, SiteClearance, clearance))
                return new MapCoordinates(point, centre.MapId);
        }

        return null;
    }

    /// <summary>A random floor tile of a grid with nothing solid on it.</summary>
    private EntityCoordinates? FreeTile(Entity<MapGridComponent> grid)
    {
        var tiles = new List<Vector2i>();
        foreach (var tile in _map.GetAllTiles(grid, grid.Comp))
        {
            if (!_turf.IsTileBlocked(tile, CollisionGroup.Impassable))
                tiles.Add(tile.GridIndices);
        }

        return tiles.Count == 0 ? null : _map.GridTileToLocal(grid, grid.Comp, _random.Pick(tiles));
    }
}
