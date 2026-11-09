using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.Companies;
using Content.Server._WF.OutpostIndustry;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.ShipAccess;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.Companies;
using Content.Shared._WF.ShipAccess;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Colonies;

/// <summary>
/// World development: each holder, a player company or a player with none, scores on each world its outposts stand on.
/// The sum of a world's scores sets its tier; from Colony tier the world is a station in the sector economy, from
/// Charter tier a company governs it, and from Dominion tier its governor may build a yard in orbit.
/// </summary>
public sealed partial class WFColonySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFCompanySystem _companies = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;
    [Dependency] private WFPlanetWeatherSystem _weather = default!;
    [Dependency] private WFRaidDirectorSystem _director = default!;
    [Dependency] private WFShipAccessServerSystem _shipAccess = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

    private ISawmill _log = default!;
    private Task? _loading;
    private readonly List<Task> _pendingWrites = new();
    private readonly Dictionary<string, WorldState> _worlds = new();
    private TimeSpan _nextTick;
    private TimeSpan _nextFlush;

    /// <summary>Whether the scores have been read from the database.</summary>
    public bool Loaded { get; private set; }

    /// <summary>One world's development, kept for the server's life and written to the database.</summary>
    private sealed class WorldState(string id)
    {
        public readonly string Id = id;
        public readonly Dictionary<string, HolderState> Holders = new();
        public WFWorldTier Tier;
        public CharterState? Charter;

        /// <summary>Goods each holder keeps off the shelf.</summary>
        public readonly Dictionary<string, HashSet<string>> KeptGoods = new();

        /// <summary>Holders refused a founding this round for want of a permit, with their names.</summary>
        public readonly Dictionary<string, string> Applicants = new();

        public double Total => Holders.Values.Sum(holder => holder.Points);
    }

    private sealed class HolderState
    {
        public string Key = string.Empty;
        public string Name = string.Empty;
        public double Points;
        public DateTime Updated;
        public bool Dirty;
    }

    /// <summary>An outpost as one tick sees it.</summary>
    private readonly record struct OutpostEntry(
        Entity<WFOutpostComponent, MapGridComponent> Outpost,
        string World,
        EntityUid Network,
        string Holder,
        string Name,
        bool Home);

    public override void Initialize()
    {
        base.Initialize();
        _log = Logger.GetSawmill("wf.colonies");
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
        SubscribeLocalEvent<WFColonyMemberComponent, WFOutpostSettledEvent>(OnSettled);
        SubscribeLocalEvent<WFColonyMemberComponent, WFOutpostFingerprintEvent>(OnFingerprint);
        SubscribeLocalEvent<WFColonyMemberComponent, WFOutpostTransferredEvent>(OnTransferred);
        SubscribeLocalEvent<WFOutpostComponent, WFRaidEndedEvent>(OnRaidEnded);
        SubscribeLocalEvent<WFOutpostComponent, WFRaidAttentionScaleEvent>(OnAttentionScale);
        SubscribeLocalEvent<WFOutpostComponent, WFOutpostProducedEvent>(OnProduced);
        InitializeConsole();
        InitializeStation();
        InitializeImports();
        InitializeCharter();
        InitializeTerritory();
        InitializeHeritage();
        InitializeProduce();
        InitializeSeat();
        InitializeOffice();
        _loading = Reload();
    }

    /// <summary>Waits for loading and every pending write; for tests and admin tools.</summary>
    public async Task Flush()
    {
        if (_loading != null)
            await _loading;
        WriteDirty();
        while (_pendingWrites.Count > 0)
        {
            var pending = _pendingWrites.ToArray();
            await Task.WhenAll(pending);
            _pendingWrites.RemoveAll(task => pending.Contains(task));
        }
    }

    /// <summary>Rereads every score and charter, keeping what this round added to them.</summary>
    public async Task Reload()
    {
        List<WFStoredDevelopment> rows;
        List<WFStoredCharter> charters;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                rows = await _db.GetWorldDevelopmentAsync();
                charters = await _db.GetWorldChartersAsync();
                break;
            }
            // A synchronous database refuses overlapping requests, such as another system's write still finishing; retry.
            catch (InvalidOperationException) when (attempt < 60)
            {
                await Task.Delay(50);
            }
            catch (Exception e)
            {
                _log.Error($"Could not load world development: {e}");
                return;
            }
        }

        foreach (var row in rows)
        {
            var world = World(row.World);
            if (world.Holders.TryGetValue(row.Holder, out var held) && held.Dirty)
                continue;

            world.Holders[row.Holder] = new HolderState
            {
                Key = row.Holder, Name = row.Name, Points = row.Points, Updated = DateTime.SpecifyKind(row.Updated, DateTimeKind.Utc),
            };
        }

        foreach (var row in charters)
        {
            World(row.World).Charter = new CharterState
            {
                CompanyId = row.CompanyId,
                Granted = DateTime.SpecifyKind(row.Granted, DateTimeKind.Utc),
                Rival = row.Rival,
                Streak = row.Streak,
                LastCheck = DateTime.SpecifyKind(row.LastCheck, DateTimeKind.Utc),
                Defence = row.Defence,
                Open = row.Open,
                Permits = row.Permits.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(),
            };
        }

        Decay(DateTime.UtcNow);
        foreach (var world in _worlds.Values)
        {
            world.Tier = TierFor(WFWorldTier.None, world.Total);
            if (world.Tier < BaseTier(world.Id))
                world.Tier = BaseTier(world.Id);
        }

        Loaded = true;
    }

    private WorldState World(string id)
    {
        if (!_worlds.TryGetValue(id, out var world))
        {
            world = new WorldState(id) { Tier = BaseTier(id) };
            _worlds[id] = world;
        }

        return world;
    }

    #region Holders

    /// <summary>The key of a company as a holder.</summary>
    public static string CompanyKey(int id) => "c" + id;

    /// <summary>The key of a player as a holder.</summary>
    public static string UserKey(NetUserId user) => "u" + user.UserId.ToString("N");

    /// <summary>The company a holder key names, or null for a player.</summary>
    public static int? CompanyOfKey(string key) => key.StartsWith('c') && int.TryParse(key.AsSpan(1), out var id) ? id : null;

    /// <summary>The player a holder key names, or null for a company.</summary>
    public static NetUserId? UserOfKey(string key) => key.StartsWith('u') && Guid.TryParse(key.AsSpan(1), out var user) ? new NetUserId(user) : null;

    /// <summary>
    /// Who an outpost earns for: the player company its ship access is linked to in Company mode, or else its owner.
    /// </summary>
    public string HolderOf(Entity<WFOutpostComponent> outpost, out string name)
    {
        if (TryComp<WFShipAccessComponent>(outpost, out var access) && access.Mode == WFShipAccessMode.Company
            && access.CompanyId is { } company)
        {
            name = _companies.CompanyIdentity(company)?.Name ?? access.CompanyName ?? outpost.Comp.OwnerName;
            return CompanyKey(company);
        }

        name = outpost.Comp.OwnerName;
        return UserKey(outpost.Comp.Owner);
    }

    /// <summary>Who a player earns for: their character's company, or themself.</summary>
    public string HolderOf(ICommonSession session, out string name)
    {
        if (_companies.CompanyOfSession(session) is { } company)
        {
            name = company.Name;
            return CompanyKey(company.Id);
        }

        name = session.AttachedEntity is { } body ? Name(body) : session.Name;
        return UserKey(session.UserId);
    }

    /// <summary>The name a holder goes by now.</summary>
    private string HolderName(WorldState world, string key)
    {
        if (CompanyOfKey(key) is { } company && _companies.CompanyIdentity(company) is { } identity)
            return identity.Name;

        return world.Holders.TryGetValue(key, out var holder) ? holder.Name : key;
    }

    #endregion

    #region Points

    /// <summary>A holder's score on a world.</summary>
    public double Points(string world, string holder)
    {
        return _worlds.TryGetValue(world, out var state) && state.Holders.TryGetValue(holder, out var held) ? held.Points : 0;
    }

    /// <summary>The sum of every holder's score on a world.</summary>
    public double Total(string world) => _worlds.TryGetValue(world, out var state) ? state.Total : 0;

    /// <summary>A world's tier.</summary>
    public WFWorldTier Tier(string world) => _worlds.TryGetValue(world, out var state) ? state.Tier : BaseTier(world);

    /// <summary>What a world gives colonies, if it has an entry.</summary>
    public WFColonyWorldPrototype? WorldProto(string world)
    {
        foreach (var proto in _proto.EnumeratePrototypes<WFColonyWorldPrototype>())
        {
            if (proto.Surface.Id == world)
                return proto;
        }

        return null;
    }

    /// <summary>The tier a world never reads below: a world with a city on it is settled already.</summary>
    public WFWorldTier BaseTier(string world) => WorldProto(world)?.BaseTier ?? WFWorldTier.None;

    /// <summary>A holder's score over the world's total.</summary>
    public float Share(string world, string holder)
    {
        var total = Total(world);
        return total <= 0 ? 0f : (float) (Points(world, holder) / total);
    }

    /// <summary>Adds to a holder's score on a world, never below zero, and moves the world's tier.</summary>
    public void AddPoints(string world, string holder, string name, double amount)
    {
        if (amount == 0 || !_cfg.GetCVar(ColonyCVars.Enabled))
            return;

        var state = World(world);
        if (!state.Holders.TryGetValue(holder, out var held))
        {
            if (amount < 0)
                return;

            held = new HolderState { Key = holder, Updated = DateTime.UtcNow };
            state.Holders[holder] = held;
        }

        if (!string.IsNullOrEmpty(name))
            held.Name = name;
        held.Points = Math.Max(0, held.Points + amount);
        held.Dirty = true;
        UpdateTier(state);
    }

    /// <summary>Takes the daily decay off every score for the days since it was last taken.</summary>
    public void Decay(DateTime now)
    {
        var rate = Math.Clamp(_cfg.GetCVar(ColonyCVars.Decay), 0f, 1f);
        foreach (var world in _worlds.Values)
        {
            foreach (var holder in world.Holders.Values)
            {
                var days = (now - holder.Updated).TotalDays;
                if (days <= 0)
                    continue;

                holder.Points *= Math.Pow(1 - rate, days);
                holder.Updated = now;
                holder.Dirty = true;
            }

            UpdateTier(world);
        }
    }

    private void OnRaidEnded(Entity<WFOutpostComponent> outpost, ref WFRaidEndedEvent args)
    {
        if (!args.Beaten && !args.BrokeIn || !TryWorldOf(outpost.Comp.Ground, out var world, out _))
            return;

        var holder = HolderOf(outpost, out var name);
        var points = _cfg.GetCVar(ColonyCVars.RaidPoints) * (args.Step + 1);
        AddPoints(world, holder, name, args.Beaten ? points : -points);
    }

    /// <summary>A developed world draws more raids: each tier past Settled adds to the raid director's attention.</summary>
    private void OnAttentionScale(Entity<WFOutpostComponent> outpost, ref WFRaidAttentionScaleEvent args)
    {
        if (!TryWorldOf(outpost.Comp.Ground, out var world, out _) || Tier(world) < WFWorldTier.Settled)
            return;

        args.Scale *= 1f + _cfg.GetCVar(ColonyCVars.AttentionPerTier) * ((int) Tier(world) - (int) WFWorldTier.Settled);
    }

    private void OnProduced(Entity<WFOutpostComponent> outpost, ref WFOutpostProducedEvent args)
    {
        if (!TryWorldOf(outpost.Comp.Ground, out var world, out _))
            return;

        var holder = HolderOf(outpost, out var name);
        AddPoints(world, holder, name, args.Value / Math.Max(1f, _cfg.GetCVar(ColonyCVars.ProducerCrateValue)));
    }

    #endregion

    #region Tiers

    /// <summary>The world total a tier needs.</summary>
    public float Threshold(WFWorldTier tier)
    {
        return tier switch
        {
            WFWorldTier.Settled => _cfg.GetCVar(ColonyCVars.SettledTier),
            WFWorldTier.Colony => _cfg.GetCVar(ColonyCVars.ColonyTier),
            WFWorldTier.Charter => _cfg.GetCVar(ColonyCVars.CharterTier),
            WFWorldTier.Dominion => _cfg.GetCVar(ColonyCVars.DominionTier),
            _ => 0f,
        };
    }

    /// <summary>The tier a total reaches; a tier held is kept until the total falls the slack below its threshold.</summary>
    public WFWorldTier TierFor(WFWorldTier current, double total)
    {
        var reached = WFWorldTier.None;
        for (var tier = WFWorldTier.Dominion; tier > WFWorldTier.None; tier--)
        {
            if (total < Threshold(tier))
                continue;

            reached = tier;
            break;
        }

        var keep = 1 - _cfg.GetCVar(ColonyCVars.TierSlack);
        var held = current;
        while (held > reached && total < Threshold(held) * keep)
        {
            held--;
        }

        return held > reached ? held : reached;
    }

    private void UpdateTier(WorldState world)
    {
        var tier = TierFor(world.Tier, world.Total);
        var floor = BaseTier(world.Id);
        if (tier < floor)
            tier = floor;
        if (tier == world.Tier)
            return;

        var old = world.Tier;
        world.Tier = tier;
        OnTierChanged(world, old);
    }

    private void OnTierChanged(WorldState world, WFWorldTier old)
    {
        _log.Info($"World {world.Id} went from {old} to {world.Tier} at {world.Total:F0} points.");
        if (world.Tier > old && world.Tier >= WFWorldTier.Colony)
        {
            _market.Radio(Loc.GetString("wf-colony-registered", ("world", WorldName(world.Id)),
                ("tier", Loc.GetString($"wf-colony-tier-{world.Tier.ToString().ToLowerInvariant()}"))));
        }

        CheckCharter(world, DateTime.UtcNow, daily: false);
        RefreshTerritory(world);
    }

    #endregion

    #region Ticks

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!Loaded || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickInterval;
        Tick((float) TickInterval.TotalSeconds);

        if (now < _nextFlush)
            return;

        _nextFlush = now + TimeSpan.FromSeconds(_cfg.GetCVar(ColonyCVars.FlushInterval));
        Decay(DateTime.UtcNow);
        CheckCharters();
        WriteDirty();
    }

    /// <summary>One step of the colonies: home points, outposts' companies, the colony stations and their territory.</summary>
    public void Tick(float seconds)
    {
        if (!_cfg.GetCVar(ColonyCVars.Enabled))
            return;

        var entries = Outposts();
        var hours = seconds / 3600f;
        var cap = _cfg.GetCVar(ColonyCVars.HomeOutposts);
        var rate = _cfg.GetCVar(ColonyCVars.HomePoints);
        foreach (var group in entries.Where(entry => entry.Home).GroupBy(entry => (entry.World, entry.Holder)))
        {
            AddPoints(group.Key.World, group.Key.Holder, group.First().Name, rate * hours * Math.Min(cap, group.Count()));
        }

        foreach (var entry in entries)
        {
            var member = EnsureComp<WFColonyMemberComponent>(entry.Outpost);
            if (entry.Home)
                member.HomeMinutes += seconds / 60f;
            if (member.Lot == 0)
                NoteLot((entry.Outpost.Owner, member));
            if (CompanyOfKey(entry.Holder) is { } company)
                member.CompanyId = company;
            else if (CompOrNull<WFShipAccessComponent>(entry.Outpost)?.Mode != WFShipAccessMode.Company)
                member.CompanyId = null;
            UpdateHeritage((entry.Outpost.Owner, member));
        }

        foreach (var (world, network) in Networks())
        {
            SeedDeposits(network);
            var state = World(world);
            UpdateColony(state, network, entries.Where(entry => entry.World == world).ToList(), seconds);
        }

        UpdateTerritory(entries);
    }

    /// <summary>Every live outpost on a world, with who it earns for and whether someone is home.</summary>
    private List<OutpostEntry> Outposts()
    {
        var entries = new List<OutpostEntry>();
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var outpost, out var grid))
        {
            if (outpost.Abandoned || TerminatingOrDeleted(uid) || !TryWorldOf(outpost.Ground, out var world, out var network))
                continue;

            var holder = HolderOf((uid, outpost), out var name);
            entries.Add(new OutpostEntry((uid, outpost, grid), world, network, holder, name, _director.SomeoneHome((uid, outpost, grid))));
        }

        return entries;
    }

    /// <summary>The worlds with a planet network this round.</summary>
    public List<(string World, EntityUid Network)> Networks()
    {
        var networks = new List<(string, EntityUid)>();
        var query = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (query.MoveNext(out var uid, out var network))
        {
            if (!TerminatingOrDeleted(uid))
                networks.Add((network.Surface.Id, uid));
        }

        return networks;
    }

    /// <summary>The world a ground map belongs to and its planet network.</summary>
    public bool TryWorldOf(EntityUid ground, out string world, out EntityUid network)
    {
        world = string.Empty;
        network = default;
        if (!_weather.TryGetWorld(ground, out network) || !TryComp<WFPlanetNetworkComponent>(network, out var comp))
            return false;

        world = comp.Surface.Id;
        return true;
    }

    /// <summary>A world's name as players know it: its sector body's, when it has one this round.</summary>
    public string WorldName(string world)
    {
        foreach (var (id, network) in Networks())
        {
            if (id == world && Comp<WFPlanetNetworkComponent>(network).Planet is { } body && !TerminatingOrDeleted(body))
                return Name(body);
        }

        return world;
    }

    private void OnRestart(RoundRestartCleanupEvent args)
    {
        if (Loaded)
        {
            Decay(DateTime.UtcNow);
            WriteDirty();
        }

        foreach (var world in _worlds.Values)
        {
            world.Applicants.Clear();
        }

        _seeded.Clear();

        _nextTick = TimeSpan.Zero;
        _nextFlush = TimeSpan.Zero;
    }

    /// <summary>Writes every changed score and charter.</summary>
    private void WriteDirty()
    {
        if (!Loaded)
            return;

        var rows = new List<WFStoredDevelopment>();
        foreach (var world in _worlds.Values)
        {
            foreach (var holder in world.Holders.Values.Where(holder => holder.Dirty))
            {
                holder.Dirty = false;
                rows.Add(new WFStoredDevelopment(holder.Key, world.Id, holder.Name, holder.Points, holder.Updated));
            }

            if (world.Charter is { Dirty: true } charter)
            {
                charter.Dirty = false;
                Track(_db.SetWorldCharterAsync(world.Id, StoredCharter(world.Id, charter)));
            }
        }

        if (rows.Count > 0)
            Track(_db.SetWorldDevelopmentAsync(rows));
    }

    private void Track(Task task)
    {
        _pendingWrites.RemoveAll(pending => pending.IsCompleted);
        _pendingWrites.Add(task.ContinueWith(done =>
        {
            if (done.Exception != null)
                _log.Error($"World development write failed: {done.Exception}");
        }, TaskScheduler.Default));
    }

    #endregion

    #region Outpost saves

    /// <summary>A loaded save relinks to the company it earned for, so its holder carries over between rounds.</summary>
    private void OnSettled(Entity<WFColonyMemberComponent> member, ref WFOutpostSettledEvent args)
    {
        member.Comp.HomeMinutes = 0;
        NoteLot(member);
        if (member.Comp.CompanyId is not { } company || !TryComp<WFShipAccessComponent>(member, out var access)
            || access.Mode != WFShipAccessMode.Company || access.CompanyId != null || _companies.CompanyIdentity(company) is not { } identity)
            return;

        _shipAccess.SetCompany((member.Owner, access), company, identity.Name, Array.Empty<WFShipAccessKey>());
    }

    private void OnFingerprint(Entity<WFColonyMemberComponent> member, ref WFOutpostFingerprintEvent args)
    {
        args.Hash = HashCode.Combine(args.Hash, member.Comp.CompanyId, member.Comp.Heritage, member.Comp.Lot);
    }

    /// <summary>Remembers which of its world's outpost lots an outpost came down on, so it is set down there again.</summary>
    private void NoteLot(Entity<WFColonyMemberComponent> member)
    {
        if (!TryComp<WFOutpostComponent>(member, out var outpost) || TerminatingOrDeleted(outpost.Console)
            || !TryComp<PlanetPois.WFPlanetPoiComponent>(outpost.Ground, out var pois)
            || !TryComp<MapGridComponent>(outpost.Ground, out var ground))
            return;

        var tile = _maps.TileIndicesFor(outpost.Ground, ground, _transform.GetMapCoordinates(outpost.Console));
        for (var i = 0; i < pois.OutpostLots.Count; i++)
        {
            var lot = pois.OutpostLots[i];
            if (tile.X >= lot.Left && tile.X < lot.Right && tile.Y >= lot.Bottom && tile.Y < lot.Top)
                member.Comp.Lot = i + 1;
        }
    }

    private void OnTransferred(Entity<WFColonyMemberComponent> member, ref WFOutpostTransferredEvent args)
    {
        member.Comp.CompanyId = null;
        member.Comp.Lot = 0;
        member.Comp.Heritage = 0;
        member.Comp.HeritageRound = 0;
        member.Comp.HomeMinutes = 0;
    }

    #endregion

    #region Tests

    /// <summary>Zeroes every score on a world, which also ends its charter; tests share one server's worlds.</summary>
    public void ClearWorldForTest(string world)
    {
        if (!_worlds.TryGetValue(world, out var state))
            return;

        foreach (var holder in state.Holders.Values)
        {
            holder.Points = 0;
            holder.Dirty = true;
        }

        UpdateTier(state);
    }

    #endregion
}
