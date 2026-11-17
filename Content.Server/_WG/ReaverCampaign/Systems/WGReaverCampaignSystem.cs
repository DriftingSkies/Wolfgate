using System.Numerics;
using Content.Server._NF.GameTicking.Events;
using Content.Server._WG.CargoSales;
using Content.Server._WG.Encounters;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Server._WG.SectorControl;
using Content.Server._WG.SectorControl.Systems;
using Content.Server._WG.ShipPa;
using Content.Server._WG.TradeGoods;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Radio.EntitySystems;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.ReaverCampaign;
using Content.Shared._WG.SectorControl;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.ReaverCampaign.Systems;

/// <summary>What keeps the Reaver campaign from acting, in the order it is checked.</summary>
public enum WGReaverIdle : byte
{
    None,
    CampaignOff,
    EncountersOff,
    SectorControlOff,
    NotInRound,
    NoCampaign,
}

/// <summary>The Ashfall Reaver campaign: strongholds that hold sector cells, spread, patrol and raid, and raise a threat tier.</summary>
public sealed partial class WGReaverCampaignSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private ShipPaSystem _shipPa = default!;
    [Dependency] private WGEncounterSystem _encounters = default!;
    [Dependency] private WGEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WGEncounterRewardSystem _rewards = default!;
    [Dependency] private WGSectorTerritorySystem _territory = default!;
    [Dependency] private WGSectorActivitySystem _activity = default!;
    [Dependency] private WGSectorProtectionSystem _protection = default!;
    [Dependency] private WGSectorSyncSystem _sync = default!;
    [Dependency] private WGSectorNoticeSystem _notices = default!;
    [Dependency] private WGCrewObjectiveSystem _objectives = default!;
    [Dependency] private WGCrewShipStatusSystem _status = default!;
    [Dependency] private DockingSystem _docking = default!;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>How soon a founding that found no place is tried again.</summary>
    private static readonly TimeSpan FoundRetry = TimeSpan.FromMinutes(1);

    private bool _enabled;
    private bool _encountersEnabled;
    private bool _campaignDirty = true;
    private WGReaverCampaignPrototype? _campaign;
    private string? _missingFor;
    private TimeSpan _nextTick;
    private TimeSpan _lastTick;
    private TimeSpan _nextFoundTry;
    private TimeSpan? _stationsAt;

    /// <summary>The faction whose legend line is up, so it can be taken down.</summary>
    private ProtoId<WGSectorFactionPrototype>? _legend;

    /// <summary>Stops founding, spread, patrols and raids and holds every clock until resumed; strongholds still break and pay.</summary>
    public new bool Paused;

    /// <summary>When the sector's stations were generated this round.</summary>
    public TimeSpan? StationsAt => _stationsAt;

    /// <summary>The campaign of the storyteller's preset, or null if it has none.</summary>
    public WGReaverCampaignPrototype? Campaign
    {
        get
        {
            if (!_campaignDirty)
                return _campaign;

            _campaignDirty = false;
            _campaign = null;
            if (_scheduler.Preset is not { } preset)
                return null;

            foreach (var campaign in _prototypes.EnumeratePrototypes<WGReaverCampaignPrototype>())
            {
                if (campaign.Preset != preset.ID)
                    continue;

                _campaign = campaign;
                break;
            }

            if (_campaign == null && _missingFor != preset.ID)
            {
                _missingFor = preset.ID;
                Log.Info($"Encounter preset {preset.ID} has no Reaver campaign; none runs.");
            }

            return _campaign;
        }
    }

    /// <summary>Whether the campaign acts: every switch on, the round in progress and a campaign for the preset.</summary>
    public bool Running => Idle() == WGReaverIdle.None;

    /// <summary>The first thing that keeps the campaign from acting, or none while it runs.</summary>
    public WGReaverIdle Idle()
    {
        if (!_enabled && !_ticker.IsGameRuleActive<WGReaverCampaignRuleComponent>())
            return WGReaverIdle.CampaignOff;
        if (!_encountersEnabled)
            return WGReaverIdle.EncountersOff;
        if (!_territory.Enabled)
            return WGReaverIdle.SectorControlOff;
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return WGReaverIdle.NotInRound;

        return Campaign == null ? WGReaverIdle.NoCampaign : WGReaverIdle.None;
    }

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, ReaverCampaignCVars.Enabled, value => _enabled = value, true);
        Subs.CVar(_config, ReaverCampaignCVars.PopulationOverride, value => _populationOverride = value, true);
        Subs.CVar(_config, EncountersCVars.Enabled, value => _encountersEnabled = value, true);
        Subs.CVar(_config, EncountersCVars.Preset, _ => _campaignDirty = true, true);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<StationsGeneratedEvent>(OnStationsGenerated);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<WGEncounterResolvedEvent>(OnResolved);
        SubscribeLocalEvent<WGPilotDockFailedEvent>(OnDockFailed);
        SubscribeLocalEvent<WGEncounterWeightEvent>(OnWeight);
        SubscribeLocalEvent<WGEncounterThreatTierEvent>(OnThreatTier);
        SubscribeLocalEvent<WGEncounterShipSpawnedEvent>(OnShipSpawned);
        SubscribeLocalEvent<WGEncounterSimulatedHitEvent>(OnSimulatedHit);
        SubscribeLocalEvent<WGCrewFriendlyFireQueryEvent>(OnFriendlyFireQuery);
        SubscribeLocalEvent<WGCrewAlertEvent>(OnCrewAlert);
        SubscribeLocalEvent<WGEncounterShipSeizedEvent>(OnShipSeized);
        SubscribeLocalEvent<WGSectorResetEvent>(OnSectorReset);
        SubscribeLocalEvent<WGSectorRetakeAttemptEvent>(OnRetakeAttempt);
        SubscribeLocalEvent<WGSectorCellRetakenEvent>(OnRetaken);
        SubscribeLocalEvent<WGSectorCellClearingEvent>(OnClearing);
        SubscribeLocalEvent<WGSectorCellContestedEvent>(OnCellContested);
        SubscribeLocalEvent<WGSectorContestResolvedEvent>(OnContestResolved);
        SubscribeLocalEvent<WGTradeSaleEvent>(OnTradeSale);
        SubscribeLocalEvent<WGPalletSaleEvent>(OnPalletSale);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WGReaverCampaignPrototype>())
            _campaignDirty = true;
    }

    private void OnStationsGenerated(StationsGeneratedEvent args)
    {
        _stationsAt = _timing.CurTime;
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _stationsAt = null;
        _nextTick = TimeSpan.Zero;
        _lastTick = TimeSpan.Zero;
        _nextFoundTry = TimeSpan.Zero;
        _nextCitadelTry = TimeSpan.Zero;
        _legend = null;
        _clearingPending.Clear();
        _campaignDirty = true;
        Paused = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (_clearingPending.Count > 0)
            AnnounceClearing();

        if (now < _nextTick)
            return;

        var elapsed = _lastTick == TimeSpan.Zero ? 1f : Math.Clamp((float) (now - _lastTick).TotalSeconds, 0f, 5f);
        var since = _lastTick == TimeSpan.Zero ? TimeSpan.Zero : now - _lastTick;
        _lastTick = now;
        _nextTick = now + TickInterval;
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        // Strongholds already standing still break and pay with the campaign switched off. A campaign that is
        // switched off, paused or held by the event runner has its clocks pushed on, so none falls due on resuming.
        var running = Running;
        var held = !running || Held;
        var current = State(running);
        if (held)
            HoldClocks(since, current);

        if (current is not { } state)
        {
            ClearLegend();
            return;
        }

        SamplePopulation(state);
        var ships = Crewed();
        if (CampaignFor(state) is { } reporting)
            ReportPresence(reporting);

        if (!held)
            HoldDisputed(TimeSpan.FromSeconds(elapsed));

        TendStrongholds(state, ships, elapsed);
        TendConvoys(state, !held);
        TendReinforcements();
        TendBatteries(CampaignFor(state));
        TendPatrols(state);
        TendRaids();
        TendResponses();
        TendIntercepts(state);
        TendMarks();
        if (!running || Campaign is not { } campaign)
        {
            ClearLegend();
            return;
        }

        // A finished campaign founds, launches and sends nothing more; what stands fights on until broken.
        if (!held && !state.Finished)
        {
            TickFounding(campaign, state);
            TickCitadel(campaign, state);
            TickReinforce(campaign, state);
            TickBatteries(campaign, state);
            TickSpread(campaign, state);
            TickPatrols(campaign, state);
            TickIntercepts(campaign, state, ships);
            TickRaids(campaign, state);
            TickFunding(campaign, state);
        }

        Recompute();
    }

    /// <summary>The campaign state on the sector map, made there if asked and a campaign is in force.</summary>
    public WGReaverCampaignComponent? State(bool create = false)
    {
        var map = _territory.SectorMap;
        if (map == MapId.Nullspace || !_map.TryGetMap(map, out var mapUid) || mapUid is not { } uid)
            return null;

        var campaign = Campaign;
        if (TryComp<WGReaverCampaignComponent>(uid, out var state))
        {
            if (campaign != null)
                state.Campaign = campaign.ID;
            return state;
        }

        if (!create || campaign == null)
            return null;

        state = EnsureComp<WGReaverCampaignComponent>(uid);
        state.Campaign = campaign.ID;
        SamplePopulation(state);
        return state;
    }

    /// <summary>The campaign in force, or else the one the round's state was made for.</summary>
    private WGReaverCampaignPrototype? CampaignFor(WGReaverCampaignComponent? state)
    {
        if (Campaign is { } campaign)
            return campaign;

        return state != null && _prototypes.TryIndex(state.Campaign, out var held) ? held : null;
    }

    /// <summary>The strongholds standing on the sector map.</summary>
    public List<Entity<WGReaverStrongholdComponent, WGEncounterComponent>> Strongholds()
    {
        var map = _territory.SectorMap;
        var strongholds = new List<Entity<WGReaverStrongholdComponent, WGEncounterComponent>>();
        var query = EntityQueryEnumerator<WGReaverStrongholdComponent, WGEncounterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var stronghold, out var encounter, out var xform))
        {
            if (encounter.Resolution == null && xform.MapID == map && !TerminatingOrDeleted(uid))
                strongholds.Add((uid, stronghold, encounter));
        }

        return strongholds;
    }

    /// <summary>The patrols out on the sector map.</summary>
    public List<Entity<WGReaverPatrolComponent, WGEncounterComponent>> Patrols()
    {
        var map = _territory.SectorMap;
        var patrols = new List<Entity<WGReaverPatrolComponent, WGEncounterComponent>>();
        var query = EntityQueryEnumerator<WGReaverPatrolComponent, WGEncounterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var patrol, out var encounter, out var xform))
        {
            if (encounter.Resolution == null && xform.MapID == map && !TerminatingOrDeleted(uid))
                patrols.Add((uid, patrol, encounter));
        }

        return patrols;
    }

    /// <summary>How many raids are out.</summary>
    public int ActiveRaids()
    {
        var count = 0;
        var query = EntityQueryEnumerator<WGReaverRaidComponent, WGEncounterComponent>();
        while (query.MoveNext(out _, out _, out var encounter))
        {
            if (encounter.Resolution == null)
                count++;
        }

        return count;
    }

    /// <summary>Where a campaign encounter is: its ship of that key, or its origin once the ship is gone.</summary>
    private Vector2 Position(WGEncounterComponent encounter, string key)
    {
        return encounter.Ships.TryGetValue(key, out var ship) && !TerminatingOrDeleted(ship.Grid)
            ? _transform.GetWorldPosition(ship.Grid)
            : encounter.Origin.Position;
    }

    /// <summary>A value of a by-tier list, the last entry serving higher tiers.</summary>
    private static T ByTier<T>(List<T> values, int tier, T fallback)
    {
        return values.Count == 0 ? fallback : values[Math.Clamp(tier, 0, values.Count - 1)];
    }

    /// <summary>The players connected, for the caps and garrison sizes.</summary>
    private int Players()
    {
        return _playerManager.PlayerCount;
    }

    /// <summary>The living, non-ghost players aboard each crewed ship.</summary>
    private Dictionary<EntityUid, int> Crews(HashSet<EntityUid> ships)
    {
        var crews = new Dictionary<EntityUid, int>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && ships.Contains(grid) && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                crews[grid] = crews.GetValueOrDefault(grid) + 1;
        }

        return crews;
    }

    /// <summary>A clear point at a random distance from a centre, or false after a few tries.</summary>
    private bool TryClearPoint(MapId map, Vector2 centre, float min, float max, float clearance, out Vector2 point)
    {
        for (var i = 0; i < 8; i++)
        {
            point = centre + _random.NextAngle().ToVec() * _random.NextFloat(min, max);
            if (_scheduler.IsClearSpace(map, point, clearance, 0f))
                return true;
        }

        point = default;
        return false;
    }

    /// <summary>Picks one entry, each as likely as its weight; null if none has any.</summary>
    private T? PickWeighted<T>(List<(T Item, float Weight)> entries) where T : struct
    {
        var total = 0f;
        foreach (var (_, weight) in entries)
        {
            total += MathF.Max(0f, weight);
        }

        if (total <= 0f)
            return null;

        var roll = _random.NextFloat(total);
        foreach (var (item, weight) in entries)
        {
            roll -= MathF.Max(0f, weight);
            if (roll <= 0f)
                return item;
        }

        return entries[^1].Item;
    }

    private void OnResolved(ref WGEncounterResolvedEvent args)
    {
        if (TryComp<WGReaverStrongholdComponent>(args.Encounter, out var stronghold))
            StrongholdResolved((args.Encounter, stronghold), args.Resolution);
        else if (TryComp<WGReaverPatrolComponent>(args.Encounter, out var patrol))
            PatrolResolved((args.Encounter, patrol), args.Resolution);
        else if (TryComp<WGReaverConvoyComponent>(args.Encounter, out var convoy))
            ConvoyResolved((args.Encounter, convoy), args.Resolution);
        else if (TryComp<WGReaverReinforcementComponent>(args.Encounter, out var reinforcement))
            ReinforcementResolved((args.Encounter, reinforcement), args.Resolution);
        else if (TryComp<WGReaverResponseComponent>(args.Encounter, out var response))
            ResponseResolved((args.Encounter, response), args.Resolution);
        else if (TryComp<WGReaverBatteryComponent>(args.Encounter, out var battery))
            BatteryResolved((args.Encounter, battery), args.Resolution);
    }

    /// <summary>Scales the storyteller's own Reaver encounters by the tier, within the campaign's cap.</summary>
    private void OnWeight(ref WGEncounterWeightEvent args)
    {
        if (!Running || Campaign is not { } campaign || State() is not { } state
            || !campaign.ScaledEncounters.Contains(args.Prototype.ID))
            return;

        args.Weight *= MathF.Min(ByTier(campaign.ThreatScale, state.Tier, 1f), campaign.MaxThreatScale);
    }

    private void OnRoundEndText(RoundEndTextAppendEvent args)
    {
        if (State() is not { Founded: > 0 } state)
            return;

        args.AddLine(Loc.GetString("wg-reaver-round-end",
            ("peak", state.PeakCells),
            ("founded", state.Founded),
            ("broken", state.Broken),
            ("patrols", state.PatrolsDestroyed),
            ("convoys", state.ConvoysDestroyed),
            ("raids", state.Raids),
            ("retaken", state.Retaken),
            ("citadels", state.Citadels > 0
                ? Loc.GetString("wg-reaver-round-end-citadels", ("raised", state.Citadels), ("fallen", state.CitadelsBroken))
                : string.Empty)));
        if (state.Finished)
            args.AddLine(Loc.GetString("wg-reaver-round-end-finished"));
        else if (state.Defeats > 0)
            args.AddLine(Loc.GetString("wg-reaver-round-end-broken", ("count", state.Defeats)));
        if (state.CellsLost > 0)
            args.AddLine(Loc.GetString("wg-reaver-round-end-lost", ("count", state.CellsLost)));
    }

    /// <summary>Ends every campaign encounter, frees the faction's cells, lifts a finished campaign and starts the regroup clock. Returns how many encounters ended.</summary>
    public int ClearCampaign()
    {
        var ended = new List<EntityUid>();
        var query = EntityQueryEnumerator<WGEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Resolution == null && (HasComp<WGReaverStrongholdComponent>(uid) || HasComp<WGReaverConvoyComponent>(uid)
                    || HasComp<WGReaverPatrolComponent>(uid) || HasComp<WGReaverRaidComponent>(uid)
                    || HasComp<WGReaverReinforcementComponent>(uid) || HasComp<WGReaverResponseComponent>(uid)
                    || HasComp<WGReaverBatteryComponent>(uid)))
                ended.Add(uid);
        }

        foreach (var uid in ended)
        {
            _encounters.End(uid);
        }

        var state = State();
        if (CampaignFor(state) is { } campaign)
            _territory.Clear(_territory.SectorMap, campaign.Faction);

        if (state != null)
        {
            state.Finished = false;
            state.NoStrongholdSince = state.Founded > 0 ? _timing.CurTime : null;
            state.NextSpread = null;
            state.NextRaid = null;
        }

        Recompute();
        return ended.Count;
    }
}
