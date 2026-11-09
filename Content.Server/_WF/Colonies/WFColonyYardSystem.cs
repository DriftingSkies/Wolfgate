using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._Mono.Cleanup;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.Companies;
using Content.Server._WF.Outposts;
using Content.Server._WF.Planets;
using Content.Server._WF.ShipAccess;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.ShipAccess;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Timing;

namespace Content.Server._WF.Colonies;

/// <summary>
/// The orbital yard: at Dominion tier a world's governor deploys a platform in its orbit. Companies with a berth there lay
/// down yard designs, feed each stage Ship Components and Metals crates at the yard chute, and the platform's cranes work
/// them through. A finished hull spawns beside its berth with a deed to its company, and its first one grants the company a
/// Hangar licence. The platform and the builds are saved, so a hull can take several rounds.
/// </summary>
public sealed partial class WFColonyYardSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private AdminVesselSpawnSystem _vessels = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WFColonySystem _colonies = default!;
    [Dependency] private WFCompanySystem _companies = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;
    [Dependency] private WFShipAccessServerSystem _shipAccess = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;

    /// <summary>Builds a yard works at once; the platform's cranes are shared between them.</summary>
    public const int Berths = 2;

    public const string ComponentsGood = "TradeGoodShipComponents";
    public const string MetalsGood = "TradeGoodMetals";

    /// <summary>The tag a vessel needs to be a yard design: sold by no shipyard and given by no voucher.</summary>
    public static readonly ProtoId<TagPrototype> YardOnlyTag = "WFAdminOnlyVessel";

    private const int PlatformWidth = 24;
    private const int PlatformHeight = 4;
    private const string PlatformTile = "Plating";
    private const string ConsolePrototype = "WFColonyYardConsole";
    private const string ChutePrototype = "WFColonyYardChute";
    private const string CranePrototype = "WFDrydockCrane";

    /// <summary>The tiles along the platform's lower edge the cranes stand on; their arms reach down past it.</summary>
    private static readonly int[] CraneColumns = { 1, 6, 16, 21 };

    /// <summary>Tiles kept clear around a platform as it deploys.</summary>
    private const float Clearance = 4f;

    /// <summary>Tiles between the platform and a finished hull.</summary>
    private const float HullGap = 3f;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WriteInterval = TimeSpan.FromMinutes(1);

    private ISawmill _log = default!;
    private Task? _loading;
    private readonly List<Task> _pendingWrites = new();
    private readonly Dictionary<string, YardState> _yards = new();
    private readonly List<JobState> _jobs = new();

    /// <summary>Worlds whose governor took a platform kit this round, so one is issued at a time.</summary>
    private readonly HashSet<string> _kitsIssued = new();

    private TimeSpan _nextTick;
    private TimeSpan _nextWrite;

    /// <summary>Whether the yards and builds have been read from the database.</summary>
    public bool Loaded { get; private set; }

    private sealed class YardState(string world)
    {
        public readonly string World = world;
        public Vector2 Position;
        public byte[]? Data;
        public EntityUid? Platform;

        /// <summary>Whether the platform was placed this round, so a failed load isn't retried every tick.</summary>
        public bool Placed;

        public TimeSpan NextSave;
    }

    private sealed class JobState
    {
        public int Id;
        public string World = string.Empty;
        public int CompanyId;
        public string Design = string.Empty;
        public int Stage;
        public float Progress;
        public int Components;
        public int Metals;
        public bool Dirty;

        /// <summary>Whether its first write is still out, so it has no id yet.</summary>
        public bool Writing;

        /// <summary>Its frame standing below the platform this round, how many pieces of it are up, and where the finished hull will reach.</summary>
        public EntityUid? Frame;
        public int Pieces;
        public Box2? FrameBox;

        /// <summary>Where the finished vessel is loaded to stand in the frame's place.</summary>
        public Vector2 FrameAt;
    }

    public override void Initialize()
    {
        base.Initialize();
        _log = Logger.GetSawmill("wf.colonies.yard");
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<WFColonyCharterChangedEvent>(OnCharterChanged);
        SubscribeLocalEvent<WFColonyYardKitComponent, UseInHandEvent>(OnKitUsed);
        SubscribeLocalEvent<WFColonyYardChuteComponent, InteractUsingEvent>(OnChuteInteract);
        Subs.BuiEvents<WFColonyYardComponent>(WFYardConsoleUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnConsoleOpened);
            subs.Event<WFYardOrderMessage>(OnOrder);
            subs.Event<WFYardScrapMessage>(OnScrap);
        });
        _loading = Reload();
    }

    #region Persistence

    /// <summary>Waits for loading and every pending write; for tests and admin tools.</summary>
    public async Task Flush()
    {
        if (_loading != null)
            await _loading;
        WriteJobs();
        while (_pendingWrites.Count > 0)
        {
            var pending = _pendingWrites.ToArray();
            await Task.WhenAll(pending);
            _pendingWrites.RemoveAll(task => pending.Contains(task));
        }
    }

    private async Task Reload()
    {
        List<(WFStoredYard Yard, byte[] Data)> yards;
        List<WFStoredYardJob> jobs;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                yards = await _db.GetWorldYardsAsync();
                jobs = await _db.GetYardJobsAsync();
                break;
            }
            // A synchronous database refuses overlapping requests, such as another system's write still finishing; retry.
            catch (InvalidOperationException) when (attempt < 60)
            {
                await Task.Delay(50);
            }
            catch (Exception e)
            {
                _log.Error($"Could not load the yards: {e}");
                return;
            }
        }

        foreach (var (yard, data) in yards)
        {
            if (_yards.ContainsKey(yard.World))
                continue;

            _yards[yard.World] = new YardState(yard.World) { Position = new Vector2(yard.X, yard.Y), Data = data };
        }

        foreach (var job in jobs)
        {
            if (_jobs.Any(held => held.Id == job.Id))
                continue;

            _jobs.Add(new JobState
            {
                Id = job.Id, World = job.World, CompanyId = job.CompanyId, Design = job.Design, Stage = job.Stage,
                Progress = job.Progress, Components = job.Components, Metals = job.Metals,
            });
        }

        Loaded = true;
    }

    private void Track(Task task)
    {
        _pendingWrites.RemoveAll(pending => pending.IsCompleted);
        _pendingWrites.Add(task.ContinueWith(done =>
        {
            if (done.Exception != null)
                _log.Error($"Yard write failed: {done.Exception}");
        }, TaskScheduler.Default));
    }

    /// <summary>Writes every build whose stage or progress changed.</summary>
    private void WriteJobs()
    {
        if (!Loaded)
            return;

        foreach (var job in _jobs.Where(job => job.Dirty && !job.Writing))
        {
            WriteJob(job);
        }
    }

    private void WriteJob(JobState job)
    {
        job.Dirty = false;
        if (job.Id != 0)
        {
            Track(_db.SetYardJobAsync(Stored(job)));
            return;
        }

        job.Writing = true;
        Track(InsertJob(job));
    }

    private async Task InsertJob(JobState job)
    {
        try
        {
            job.Id = await _db.SetYardJobAsync(Stored(job));
        }
        finally
        {
            job.Writing = false;
        }

        // Scrapped or finished while its first write was out.
        if (!_jobs.Contains(job))
            Track(_db.DeleteYardJobAsync(job.Id));
        else if (job.Dirty)
            WriteJob(job);
    }

    private static WFStoredYardJob Stored(JobState job) =>
        new(job.Id, job.World, job.CompanyId, job.Design, job.Stage, job.Progress, job.Components, job.Metals);

    private void RemoveJob(JobState job)
    {
        ClearFrame(job);
        _jobs.Remove(job);
        if (job.Id != 0)
            Track(_db.DeleteYardJobAsync(job.Id));
    }

    /// <summary>Saves a world's platform grid where it stands.</summary>
    public bool SaveYard(string world)
    {
        if (!_yards.TryGetValue(world, out var yard) || yard.Platform is not { } platform || TerminatingOrDeleted(platform))
            return false;

        var options = SerializationOptions.Default with
        {
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
            LogAutoInclude = null,
            Category = FileCategory.Grid,
        };

        MappingDataNode node;
        try
        {
            FileCategory category;
            (node, category) = _mapLoader.SerializeEntitiesRecursive([platform], options);
            if (category != FileCategory.Grid)
                throw new InvalidOperationException($"Saved as {category}, not as a grid.");
        }
        catch (Exception e)
        {
            _log.Error($"Failed to save the yard of {world}: {e}");
            return false;
        }

        yard.Position = _transform.GetWorldPosition(platform);
        yard.NextSave = _timing.CurTime + SaveInterval;
        var stored = new WFStoredYard(world, yard.Position.X, yard.Position.Y, string.Empty, DateTime.UtcNow);
        var name = Name(platform);
        Track(StoreYard(yard, stored, node, name));
        return true;
    }

    private async Task StoreYard(YardState yard, WFStoredYard stored, MappingDataNode node, string name)
    {
        var packed = await Task.Run(() => _shipyard.WFWriteCopy(node, name) is { } yaml ? WFOutpostSystem.Pack(yaml) : null);
        if (packed == null)
        {
            _log.Error($"Failed to write the yard of {yard.World}.");
            return;
        }

        yard.Data = packed;
        await _db.SetWorldYardAsync(stored, packed);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        if (args.New != GameRunLevel.PostRound)
            return;

        foreach (var world in _yards.Keys.ToList())
        {
            SaveYard(world);
        }

        WriteJobs();
    }

    private void OnRestart(RoundRestartCleanupEvent args)
    {
        WriteJobs();
        foreach (var yard in _yards.Values)
        {
            yard.Platform = null;
            yard.Placed = false;
        }

        _kitsIssued.Clear();
        ForgetFrames();
        _nextTick = TimeSpan.Zero;
        _nextWrite = TimeSpan.Zero;
    }

    #endregion

    #region Platform

    /// <summary>Whether a world has a yard platform, placed this round or not.</summary>
    public bool HasYard(string world) => _yards.ContainsKey(world);

    /// <summary>A world's platform grid this round, if it is up.</summary>
    public EntityUid? Platform(string world) =>
        _yards.TryGetValue(world, out var yard) && yard.Platform is { } platform && !TerminatingOrDeleted(platform) ? platform : null;

    /// <summary>Whether a platform kit may go to a world's governor: at Dominion, with no platform and no kit out this round.</summary>
    public bool MayIssueKit(string world) =>
        Loaded && !HasYard(world) && !_kitsIssued.Contains(world) && _colonies.Tier(world) >= WFWorldTier.Dominion;

    /// <summary>Marks a world's kit as issued this round.</summary>
    public void KitIssued(string world) => _kitsIssued.Add(world);

    /// <summary>The world whose orbit layer a map is.</summary>
    private bool TryWorldOfOrbit(EntityUid? map, out string world, out EntityUid network)
    {
        world = string.Empty;
        network = default;
        if (map == null)
            return false;

        foreach (var (id, uid) in _colonies.Networks())
        {
            if (Comp<WFPlanetNetworkComponent>(uid).OrbitMap != map)
                continue;

            world = id;
            network = uid;
            return true;
        }

        return false;
    }

    private void OnKitUsed(Entity<WFColonyYardKitComponent> kit, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = args.User;
        if (!TryWorldOfOrbit(Transform(user).MapUid, out var world, out var network))
        {
            _popup.PopupEntity(Loc.GetString("wf-yard-kit-not-orbit"), user, user);
            return;
        }

        if (!TryComp<ActorComponent>(user, out var actor) || !_colonies.IsGovernor(world, actor.PlayerSession))
        {
            _popup.PopupEntity(Loc.GetString("wf-yard-kit-not-governor", ("world", _colonies.WorldName(world))), user, user);
            return;
        }

        if (_colonies.Tier(world) < WFWorldTier.Dominion)
        {
            _popup.PopupEntity(Loc.GetString("wf-yard-kit-tier"), user, user);
            return;
        }

        if (HasYard(world))
        {
            _popup.PopupEntity(Loc.GetString("wf-yard-kit-exists"), user, user);
            return;
        }

        var map = Transform(Comp<WFPlanetNetworkComponent>(network).OrbitMap).MapID;
        var origin = _transform.GetWorldPosition(user) - new Vector2(PlatformWidth / 2f, PlatformHeight / 2f);
        var footprint = new Box2(origin, origin + new Vector2(PlatformWidth, PlatformHeight)).Enlarged(Clearance);
        var grids = new List<Entity<MapGridComponent>>();
        _maps.FindGridsIntersecting(map, footprint, ref grids, approx: true, includeMap: false);
        if (grids.Count > 0)
        {
            _popup.PopupEntity(Loc.GetString("wf-yard-kit-blocked"), user, user);
            return;
        }

        FoundYard(world, map, origin);
        QueueDel(kit);
        _popup.PopupEntity(Loc.GetString("wf-yard-kit-deployed"), user, user);
        _market.Radio(Loc.GetString("wf-yard-founded", ("world", _colonies.WorldName(world))));
        _log.Info($"{ToPrettyString(user)} deployed the yard of {world}.");
    }

    /// <summary>Lays a world's yard platform and saves it as the world's.</summary>
    public EntityUid FoundYard(string world, MapId map, Vector2 origin)
    {
        var platform = BuildPlatform(world, map, origin);
        _yards[world] = new YardState(world) { Position = origin, Platform = platform, Placed = true };
        SaveYard(world);
        return platform;
    }

    /// <summary>Takes a world's yard down for good: its platform, its save and its builds.</summary>
    public void Demolish(string world)
    {
        if (!_yards.Remove(world, out var yard))
            return;

        if (yard.Platform is { } platform && !TerminatingOrDeleted(platform))
            QueueDel(platform);
        foreach (var job in JobsOn(world))
        {
            RemoveJob(job);
        }

        Track(_db.DeleteWorldYardAsync(world));
        _log.Info($"The yard of {world} was demolished.");
    }

    /// <summary>Lays a new platform: a deck with the cranes along its lower edge and the console and chute along its upper.</summary>
    public EntityUid BuildPlatform(string world, MapId map, Vector2 origin)
    {
        var grid = _maps.CreateGridEntity(map);
        var tile = new Tile(_tiles[PlatformTile].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < PlatformWidth; x++)
        {
            for (var y = 0; y < PlatformHeight; y++)
            {
                tiles.Add((new Vector2i(x, y), tile));
            }
        }

        _maps.SetTiles(grid.Owner, grid.Comp, tiles);
        _transform.SetWorldPosition(grid.Owner, origin);
        _meta.SetEntityName(grid.Owner, Loc.GetString("wf-yard-platform-name", ("world", _colonies.WorldName(world))));

        var console = Spawn(ConsolePrototype, new EntityCoordinates(grid.Owner, new Vector2(PlatformWidth / 2f - 0.5f, PlatformHeight - 0.5f)));
        Spawn(ChutePrototype, new EntityCoordinates(grid.Owner, new Vector2(PlatformWidth / 2f + 1.5f, PlatformHeight - 0.5f)));
        foreach (var column in CraneColumns)
        {
            Spawn(CranePrototype, new EntityCoordinates(grid.Owner, new Vector2(column + 0.5f, 0.5f)));
        }

        Comp<WFColonyYardComponent>(console).World = world;
        SetupPlatform(grid.Owner, world);
        return grid.Owner;
    }

    /// <summary>Makes a platform the world's: fixed in place, never cleaned up, its cranes on its console and its machines on no grid power.</summary>
    private void SetupPlatform(EntityUid grid, string world)
    {
        EnsureComp<WFColonyYardPlatformComponent>(grid).World = world;
        EnsureComp<CleanupImmuneComponent>(grid);
        _physics.SetBodyType(grid, BodyType.Static);

        EntityUid? console = null;
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<WFColonyYardComponent>(child, out var yard))
            {
                yard.World = world;
                console = child;
            }

            if (TryComp<ApcPowerReceiverComponent>(child, out var receiver))
                receiver.NeedsPower = false;
        }

        if (console == null)
            return;

        children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<WFDrydockCraneComponent>(child, out var crane))
                continue;

            crane.LinkedConsole = console;
            Dirty(child, crane);
        }
    }

    /// <summary>Puts a world's saved platform back where it was, once its orbit layer is up.</summary>
    private void PlacePlatform(YardState yard, EntityUid network)
    {
        yard.Placed = true;
        if (yard.Data == null)
            return;

        var map = Transform(Comp<WFPlanetNetworkComponent>(network).OrbitMap).MapID;
        MappingDataNode data;
        try
        {
            using var reader = new StringReader(WFOutpostSystem.Unpack(yard.Data));
            if (DataNodeParser.ParseYamlStream(reader).FirstOrDefault()?.Root is not MappingDataNode root)
                throw new InvalidOperationException("No grid in the save.");
            data = root;
        }
        catch (Exception e)
        {
            _log.Error($"Could not read the yard of {yard.World}: {e}");
            return;
        }

        var options = new MapLoadOptions
        {
            MergeMap = map,
            ExpectedCategory = FileCategory.Grid,
        };

        if (!_mapLoader.TryLoadGeneric(data, $"yard of {yard.World}", out var result, options) || result.Grids.Count != 1)
        {
            _log.Error($"Could not load the yard of {yard.World}.");
            if (result != null)
                _mapLoader.Delete(result);
            return;
        }

        // The save keeps the grid's own position; the stored one is the authority.
        var grid = result.Grids.Single().Owner;
        _transform.SetWorldPosition(grid, yard.Position);
        Strip(grid);
        SetupPlatform(grid, yard.World);
        yard.Platform = grid;
        yard.NextSave = _timing.CurTime + SaveInterval;
    }

    /// <summary>Takes off a loaded platform what a save never keeps: bodies, mobs, cash and the like.</summary>
    private void Strip(EntityUid grid)
    {
        var strip = new List<EntityUid>();
        var pending = new Stack<EntityUid>();
        pending.Push(grid);
        while (pending.TryPop(out var uid))
        {
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (_outposts.IsKept(child))
                    pending.Push(child);
                else
                    strip.Add(child);
            }
        }

        foreach (var uid in strip)
        {
            Del(uid);
        }
    }

    #endregion

    #region Builds

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!Loaded || _ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextTick)
            return;

        var elapsed = _nextTick == TimeSpan.Zero ? 0f : (float) (_timing.CurTime - _nextTick + TickInterval).TotalSeconds;
        _nextTick = _timing.CurTime + TickInterval;

        foreach (var (world, network) in _colonies.Networks())
        {
            if (!_yards.TryGetValue(world, out var yard))
                continue;

            if (!yard.Placed)
                PlacePlatform(yard, network);
            if (yard.Platform is { } platform && !TerminatingOrDeleted(platform))
            {
                Work(world, platform, elapsed);
                if (_timing.CurTime >= yard.NextSave)
                    SaveYard(world);
            }
        }

        if (_timing.CurTime >= _nextWrite)
        {
            _nextWrite = _timing.CurTime + WriteInterval;
            WriteJobs();
        }
    }

    /// <summary>The builds on a world's yard, oldest first; the n-th works the n-th berth.</summary>
    private List<JobState> JobsOn(string world) => _jobs.Where(job => job.World == world).OrderBy(job => job.Id == 0 ? int.MaxValue : job.Id).ToList();

    private int Cranes(EntityUid platform)
    {
        var count = 0;
        var children = Transform(platform).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<WFDrydockCraneComponent>(child))
                count++;
        }

        return count;
    }

    /// <summary>Works every build whose stage has its crates in, the platform's cranes shared between the berths.</summary>
    public void Work(string world, EntityUid platform, float seconds)
    {
        if (seconds <= 0f)
            return;

        var share = Cranes(platform) / (float) Berths;
        if (share <= 0f)
            return;

        var changed = false;
        foreach (var job in JobsOn(world).Take(Berths).ToList())
        {
            if (!_proto.TryIndex<WFYardDesignPrototype>(job.Design, out var design) || job.Stage >= design.Stages.Count)
                continue;

            var stage = design.Stages[job.Stage];
            if (job.Components < stage.Components || job.Metals < stage.Metals)
            {
                // Waiting on its crates: the frame stands as far as the work has got.
                TendFrame(job, design, platform, false);
                continue;
            }

            job.Progress += seconds * share / MathF.Max(1f, stage.Minutes * 60f);
            job.Dirty = true;
            changed = true;
            if (job.Progress >= 1f)
            {
                job.Stage++;
                job.Progress = 0f;
                job.Components = 0;
                job.Metals = 0;
            }

            if (job.Stage >= design.Stages.Count)
            {
                Complete(job, design, platform);
                continue;
            }

            if (job.Progress == 0f)
                WriteJob(job);
            TendFrame(job, design, platform, seconds <= CatchUp);
        }

        if (changed)
            RefreshConsoles(world);
        else
            ParkCranes(platform);
    }

    /// <summary>Spawns a finished hull clear of the platform, deeds it to its company and grants the licence its first build earns.</summary>
    private void Complete(JobState job, WFYardDesignPrototype design, EntityUid platform)
    {
        // The frame makes way for the finished vessel, which stands where it stood.
        var berth = ClearFrame(job);
        RemoveJob(job);
        if (!_proto.TryIndex(design.Vessel, out var vessel))
            return;

        var map = Transform(platform).MapID;
        var platformBox = _transform.GetWorldMatrix(platform).TransformBox(Comp<MapGridComponent>(platform).LocalAABB);
        if (!_vessels.TrySpawnVessel(vessel, map, berth ?? platformBox.Center - new Vector2(0f, 1000f), platform, out var hull))
        {
            _log.Error($"The yard of {job.World} could not spawn {vessel.ID} for company {job.CompanyId}.");
            return;
        }

        if (berth == null)
            PlaceHull(hull.Value, platformBox);
        ParkCranes(platform);
        var companyName = _companies.CompanyIdentity(job.CompanyId)?.Name ?? job.CompanyId.ToString();
        var deeded = Deliverer(job.CompanyId, map) is { } session
            && _vessels.TryGetDeedCard(session, out var card, out _)
            && _vessels.TryAssignOwner(hull.Value, vessel, card, session);
        var access = deeded
            ? (hull.Value, EnsureComp<WFShipAccessComponent>(hull.Value))
            : _shipAccess.SetupRegisteredShip(hull.Value, companyName, Array.Empty<NetUserId>());
        _shipAccess.SetCompany(access, job.CompanyId, companyName, Array.Empty<WFShipAccessKey>());
        _shipAccess.SetMode(access, WFShipAccessMode.Company);
        _log.Info($"The yard of {job.World} finished {ToPrettyString(hull.Value)} for company {job.CompanyId}, deeded: {deeded}.");

        _market.Radio(Loc.GetString("wf-yard-complete", ("company", companyName), ("design", vessel.Name),
            ("world", _colonies.WorldName(job.World))));
        if (_companies.GrantLicence(job.CompanyId, vessel.ID))
            _market.Radio(Loc.GetString("wf-yard-licence", ("company", companyName), ("design", vessel.Name)));
        RefreshConsoles(job.World);
    }

    /// <summary>Moves a new hull below the platform, then further out until it overlaps no other grid.</summary>
    private void PlaceHull(EntityUid hull, Box2 platformBox)
    {
        var map = Transform(hull).MapID;
        var local = Comp<MapGridComponent>(hull).LocalAABB;
        var grids = new List<Entity<MapGridComponent>>();
        var top = platformBox.Bottom - HullGap;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var position = new Vector2(platformBox.Center.X - local.Center.X, top - local.Top);
            var box = local.Translated(position);
            grids.Clear();
            _maps.FindGridsIntersecting(map, box.Enlarged(1f), ref grids, approx: true, includeMap: false);
            grids.RemoveAll(grid => grid.Owner == hull);
            _transform.SetWorldPosition(hull, position);
            if (grids.Count == 0)
                return;

            top = grids.Min(grid => _transform.GetWorldMatrix(grid.Owner).TransformBox(grid.Comp.LocalAABB).Bottom) - HullGap;
        }
    }

    /// <summary>A member of the company on the yard's map, to hold the hull's deed: the one on a platform first.</summary>
    private ICommonSession? Deliverer(int company, MapId map)
    {
        ICommonSession? found = null;
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } body || Transform(body).MapID != map
                || _companies.CompanyOfSession(session)?.Id != company)
                continue;

            if (HasComp<WFColonyYardPlatformComponent>(Transform(body).GridUid))
                return session;
            found ??= session;
        }

        return found;
    }

    /// <summary>Whether a vessel may be a yard design: no shipyard sells it and no voucher gives it.</summary>
    public bool IsYardVessel(VesselPrototype vessel) => vessel.Tags.Contains(YardOnlyTag) && !vessel.Purchasable;

    /// <summary>Why a company can't lay down a build on a world's yard, localised, or null when it can.</summary>
    public string? Refusal(string world, int? company)
    {
        if (company is not { } id)
            return Loc.GetString("wf-yard-reason-company");
        if (_colonies.Tier(world) < WFWorldTier.Dominion)
            return Loc.GetString("wf-yard-reason-tier");
        if (!_colonies.HasBerth(world, id))
            return Loc.GetString("wf-yard-reason-berth");
        if (JobsOn(world).Count >= Berths)
            return Loc.GetString("wf-yard-reason-full");
        return null;
    }

    /// <summary>Lays down a design on a world's yard for a company.</summary>
    public bool TryOrder(string world, int company, string designId, [NotNullWhen(false)] out string? reason)
    {
        reason = Refusal(world, company);
        if (reason != null)
            return false;

        if (!_proto.TryIndex<WFYardDesignPrototype>(designId, out var design) || !_proto.TryIndex(design.Vessel, out var vessel)
            || !IsYardVessel(vessel))
        {
            reason = Loc.GetString("wf-yard-reason-design");
            return false;
        }

        var job = new JobState { World = world, CompanyId = company, Design = design.ID };
        _jobs.Add(job);
        WriteJob(job);
        var name = _companies.CompanyIdentity(company)?.Name ?? company.ToString();
        _market.Radio(Loc.GetString("wf-yard-ordered", ("company", name), ("design", vessel.Name), ("world", _colonies.WorldName(world))));
        RefreshConsoles(world);
        return true;
    }

    /// <summary>Feeds a delivered crate to a build that still needs it this stage: the deliverer's company's first.</summary>
    public bool TryFeed(string world, string good, int? company, out string design)
    {
        design = string.Empty;
        var jobs = JobsOn(world).Take(Berths).ToList();
        foreach (var job in jobs.Where(job => job.CompanyId == company).Concat(jobs.Where(job => job.CompanyId != company)))
        {
            if (!_proto.TryIndex<WFYardDesignPrototype>(job.Design, out var proto) || job.Stage >= proto.Stages.Count)
                continue;

            var stage = proto.Stages[job.Stage];
            if (good == ComponentsGood && job.Components < stage.Components)
                job.Components++;
            else if (good == MetalsGood && job.Metals < stage.Metals)
                job.Metals++;
            else
                continue;

            job.Dirty = true;
            design = _proto.TryIndex(proto.Vessel, out var vessel) ? vessel.Name : proto.ID;
            RefreshConsoles(world);
            return true;
        }

        return false;
    }

    private void OnChuteInteract(Entity<WFColonyYardChuteComponent> chute, ref InteractUsingEvent args)
    {
        if (args.Handled || Transform(chute).GridUid is not { } grid
            || !TryComp<WFColonyYardPlatformComponent>(grid, out var platform))
            return;

        args.Handled = true;
        var good = MetaData(args.Used).EntityPrototype?.ID;
        if (good != ComponentsGood && good != MetalsGood)
        {
            _popup.PopupEntity(Loc.GetString("wf-yard-chute-wrong"), chute, args.User);
            return;
        }

        int? company = TryComp<ActorComponent>(args.User, out var actor) ? _companies.CompanyOfSession(actor.PlayerSession)?.Id : null;
        if (!TryFeed(platform.World, good, company, out var design))
        {
            _popup.PopupEntity(Loc.GetString("wf-yard-chute-unneeded"), chute, args.User);
            return;
        }

        _popup.PopupEntity(Loc.GetString("wf-yard-chute-fed", ("good", _market.GoodName(good)), ("design", design)), chute, args.User);
        QueueDel(args.Used);
    }

    #endregion

    #region Console

    private void OnCharterChanged(ref WFColonyCharterChangedEvent args)
    {
        RefreshConsoles(args.World);
    }

    private void OnConsoleOpened(Entity<WFColonyYardComponent> console, ref BoundUIOpenedEvent args)
    {
        UpdateConsole(console, args.Actor);
    }

    private void OnOrder(Entity<WFColonyYardComponent> console, ref WFYardOrderMessage args)
    {
        if (!TryComp<ActorComponent>(args.Actor, out var actor))
            return;

        var company = _companies.CompanyOfSession(actor.PlayerSession)?.Id;
        if (company == null || !TryOrder(console.Comp.World, company.Value, args.Design, out var reason))
            _popup.PopupEntity(Refusal(console.Comp.World, company) ?? Loc.GetString("wf-yard-reason-design"), console, args.Actor);
    }

    private void OnScrap(Entity<WFColonyYardComponent> console, ref WFYardScrapMessage args)
    {
        if (!TryComp<ActorComponent>(args.Actor, out var actor)
            || _companies.CompanyOfSession(actor.PlayerSession)?.Id is not { } company)
            return;

        var id = args.Job;
        var world = console.Comp.World;
        var job = _jobs.FirstOrDefault(job => job.World == world && job.Id == id && job.CompanyId == company);
        if (job == null)
            return;

        RemoveJob(job);
        RefreshConsoles(console.Comp.World);
    }

    /// <summary>Shows every open yard console on a world afresh.</summary>
    private void RefreshConsoles(string world)
    {
        var query = EntityQueryEnumerator<WFColonyYardComponent>();
        while (query.MoveNext(out var uid, out var yard))
        {
            if (yard.World != world)
                continue;

            foreach (var actor in _ui.GetActors(uid, WFYardConsoleUiKey.Key))
            {
                UpdateConsole((uid, yard), actor);
            }
        }
    }

    private void UpdateConsole(Entity<WFColonyYardComponent> console, EntityUid viewer)
    {
        var world = console.Comp.World;
        int? company = TryComp<ActorComponent>(viewer, out var actor) ? _companies.CompanyOfSession(actor.PlayerSession)?.Id : null;
        var designs = new List<WFYardDesignView>();
        foreach (var design in _proto.EnumeratePrototypes<WFYardDesignPrototype>().OrderBy(design => design.ID))
        {
            if (!_proto.TryIndex(design.Vessel, out var vessel) || !IsYardVessel(vessel))
                continue;

            designs.Add(new WFYardDesignView(design.ID, vessel.Name, vessel.Description, design.Stages.Count,
                design.Stages.Sum(stage => stage.Components), design.Stages.Sum(stage => stage.Metals),
                company is { } id && _companies.HasLicence(id, vessel.ID)));
        }

        var jobs = new List<WFYardJobView>();
        foreach (var job in JobsOn(world))
        {
            if (!_proto.TryIndex<WFYardDesignPrototype>(job.Design, out var design) || job.Stage >= design.Stages.Count)
                continue;

            var stage = design.Stages[job.Stage];
            jobs.Add(new WFYardJobView(job.Id, _companies.CompanyIdentity(job.CompanyId)?.Name ?? job.CompanyId.ToString(),
                _proto.TryIndex(design.Vessel, out var vessel) ? vessel.Name : design.ID, job.Stage, design.Stages.Count,
                Loc.GetString(stage.Name), job.Components, stage.Components, job.Metals, stage.Metals, job.Progress,
                job.CompanyId == company));
        }

        var governor = _colonies.Governor(world) is { } held ? _companies.CompanyIdentity(held)?.Name : null;
        var state = new WFYardConsoleState(_colonies.WorldName(world), governor, Refusal(world, company), designs, jobs);
        _ui.ServerSendUiMessage(console.Owner, WFYardConsoleUiKey.Key, state, viewer);
    }

    #endregion

    #region Tests

    /// <summary>The builds on a world's yard as stored: for tests and admin tools.</summary>
    public List<WFStoredYardJob> Jobs(string world) => JobsOn(world).Select(Stored).ToList();

    /// <summary>Places a world's saved platform now, as a round's first yard tick would.</summary>
    public EntityUid? PlaceForTest(string world)
    {
        if (!_yards.TryGetValue(world, out var yard))
            return null;

        foreach (var (id, network) in _colonies.Networks())
        {
            if (id != world)
                continue;

            PlacePlatform(yard, network);
            return yard.Platform;
        }

        return null;
    }

    /// <summary>Forgets what the yard holds in memory and rereads it, as a server restart would.</summary>
    public async Task ReloadForTest()
    {
        await Flush();
        ForgetFrames();
        _yards.Clear();
        _jobs.Clear();
        Loaded = false;
        await Reload();
    }

    #endregion
}
