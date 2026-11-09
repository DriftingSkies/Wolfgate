using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server.Administration.Logs;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WF.OutpostRaids;
using Content.Shared._WF.Planets;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.OutpostRaids;

/// <summary>
/// Decides when outposts are raided and with what. Someone at home builds an outpost's attention into threat points;
/// in its world's Active window, past its grace, and once per window, the director spends them on the biggest raid of
/// the world's <see cref="WFRaidTablePrototype"/> the outpost's ladder step allows, warns of it through
/// <see cref="WFRaidSystem"/>, and walks it in when it lands. Beating a raid moves the outpost up to the table's next step.
/// </summary>
public sealed partial class WFRaidDirectorSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFPlanetWeatherSystem _weather = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;
    [Dependency] private WFRaidLootSystem _loot = default!;
    [Dependency] private WFOrbitalDefenceSystem _defence = default!;
    [Dependency] private WFRaidSystem _raids = default!;
    [Dependency] private WFRaidWindowSystem _window = default!;

    /// <summary>The blackboard key of the spot a raid mob walks to.</summary>
    public const string TargetKey = "WFRaidTarget";

    /// <summary>The HTN a raid mob runs: fight what it meets, else walk to its spot.</summary>
    public const string RaidCompound = "WFRaidWildlifeCompound";

    /// <summary>Raid mobs stay awake this far from any player.</summary>
    public const float SleepRange = 150f;

    /// <summary>How close to its claim edge spot a raid mob counts as there.</summary>
    private const float EdgeReach = 3f;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    /// <summary>Raids picked and waiting to land: the pack each will send.</summary>
    private readonly Dictionary<int, WFRaidPack> _picked = new();

    /// <summary>Raids on the ground, by id.</summary>
    private readonly Dictionary<int, WFActiveRaid> _active = new();

    /// <summary>Shuttle raids descending on a defended outpost, by id; they land when the descent is over.</summary>
    private readonly Dictionary<int, WFActiveRaid> _descending = new();

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRaidArrivedEvent>(OnArrived);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _picked.Clear();
            _active.Clear();
            _descending.Clear();
            _lingering.Clear();
        });
    }

    /// <summary>The raids on the ground.</summary>
    public IReadOnlyDictionary<int, WFActiveRaid> ActiveRaids => _active;

    /// <summary>The raid table for a world: its surface's own, or the default.</summary>
    public WFRaidTablePrototype? GetTable(EntityUid world)
    {
        var surface = CompOrNull<WFPlanetNetworkComponent>(world)?.Surface;
        WFRaidTablePrototype? fallback = null;
        foreach (var table in _proto.EnumeratePrototypes<WFRaidTablePrototype>())
        {
            if (table.Surfaces.Count == 0)
                fallback ??= table;
            else if (surface is { } id && table.Surfaces.Contains(id))
                return table;
        }

        return fallback;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextTick)
            return;
        _nextTick = now + TickInterval;

        UpdateActive();
        var enabled = _cfg.GetCVar(OutpostRaidCVars.Enabled);
        var outposts = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (outposts.MoveNext(out var uid, out var outpost, out var grid))
        {
            var attention = EnsureComp<WFRaidAttentionComponent>(uid);
            if (!attention.Started)
            {
                attention.Started = true;
                attention.GraceUntil = now + TimeSpan.FromSeconds(_cfg.GetCVar(OutpostRaidCVars.Grace));
            }

            if (!SomeoneHome((uid, outpost, grid)))
                continue;

            var tiles = _outposts.CountTiles((uid, grid));
            var scale = new WFRaidAttentionScaleEvent(1f);
            RaiseLocalEvent(uid, ref scale);
            attention.Attention += _cfg.GetCVar(OutpostRaidCVars.AttentionRate) * (float) TickInterval.TotalMinutes * (1f + tiles / 400f) * scale.Scale;
            if (enabled)
                TryPickRaid((uid, outpost, grid, attention));
        }
    }

    /// <summary>
    /// Picks and schedules a raid on an outpost if every gate is open: its world's window, its grace, once a window, the
    /// sector caps, and threat points enough for a raid at or below its ladder step.
    /// </summary>
    public bool TryPickRaid(Entity<WFOutpostComponent, MapGridComponent, WFRaidAttentionComponent> outpost)
    {
        var now = _timing.CurTime;
        var attention = outpost.Comp3;
        if (now < attention.GraceUntil || now < attention.RaidedUntil || IsRaided(outpost))
            return false;
        if (_picked.Count + _active.Count >= _cfg.GetCVar(OutpostRaidCVars.MaxRaids) || CountMobs() >= _cfg.GetCVar(OutpostRaidCVars.MaxMobs))
            return false;
        if (!_window.TryGetWindowOn(outpost.Comp1.Ground, out var active, out var until) || !active)
            return false;
        if (!_weather.TryGetWorld(outpost.Comp1.Ground, out var world) || GetTable(world) is not { } table)
            return false;
        if (PickPack(table, attention) is not { } pack)
            return false;

        var tier = _proto.Index(pack.Tier);
        attention.Attention -= tier.Cost;
        attention.RaidedUntil = until > TimeSpan.Zero ? now + until : now + TimeSpan.FromHours(1);
        var id = _raids.ScheduleRaid(outpost, now + TimeSpan.FromSeconds(_cfg.GetCVar(OutpostRaidCVars.Lead)));
        _picked[id] = pack;
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"The raid director sent a step {tier.Step} raid ({tier.ID}) at {ToPrettyString(outpost):grid}");
        return true;
    }

    /// <summary>The pack of the highest affordable tier at or below the outpost's step, weighted among that tier's packs.</summary>
    private WFRaidPack? PickPack(WFRaidTablePrototype table, WFRaidAttentionComponent attention)
    {
        var best = -1;
        foreach (var pack in table.Packs)
        {
            var tier = _proto.Index(pack.Tier);
            if (tier.Step <= attention.Step && tier.Cost <= attention.Attention && tier.Step > best)
                best = tier.Step;
        }

        if (best < 0)
            return null;

        var total = 0f;
        foreach (var pack in table.Packs)
        {
            if (_proto.Index(pack.Tier).Step == best)
                total += pack.Weight;
        }

        var roll = _random.NextFloat(total);
        WFRaidPack? picked = null;
        foreach (var pack in table.Packs)
        {
            if (_proto.Index(pack.Tier).Step != best)
                continue;
            picked = pack;
            roll -= pack.Weight;
            if (roll <= 0)
                break;
        }

        return picked;
    }

    /// <summary>Whether a raid on the outpost is on its way or on the ground.</summary>
    public bool IsRaided(EntityUid outpost)
    {
        foreach (var raid in _raids.Raids)
        {
            if (raid.Outpost == outpost)
                return true;
        }

        foreach (var raid in _active.Values)
        {
            if (raid.Outpost == outpost)
                return true;
        }

        foreach (var raid in _descending.Values)
        {
            if (raid.Outpost == outpost)
                return true;
        }

        return false;
    }

    /// <summary>Whether a living player is on the outpost or inside its claim.</summary>
    public bool SomeoneHome(Entity<WFOutpostComponent, MapGridComponent> outpost)
    {
        if (!TryComp<MapGridComponent>(outpost.Comp1.Ground, out var groundGrid))
            return false;

        var (min, max) = _outposts.GetClaimZone(outpost);
        var groundMap = Transform(outpost.Comp1.Ground).MapID;
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (!_mobState.IsAlive(uid))
                continue;
            if (xform.GridUid == outpost.Owner)
                return true;
            if (xform.MapID != groundMap)
                continue;

            var index = _maps.TileIndicesFor(outpost.Comp1.Ground, groundGrid, _transform.GetMapCoordinates(uid, xform));
            if (index.X >= min.X && index.X <= max.X && index.Y >= min.Y && index.Y <= max.Y)
                return true;
        }

        return false;
    }

    private int CountMobs()
    {
        var count = 0;
        foreach (var raid in _active.Values)
            count += raid.Mobs.Count;
        return count;
    }

    private void OnArrived(ref WFRaidArrivedEvent args)
    {
        if (!_picked.Remove(args.Id, out var pack)
            || !TryComp<WFOutpostComponent>(args.Outpost, out var outpost)
            || !TryComp<MapGridComponent>(args.Outpost, out var grid))
            return;

        if (!(pack.Tunnel && TryFindTunnelExit((args.Outpost, outpost, grid), out var spot, out var edge))
            && !TryFindWalkIn((args.Outpost, outpost, grid), out spot, out edge))
        {
            Log.Info($"A raid on {ToPrettyString(args.Outpost)} found nowhere to walk in from.");
            return;
        }

        var step = _proto.Index(pack.Tier).Step;
        var raid = new WFActiveRaid(args.Id, args.Outpost, outpost.Ground, step, NextStep(outpost.Ground, step), pack, spot, edge);
        if (pack.Shuttle != null && _defence.TryStartDescent(raid.Id, args.Outpost))
        {
            _descending[raid.Id] = raid;
            return;
        }

        Land(raid, false);
    }

    /// <summary>Ends a shuttle raid's descent: it lands, or, shot down, loses its shuttle and its first wave.</summary>
    public void FinishDescent(int id, bool shotDown)
    {
        if (!_descending.Remove(id, out var raid) || TerminatingOrDeleted(raid.Outpost))
            return;

        Land(raid, shotDown);
    }

    private void Land(WFActiveRaid raid, bool shotDown)
    {
        var pack = raid.Pack;
        if (shotDown)
        {
            raid.Killed++;
        }
        else
        {
            if (pack.Shuttle is { } shuttle && !TryLandShuttle(raid, shuttle))
                Log.Info($"A raid on {ToPrettyString(raid.Outpost)} found no room to set its shuttle down; it walks in.");
            if (pack.Arrival is { } arrival)
                raid.Props.Add(Spawn(arrival, raid.Spot));

            SendWave(raid);
        }

        raid.WavesLeft = Math.Max(0, pack.Waves - 1);
        raid.NextWave = _timing.CurTime + pack.WaveInterval;
        _active[raid.Id] = raid;
        if (shotDown)
        {
            _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
                $"A raid shuttle on {ToPrettyString(raid.Outpost):grid} was shot down; {raid.WavesLeft} waves still come");
            return;
        }

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"A raid of {raid.Mobs.Count} and {raid.Burrows.Count} burrows walked in on {ToPrettyString(raid.Outpost):grid} from {raid.Spot}");
    }

    /// <summary>
    /// Turns a freshly spawned mob into a raider: awake far from players, loading ground, walking to the claim edge.
    /// Wildlife get the raid HTN; humanoid raiders keep their own, and what they carry is marked raid loot.
    /// </summary>
    private void SetUpMob(EntityUid mob, WFActiveRaid raid, EntityCoordinates edge)
    {
        var comp = EnsureComp<WFRaidMobComponent>(mob);
        comp.Outpost = raid.Outpost;
        comp.Raid = raid.Id;
        comp.Edge = edge;
        EnsureComp<WFTerrainLoaderComponent>(mob);
        var raider = HasComp<WFRaidRaiderComponent>(mob);
        if (raider)
            _loot.MarkCarried(mob);
        if (!TryComp<HTNComponent>(mob, out var htn))
            return;

        if (!raider)
            htn.RootTask = new HTNCompoundTask { Task = RaidCompound };
        htn.SleepPlayerCheckRangeOverride = SleepRange;
        _npc.SetBlackboard(mob, TargetKey, edge, htn);
        _npc.WakeNPC(mob, htn);
        _htn.Replan(htn);
    }

    /// <summary>
    /// Walks raids on: on to the outpost once at the claim edge, a siege's next wave when it is due, beaten when all are
    /// dead and every burrow destroyed, away at dawn or past the leash.
    /// </summary>
    private void UpdateActive()
    {
        UpdateLingering();
        var now = _timing.CurTime;
        var leash = _cfg.GetCVar(OutpostRaidCVars.Leash);
        var done = new List<int>();
        foreach (var raid in _active.Values)
        {
            var outpostGone = TerminatingOrDeleted(raid.Outpost);
            var dawn = outpostGone
                       || !_window.TryGetWindowOn(Comp<WFOutpostComponent>(raid.Outpost).Ground, out var open, out _)
                       || !open;

            for (var i = raid.Mobs.Count - 1; i >= 0; i--)
            {
                var mob = raid.Mobs[i];
                if (TerminatingOrDeleted(mob))
                {
                    raid.Mobs.RemoveAt(i);
                    continue;
                }

                // Gear a raider picks up or is dressed in on the way is raid loot too.
                if (HasComp<WFRaidRaiderComponent>(mob))
                    _loot.MarkCarried(mob);

                if (!_mobState.IsAlive(mob))
                {
                    raid.Killed++;
                    raid.Mobs.RemoveAt(i);
                    RemComp<WFTerrainLoaderComponent>(mob);
                    continue;
                }

                var far = !outpostGone && (_transform.GetWorldPosition(mob) - _transform.GetWorldPosition(raid.Outpost)).Length() > leash;
                if (dawn || far)
                {
                    raid.Despawned++;
                    raid.Mobs.RemoveAt(i);
                    QueueDel(mob);
                    continue;
                }

                if (!outpostGone && Transform(mob).GridUid == raid.Outpost)
                    raid.BrokeIn = true;

                WalkOn(mob, raid);
            }

            UpdateBurrows(raid, dawn);
            if (dawn && raid.WavesLeft > 0)
            {
                raid.Despawned++;
                raid.WavesLeft = 0;
            }
            else if (raid.WavesLeft > 0 && now >= raid.NextWave)
            {
                SendWave(raid);
                raid.WavesLeft--;
                raid.NextWave = now + raid.Pack.WaveInterval;
            }

            if (raid.Mobs.Count > 0 || raid.Burrows.Count > 0 || raid.WavesLeft > 0)
                continue;

            done.Add(raid.Id);
            Leave(raid, dawn);
            var beaten = !outpostGone && raid.Despawned == 0 && raid.Killed > 0;
            if (beaten && TryComp<WFRaidAttentionComponent>(raid.Outpost, out var attention))
                attention.Step = Math.Max(attention.Step, raid.NextStep);

            if (!outpostGone)
            {
                var ended = new WFRaidEndedEvent(raid.Step, beaten, raid.BrokeIn);
                RaiseLocalEvent(raid.Outpost, ref ended);
            }
        }

        foreach (var id in done)
            _active.Remove(id);
    }

    /// <summary>Once at its claim edge spot, a raid mob goes for the outpost itself.</summary>
    private void WalkOn(EntityUid mob, WFActiveRaid raid)
    {
        if (!TryComp<WFRaidMobComponent>(mob, out var comp) || comp.ReachedEdge)
            return;

        var here = _transform.GetMapCoordinates(mob);
        var edge = _transform.ToMapCoordinates(comp.Edge);
        if (here.MapId != edge.MapId || (here.Position - edge.Position).Length() > EdgeReach)
            return;

        comp.ReachedEdge = true;
        if (TryComp<HTNComponent>(mob, out var htn))
        {
            _npc.SetBlackboard(mob, TargetKey, new EntityCoordinates(raid.Outpost, Comp<MapGridComponent>(raid.Outpost).LocalAABB.Center), htn);
            if (CompOrNull<WFRaidRaiderComponent>(mob)?.Breacher == true)
                _npc.SetBlackboard(mob, WFRaidBreachSystem.BreachKey, true, htn);
            _htn.Replan(htn);
        }
    }

    /// <summary>The ladder step after <paramref name="step"/>: the next one the world's table has a raid for.</summary>
    private int NextStep(EntityUid ground, int step)
    {
        var next = int.MaxValue;
        if (_weather.TryGetWorld(ground, out var world) && GetTable(world) is { } table)
        {
            foreach (var pack in table.Packs)
            {
                var tier = _proto.Index(pack.Tier).Step;
                if (tier > step && tier < next)
                    next = tier;
            }
        }

        return next == int.MaxValue ? step + 1 : next;
    }
}

/// <summary>A raid on the ground: who is left of it and how it went.</summary>
public sealed class WFActiveRaid(
    int id,
    EntityUid outpost,
    EntityUid ground,
    int step,
    int nextStep,
    WFRaidPack pack,
    EntityCoordinates spot,
    EntityCoordinates edge)
{
    public readonly int Id = id;
    public readonly EntityUid Outpost = outpost;
    public readonly EntityUid Ground = ground;
    public readonly int Step = step;

    /// <summary>The step beating this raid moves the outpost up to.</summary>
    public readonly int NextStep = nextStep;

    public readonly WFRaidPack Pack = pack;

    /// <summary>Where it walks in from, and the point on the claim edge it walks to first.</summary>
    public readonly EntityCoordinates Spot = spot;
    public readonly EntityCoordinates Edge = edge;

    public readonly List<EntityUid> Mobs = new();

    /// <summary>Its burrows still open; each must be destroyed to beat it.</summary>
    public readonly List<EntityUid> Burrows = new();

    /// <summary>What the raid rode in on: its quad or shuttle, gone at dawn.</summary>
    public readonly List<EntityUid> Props = new();

    /// <summary>A siege's waves still to come, and when the next is due.</summary>
    public int WavesLeft;
    public TimeSpan NextWave;

    public int Killed;
    public int Despawned;

    /// <summary>Whether a raid mob got onto the outpost itself.</summary>
    public bool BrokeIn;
}

/// <summary>Raised on an outpost to scale the attention it builds up.</summary>
[ByRefEvent]
public record struct WFRaidAttentionScaleEvent(float Scale);

/// <summary>Raised on an outpost when a raid on it is over: beaten, or gone with or without breaking in.</summary>
[ByRefEvent]
public readonly record struct WFRaidEndedEvent(int Step, bool Beaten, bool BrokeIn);
