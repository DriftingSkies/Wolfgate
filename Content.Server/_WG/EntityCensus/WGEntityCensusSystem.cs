using System.Linq;
using Content.Server._WG.AsteroidMining;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.GameTicking;
using Content.Shared.Station.Components;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WG.EntityCensus;

/// <summary>Server cvars for <see cref="WGEntityCensusSystem"/>.</summary>
[CVarDefs]
public sealed class WGEntityCensusCVars
{
    /// <summary>Seconds between censuses; 0 takes none.</summary>
    public static readonly CVarDef<float> Interval =
        CVarDef.Create("wg.census.interval", 300f, CVar.SERVERONLY);

    /// <summary>Milliseconds a tick may spend counting.</summary>
    public static readonly CVarDef<float> BudgetMs =
        CVarDef.Create("wg.census.budget_ms", 0.5f, CVar.SERVERONLY);
}

/// <summary>What an entity stands on or in, for the census.</summary>
public enum WGCensusKind : byte
{
    Nullspace,
    Space,
    Planet,
    Station,
    PlayerShip,
    NpcShip,
    Asteroid,
    OtherGrid,
    Count,
}

/// <summary>One count of every entity: by where it is, by prototype and by grid.</summary>
public sealed class WGCensus
{
    public TimeSpan Taken;
    public int Total;
    public int Contained;
    public readonly int[] Kinds = new int[(int) WGCensusKind.Count];
    public readonly int[] Paused = new int[(int) WGCensusKind.Count];
    public readonly Dictionary<string, int> Prototypes = new();

    /// <summary>Deleted per prototype between the census before and this one.</summary>
    public Dictionary<string, int> Deleted = new();

    /// <summary>Spawned per prototype between the census before (or the round start) and this one.</summary>
    public readonly Dictionary<string, int> Spawned = new();

    /// <summary>Seconds the spawn and delete counts cover.</summary>
    public double Span;

    public readonly Dictionary<EntityUid, int> GridCounts = new();
    public readonly List<(string Name, WGCensusKind Kind, int Count)> Grids = new();
}

/// <summary>
/// Counts every entity a few at a time: where it is (station, player ship, NPC ship, asteroid, planet, loose in
/// space), its prototype, and how many of each prototype came and went since the last count. The answer goes to
/// gauges, the log and <c>wg_entcensus</c>.
/// </summary>
public sealed class WGEntityCensusSystem : EntitySystem
{
    /// <summary>Prototypes and grids kept in gauges and the log.</summary>
    private const int Top = 40;

    private const string NoPrototype = "(none)";

    private static readonly Gauge KindGauge = Metrics.CreateGauge(
        "wg_entities",
        "Entities by where they are and whether they are paused",
        new GaugeConfiguration { LabelNames = ["kind", "state"] });

    private static readonly Gauge PrototypeGauge = Metrics.CreateGauge(
        "wg_entities_prototype",
        "Entities of the most numerous prototypes",
        new GaugeConfiguration { LabelNames = ["prototype"] });

    private static readonly Gauge SpawnGauge = Metrics.CreateGauge(
        "wg_entities_spawned_per_minute",
        "Spawns a minute of the prototypes spawned most since the census before",
        new GaugeConfiguration { LabelNames = ["prototype"] });

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<MetaDataComponent> _metaQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private readonly System.Diagnostics.Stopwatch _watch = new();
    private readonly Dictionary<EntityUid, WGCensusKind> _gridKinds = new();
    private readonly HashSet<string> _prototypeLabels = new();
    private readonly HashSet<string> _spawnLabels = new();
    private Dictionary<string, int> _deleted = new();
    private Dictionary<string, int> _spawned = new();
    private TimeSpan _countedSince;
    private EntityUid[] _snapshot = [];
    private WGCensus? _building;
    private int _cursor;
    private TimeSpan _next;
    private float _interval;
    private float _budget;

    /// <summary>The last finished census.</summary>
    public WGCensus? Last { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        _metaQuery = GetEntityQuery<MetaDataComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        Subs.CVar(_cfg, WGEntityCensusCVars.Interval, value => _interval = value, true);
        Subs.CVar(_cfg, WGEntityCensusCVars.BudgetMs, value => _budget = value, true);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        EntityManager.EntityDeleted += OnEntityDeleted;
        EntityManager.EntityInitialized += OnEntityAdded;
        _countedSince = _timing.CurTime;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        EntityManager.EntityDeleted -= OnEntityDeleted;
        EntityManager.EntityInitialized -= OnEntityAdded;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _building = null;
        _snapshot = [];
        _deleted.Clear();
        _spawned.Clear();
        _countedSince = _timing.CurTime;
        Last = null;
    }

    private void OnEntityAdded(Entity<MetaDataComponent> ent)
    {
        var id = ent.Comp.EntityPrototype?.ID ?? NoPrototype;
        _spawned[id] = _spawned.GetValueOrDefault(id) + 1;
    }

    private void OnEntityDeleted(Entity<MetaDataComponent> ent)
    {
        var id = ent.Comp.EntityPrototype?.ID ?? NoPrototype;
        _deleted[id] = _deleted.GetValueOrDefault(id) + 1;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_building == null)
        {
            if (_interval <= 0f || _timing.CurTime < _next)
                return;

            Begin();
        }

        _watch.Restart();
        while (_cursor < _snapshot.Length)
        {
            var end = Math.Min(_snapshot.Length, _cursor + 256);
            for (; _cursor < end; _cursor++)
            {
                Count(_building!, _snapshot[_cursor]);
            }

            if (_watch.Elapsed.TotalMilliseconds >= _budget)
                return;
        }

        Finish();
    }

    /// <summary>Counts everything this tick, however long it takes.</summary>
    public WGCensus TakeNow()
    {
        Begin();
        foreach (var uid in _snapshot)
        {
            Count(_building!, uid);
        }

        return Finish();
    }

    private void Begin()
    {
        _snapshot = EntityManager.GetEntities().ToArray();
        _cursor = 0;
        _gridKinds.Clear();
        _building = new WGCensus { Taken = _timing.CurTime };
    }

    private void Count(WGCensus census, EntityUid uid)
    {
        if (!_metaQuery.TryComp(uid, out var meta)
            || meta.EntityLifeStage >= EntityLifeStage.Terminating
            || !_xformQuery.TryComp(uid, out var xform))
            return;

        var kind = KindOf(census, xform);
        census.Total++;
        census.Kinds[(int) kind]++;
        if (meta.EntityPaused)
            census.Paused[(int) kind]++;

        if ((meta.Flags & MetaDataFlags.InContainer) != 0)
            census.Contained++;

        var id = meta.EntityPrototype?.ID ?? NoPrototype;
        census.Prototypes[id] = census.Prototypes.GetValueOrDefault(id) + 1;
    }

    private WGCensusKind KindOf(WGCensus census, TransformComponent xform)
    {
        if (xform.GridUid is not { } grid)
            return xform.MapID == MapId.Nullspace ? WGCensusKind.Nullspace : WGCensusKind.Space;

        census.GridCounts[grid] = census.GridCounts.GetValueOrDefault(grid) + 1;
        if (_gridKinds.TryGetValue(grid, out var known))
            return known;

        WGCensusKind kind;
        if (HasComp<MapComponent>(grid) || HasComp<MapGridComponent>(Transform(grid).ParentUid))
            kind = WGCensusKind.Planet;
        else if (HasComp<StationMemberComponent>(grid))
            kind = WGCensusKind.Station;
        else if (HasComp<WGAsteroidGridComponent>(grid))
            kind = WGCensusKind.Asteroid;
        else if (HasComp<WGEncounterGridComponent>(grid) || HasComp<WGCrewShipProfileComponent>(grid))
            kind = WGCensusKind.NpcShip;
        else if (HasComp<ShuttleDeedComponent>(grid))
            kind = WGCensusKind.PlayerShip;
        else
            kind = WGCensusKind.OtherGrid;

        _gridKinds[grid] = kind;
        return kind;
    }

    private WGCensus Finish()
    {
        var census = _building!;
        _building = null;
        _snapshot = [];
        _next = _timing.CurTime + TimeSpan.FromSeconds(_interval);

        foreach (var (grid, count) in census.GridCounts.OrderByDescending(pair => pair.Value).Take(Top))
        {
            if (_metaQuery.TryComp(grid, out var meta))
                census.Grids.Add((meta.EntityName, _gridKinds.GetValueOrDefault(grid), count));
        }

        // Spawns and deletions are counted as they happen, over the same span, so a prototype whose entities came
        // and went between two counts still shows, and the walk's own timing doesn't skew them.
        census.Deleted = _deleted;
        _deleted = new Dictionary<string, int>();
        foreach (var (id, count) in _spawned)
        {
            census.Spawned[id] = count;
        }

        _spawned.Clear();
        census.Span = (_timing.CurTime - _countedSince).TotalSeconds;
        _countedSince = _timing.CurTime;

        Last = census;
        Publish(census);
        return census;
    }

    private void Publish(WGCensus census)
    {
        for (var i = 0; i < (int) WGCensusKind.Count; i++)
        {
            var name = ((WGCensusKind) i).ToString();
            KindGauge.WithLabels(name, "paused").Set(census.Paused[i]);
            KindGauge.WithLabels(name, "active").Set(census.Kinds[i] - census.Paused[i]);
        }

        SetTop(PrototypeGauge, _prototypeLabels, census.Prototypes, 1.0);
        SetTop(SpawnGauge, _spawnLabels, census.Spawned, census.Span > 0 ? 60.0 / census.Span : 0.0);

        var kinds = string.Join(", ", Enumerable.Range(0, (int) WGCensusKind.Count)
            .Select(i => $"{(WGCensusKind) i} {census.Kinds[i]} ({census.Paused[i]} paused)"));
        Log.Info($"Entity census: {census.Total} entities, {census.Contained} in containers. {kinds}.");
        if (census.Spawned.Count > 0)
        {
            var spawned = string.Join(", ", census.Spawned.OrderByDescending(pair => pair.Value).Take(15)
                .Select(pair => $"{pair.Key} +{pair.Value} -{census.Deleted.GetValueOrDefault(pair.Key)}"));
            Log.Info($"Entity census, spawned and deleted over {census.Span:F0} s: {spawned}.");
        }
    }

    /// <summary>Sets a gauge for the largest values and drops the labels that fell out of the top.</summary>
    private static void SetTop(Gauge gauge, HashSet<string> labels, Dictionary<string, int> values, double scale)
    {
        var top = values.OrderByDescending(pair => pair.Value).Take(Top).ToList();
        foreach (var label in labels)
        {
            if (top.All(pair => pair.Key != label))
                gauge.RemoveLabelled(label);
        }

        labels.Clear();
        foreach (var (id, count) in top)
        {
            gauge.WithLabels(id).Set(count * scale);
            labels.Add(id);
        }
    }
}
