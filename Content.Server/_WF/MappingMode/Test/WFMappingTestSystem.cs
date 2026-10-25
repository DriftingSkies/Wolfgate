using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingTools;
using Content.Server.Administration.Logs;
using Content.Server.Communications;
using Content.Server.Preferences.Managers;
using Content.Server.Radio;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Test;
using Content.Shared.Database;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Preferences;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingMode.Test;

/// <summary>
/// Live testing of a mapping session: an initialised copy of its levels where power, atmos and doors run, with the
/// ship made flyable and a range to fly to, which members walk as their own characters. Nothing in a test is ever
/// saved or edited; Back to mapping, death, disconnect, the session closing, the owner, or the time limit end it.
/// </summary>
public sealed partial class WFMappingTestSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MappingZLevelsSystem _zLevels = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private StationSpawningSystem _spawning = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private WFMappingLibrarySystem _library = default!;
    [Dependency] private WFMappingModeSystem _mapping = default!;

    /// <summary>Tiles between the ship's edge and the range's.</summary>
    public const float RangeDistance = 60f;

    /// <summary>Seconds a dead tester lies there before they are sent back.</summary>
    public static readonly TimeSpan DeathReturn = TimeSpan.FromSeconds(3);

    /// <summary>Share of <c>max_entities</c> a session may use and still start a test, which copies it.</summary>
    public const float StartShare = 0.6f;

    /// <summary>The session each tester is testing.</summary>
    private readonly Dictionary<NetUserId, int> _testing = new();

    /// <summary>One start per three seconds per player; stopping is never throttled, so Back to mapping always works.</summary>
    private readonly WFMappingThrottle<NetUserId> _throttle = new(1 / 3.0, 1);

    /// <summary>When the next test may be built on this server, after <c>test_global_cooldown</c> since the last.</summary>
    private TimeSpan _globalCooldownUntil;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFMappingTestStartRequest>(OnStartRequest);
        SubscribeNetworkEvent<WFMappingTestStopRequest>(OnStopRequest);
        SubscribeLocalEvent<WFMappingTesterComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<WFMappingTesterComponent, PlayerDetachedEvent>(OnTesterDetached);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnFTLAttempt);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceiveAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_testing.Count == 0)
            return;

        var now = _timing.CurTime;
        foreach (var session in _mapping.Sessions.ToList())
        {
            if (session.Test is not { } test)
                continue;

            if (now >= test.Ends)
            {
                EndTest(session, "wf-mapping-test-time-up");
                continue;
            }

            foreach (var (user, tester) in test.Testers.ToList())
            {
                if (TerminatingOrDeleted(tester.Mob) || tester.ReturnAt is { } at && now >= at)
                    EndTest(user, "wf-mapping-test-returned");
            }
        }
    }

    /// <summary>Whether a player is in a live test.</summary>
    public bool IsTesting(NetUserId user)
    {
        return _testing.ContainsKey(user);
    }

    /// <summary>A session's running test as its members see it, or null.</summary>
    public WFMappingTestInfo? Info(WFMappingSession session)
    {
        if (session.Test is not { } test)
            return null;

        var names = test.Testers.Keys
            .Select(user => _players.TryGetPlayerData(user, out var data) ? data.UserName : user.ToString())
            .ToList();
        return new WFMappingTestInfo(test.Started, test.Ends, names);
    }

    private void OnStartRequest(WFMappingTestStartRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_throttle.TryTake(player.UserId, _timing.CurTime))
            return;

        TryStart(player);
    }

    private void OnStopRequest(WFMappingTestStopRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled)
            return;

        if (ev.All)
        {
            if (!_mapping.TryGetPresent(player, out var session) || session.Test == null)
                return;

            if (session.Owner != player.UserId)
            {
                _mapping.Notify(player, false, "wf-mapping-test-end-not-owner");
                return;
            }

            _adminLog.Add(LogType.Action, LogImpact.Low, $"{player:player} ended the live test of mapping session {session.Code}");
            EndTest(session, "wf-mapping-test-ended-by-owner");
            return;
        }

        EndTest(player.UserId, "wf-mapping-test-returned");
    }

    /// <summary>
    /// Puts a present member into their session's live test, starting it if none runs. Tells them why when it can't.
    /// </summary>
    public bool TryStart(ICommonSession player)
    {
        if (!_mapping.TryGetPresent(player, out var session))
        {
            _mapping.Notify(player, false, "wf-mapping-mode-no-session");
            return false;
        }

        if (_testing.ContainsKey(player.UserId))
        {
            _mapping.Notify(player, false, "wf-mapping-test-already");
            return false;
        }

        if (session.Test == null && !TryBuild(player, session))
            return false;

        var test = session.Test!;
        if (TryJoin(player, session, test))
            return true;

        // A test nobody got into would run unseen until the session closes.
        if (session.Test == test && test.Testers.Count == 0)
            CloseTest(session, test);

        return false;
    }

    /// <summary>
    /// Snapshots the session and opens its test stack; refused past the session's and the server's cooldowns, the
    /// running tests cap and the entity share.
    /// </summary>
    private bool TryBuild(ICommonSession player, WFMappingSession session)
    {
        var now = _timing.CurTime;
        if (now < session.TestCooldownUntil)
        {
            _mapping.Notify(player, false, "wf-mapping-test-cooldown",
                ("seconds", (int) Math.Ceiling((session.TestCooldownUntil - now).TotalSeconds)));
            return false;
        }

        if (now < _globalCooldownUntil)
        {
            _mapping.Notify(player, false, "wf-mapping-test-global-cooldown",
                ("seconds", (int) Math.Ceiling((_globalCooldownUntil - now).TotalSeconds)));
            return false;
        }

        var maxRunning = _cfg.GetCVar(WFMappingModeCVars.TestMaxRunning);
        var running = _mapping.Sessions.Count(s => s.Test != null);
        if (running >= maxRunning)
        {
            _mapping.Notify(player, false, "wf-mapping-test-max-running", ("max", maxRunning));
            return false;
        }

        var maxEntities = _cfg.GetCVar(WFMappingModeCVars.MaxEntities);
        var count = _mapping.EntityCount(session);
        if (count > maxEntities * StartShare)
        {
            _mapping.Notify(player, false, "wf-mapping-test-too-big",
                ("count", count), ("max", (int) (maxEntities * StartShare)), ("limit", maxEntities));
            return false;
        }

        if (!TryOpenStack(session, out var maps, out var ground))
        {
            _mapping.Notify(player, false, "wf-mapping-test-failed");
            return false;
        }

        var groundMap = maps[ground];
        EntityUid? ship = null;
        var spawn = new EntityCoordinates(groundMap, Vector2.Zero);
        try
        {
            ship = LargestGrid(groundMap);
            if (ship is { } shipUid)
            {
                PrepareShip(shipUid, session);
                LoadRange(groundMap, shipUid);
                spawn = FindSpawn(shipUid);
            }

            foreach (var map in maps)
            {
                _map.InitializeMap(map);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Building the live test of mapping session {session.Code} failed: {e}");
            _mapping.DeleteMaps(maps);
            _mapping.Notify(player, false, "wf-mapping-test-failed");
            return false;
        }

        var minutes = Math.Max(1, _cfg.GetCVar(WFMappingModeCVars.TestMaxMinutes));
        session.Test = new WFMappingTest(maps, groundMap, ship, spawn, now, now + TimeSpan.FromMinutes(minutes));
        _globalCooldownUntil = now + TimeSpan.FromSeconds(Math.Max(0f, _cfg.GetCVar(WFMappingModeCVars.TestGlobalCooldown)));
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} started a live test of mapping session {session.Code} on {maps.Count} levels");
        return true;
    }

    /// <summary>
    /// Saves the session's levels to a scratch folder with the library's save code and loads them back as a stack of
    /// their own, tagged as the session's test levels; the scratch files go at once.
    /// </summary>
    private bool TryOpenStack(WFMappingSession session, [NotNullWhen(true)] out List<EntityUid>? maps, out int ground)
    {
        maps = null;
        ground = 0;
        var folder = new ResPath($"/Mapping/Test/{session.Id}");
        try
        {
            if (TerminatingOrDeleted(session.RootMap))
                return false;

            var levels = _zLevels.GetLevels(session.RootMap, out var liveGround);
            if (levels.Any(level => !_mapping.IsSessionMap(level, session)))
            {
                Log.Error($"Mapping session {session.Code} has a level outside the session; not testing it");
                return false;
            }

            var files = new SortedDictionary<int, ResPath>();
            for (var i = 0; i < levels.Count; i++)
            {
                files[i - liveGround] = folder / $"z{i - liveGround}.yml";
            }

            if (!_zLevels.TrySaveLevels(levels, liveGround, depth => files[depth]))
                return false;

            if (!_zLevels.TryLoadLevels(files, out maps, out ground))
                return false;

            foreach (var map in maps)
            {
                _mapping.RegisterTestMap(map, session.Id);
                _meta.SetEntityName(map, Loc.GetString("wf-mapping-test-map-name", ("name", session.Name)));
            }

            // The same fix-up a saved map gets, on the copy: the live levels stay as they are.
            _library.FixAtmos(maps, session);
            ScrubCopy(maps);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Snapshotting mapping session {session.Code} for a live test failed: {e}");
            if (maps != null)
                _mapping.DeleteMaps(maps);

            maps = null;
            return false;
        }
        finally
        {
            try
            {
                if (_res.UserData.Exists(folder))
                    _res.UserData.Delete(folder);
            }
            catch (Exception e)
            {
                Log.Warning($"Couldn't delete the live test scratch folder {folder}: {e}");
            }
        }
    }

    /// <summary>
    /// Turns server-wide announcements on the copy into station ones, which reach nobody without a station: a
    /// running comms console would otherwise announce to the whole server.
    /// </summary>
    private void ScrubCopy(List<EntityUid> maps)
    {
        var query = AllEntityQuery<CommunicationsConsoleComponent, TransformComponent>();
        while (query.MoveNext(out _, out var comms, out var xform))
        {
            if (xform.MapUid is { } map && maps.Contains(map))
                comms.Global = false;
        }
    }

    /// <summary>
    /// What a bought ship has that a mapped grid may lack: a shuttle so consoles fly it, an IFF so radars show it, and
    /// the session's name. Deeds, console locks, ship access and the FTL lock stay off: there is no deed holder.
    /// </summary>
    private void PrepareShip(EntityUid ship, WFMappingSession session)
    {
        EnsureComp<ShuttleComponent>(ship);
        EnsureComp<IFFComponent>(ship);
        _meta.SetEntityName(ship, session.Name);
    }

    /// <summary>Loads the range grid named by <c>test_range</c> beside the ship, <see cref="RangeDistance"/> off its edge.</summary>
    private void LoadRange(EntityUid groundMap, EntityUid ship)
    {
        var path = _cfg.GetCVar(WFMappingModeCVars.TestRange);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var file = new ResPath(path);
        if (!_res.ContentFileExists(file))
        {
            Log.Warning($"The live test range {path} doesn't exist; testing in empty space");
            return;
        }

        var shipBox = _transform.GetWorldMatrix(ship).TransformBox(Comp<MapGridComponent>(ship).LocalAABB);
        var mapId = Comp<MapComponent>(groundMap).MapId;
        if (!_loader.TryLoadGrid(mapId, file, out var range, offset: new Vector2(shipBox.Right + RangeDistance + 500f, 0f)))
        {
            Log.Warning($"The live test range {path} didn't load as a grid; testing in empty space");
            return;
        }

        var local = range.Value.Comp.LocalAABB;
        _transform.SetWorldPosition(range.Value.Owner,
            new Vector2(shipBox.Right + RangeDistance - local.Left, shipBox.Center.Y - local.Center.Y));
        _meta.SetEntityName(range.Value.Owner, Loc.GetString("wf-mapping-test-range-name"));
    }

    /// <summary>
    /// Where testers land on the ship: a spawn point, else the tile in front of a shuttle console, else the free floor
    /// tile nearest the grid's centre.
    /// </summary>
    private EntityCoordinates FindSpawn(EntityUid ship)
    {
        var grid = Comp<MapGridComponent>(ship);
        EntityCoordinates? console = null;
        var children = Transform(ship).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<SpawnPointComponent>(child))
                return new EntityCoordinates(ship, Transform(child).LocalPosition);

            if (console != null || !HasComp<ShuttleConsoleComponent>(child))
                continue;

            var xform = Transform(child);
            var front = xform.LocalPosition + xform.LocalRotation.ToWorldVec();
            var cell = new Vector2i((int) MathF.Floor(front.X), (int) MathF.Floor(front.Y));
            if (!_map.TryGetTileRef(ship, grid, cell, out var tile) || tile.Tile.IsEmpty)
                continue;

            console = new EntityCoordinates(ship, cell + new Vector2(0.5f));
        }

        if (console is { } spot)
            return spot;

        var centre = grid.LocalAABB.Center;
        Vector2? best = null;
        var bestDistance = float.MaxValue;
        var tiles = _map.GetAllTiles(ship, grid);
        while (tiles.MoveNext(out var next))
        {
            if (next is not { } tile || tile.Tile.IsEmpty ||
                _map.GetAnchoredEntities(ship, grid, tile.GridIndices).MoveNext(out _))
            {
                continue;
            }

            var position = tile.GridIndices + new Vector2(0.5f);
            var distance = (position - centre).LengthSquared();
            if (distance >= bestDistance)
                continue;

            best = position;
            bestDistance = distance;
        }

        return new EntityCoordinates(ship, best ?? centre);
    }

    private EntityUid? LargestGrid(EntityUid map)
    {
        EntityUid? largest = null;
        var largestArea = -1f;
        var children = Transform(map).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp(child, out MapGridComponent? grid))
                continue;

            var area = grid.LocalAABB.Width * grid.LocalAABB.Height;
            if (area <= largestArea)
                continue;

            largest = child;
            largestArea = area;
        }

        return largest;
    }

    /// <summary>
    /// Spawns a present member as their character at the test's spot, moves their mind onto it and parks their observer
    /// in nullspace until they come back.
    /// </summary>
    private bool TryJoin(ICommonSession player, WFMappingSession session, WFMappingTest test)
    {
        var user = player.UserId;
        if (!session.Present.TryGetValue(user, out var observer) || player.AttachedEntity != observer ||
            !_mind.TryGetMind(player, out var mindId, out _))
        {
            _mapping.Notify(player, false, "wf-mapping-test-failed");
            return false;
        }

        if (TerminatingOrDeleted(test.Ground) || !test.Spawn.IsValid(EntityManager))
        {
            // The ending test tells its testers; the player joining it isn't one yet.
            EndTest(session, "wf-mapping-test-failed");
            _mapping.Notify(player, false, "wf-mapping-test-failed");
            return false;
        }

        EntityUid mob;
        try
        {
            var profile = _prefs.GetPreferencesOrNull(user)?.SelectedCharacter as HumanoidCharacterProfile
                          ?? HumanoidCharacterProfile.DefaultWithSpecies();
            mob = _spawning.SpawnPlayerMob(test.Spawn, job: null, TestProfile(profile), station: null, session: player);
        }
        catch (Exception e)
        {
            Log.Error($"Spawning {player}'s test character for mapping session {session.Code} failed: {e}");
            _mapping.Notify(player, false, "wf-mapping-test-failed");
            return false;
        }

        var tester = EnsureComp<WFMappingTesterComponent>(mob);
        tester.MapName = session.Name;
        tester.Ends = test.Ends;
        Dirty(mob, tester);

        var observerAt = _transform.GetMapCoordinates(observer);
        test.Testers[user] = new WFMappingTester(mob, observer, observerAt);
        _testing[user] = session.Id;

        // Registered first, so the observer's detach doesn't count the member as absent.
        _mind.TransferTo(mindId, mob, ghostCheckOverride: true);
        _transform.DetachEntity(observer);

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} joined the live test of mapping session {session.Code} as {ToPrettyString(mob)}");
        _mapping.Notify(player, true, "wf-mapping-test-started", ("name", session.Name));
        _mapping.SendState(session);
        return true;
    }

    /// <summary>
    /// The character a tester walks as: their profile without its persistent items and components. Spawning hands
    /// those to the body and saves the profile without the ones used up, which a test character deleted afterwards
    /// would take from the real character for good.
    /// </summary>
    public static HumanoidCharacterProfile TestProfile(HumanoidCharacterProfile profile)
    {
        return profile.WithPersistentData(profile.Flags, Array.Empty<PersistentProfileComponent>(),
            Array.Empty<PersistentProfileItem>());
    }

    /// <summary>
    /// Takes one tester back to their observer where they left it and deletes their character; the test's maps go
    /// once nobody is in them.
    /// </summary>
    public void EndTest(NetUserId user, string? reason = null)
    {
        if (!_testing.Remove(user, out var id) || !_mapping.TryGetSession(id, out var session) ||
            session.Test is not { } test || !test.Testers.Remove(user, out var tester))
        {
            return;
        }

        _players.TryGetSessionById(user, out var player);
        var observer = tester.Observer;
        var observerAlive = !TerminatingOrDeleted(observer);

        // A session losing its root map is closing; its observers go with it, so there is nowhere to put this one back.
        if (observerAlive && !TerminatingOrDeleted(session.RootMap))
        {
            var at = _map.TryGetMap(tester.ObserverAt.MapId, out var atMap) && _mapping.IsSessionMap(atMap.Value, session)
                ? tester.ObserverAt
                : new MapCoordinates(Vector2.Zero, Comp<MapComponent>(session.RootMap).MapId);
            _transform.SetMapCoordinates(observer, at);
        }

        if (!TerminatingOrDeleted(tester.Mob))
        {
            if (observerAlive && player is { Status: SessionStatus.InGame } &&
                _mind.TryGetMind(user, out var mind))
            {
                _mind.TransferTo(mind.Value.Owner, observer, ghostCheckOverride: true);
            }

            QueueDel(tester.Mob);
        }

        // Nobody came back for the observer: they left while testing, so they are absent now.
        if (observerAlive && (player == null || player.AttachedEntity != observer))
            _mapping.ObserverGone(user, observer);

        if (player != null && reason != null)
            _mapping.Notify(player, true, reason, ("name", session.Name));

        if (test.Testers.Count == 0)
            CloseTest(session, test);
        else
            _mapping.SendState(session);
    }

    /// <summary>Ends the session's test for everyone in it and deletes its maps.</summary>
    public void EndTest(WFMappingSession session, string? reason = null)
    {
        if (session.Test is not { } test)
            return;

        foreach (var user in test.Testers.Keys.ToList())
        {
            EndTest(user, reason);
        }

        if (session.Test == test)
            CloseTest(session, test);
    }

    private void CloseTest(WFMappingSession session, WFMappingTest test)
    {
        session.Test = null;
        session.TestCooldownUntil = _timing.CurTime +
                                    TimeSpan.FromSeconds(Math.Max(0f, _cfg.GetCVar(WFMappingModeCVars.TestCooldown)));
        _mapping.DeleteMaps(test.Maps.Concat(_mapping.TestMaps(session.Id)).Distinct());
        session.CountedAt = TimeSpan.Zero;
        _adminLog.Add(LogType.Action, LogImpact.Low, $"The live test of mapping session {session.Code} ended");
        if (_mapping.Sessions.Contains(session))
            _mapping.SendState(session);
    }

    /// <summary>Forgets every test, for a round restart whose flush deletes the maps.</summary>
    public void Clear()
    {
        _testing.Clear();
        _throttle.Clear();
        _globalCooldownUntil = TimeSpan.Zero;
    }

    private void OnMobState(Entity<WFMappingTesterComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || !TryGetTester(ent.Owner, out var tester))
            return;

        tester.ReturnAt ??= _timing.CurTime + DeathReturn;
    }

    /// <summary>The tester's player left their character: a disconnect, or an admin moving them. The test ends for them.</summary>
    private void OnTesterDetached(Entity<WFMappingTesterComponent> ent, ref PlayerDetachedEvent args)
    {
        var user = args.Player.UserId;
        if (_testing.TryGetValue(user, out var id) && _mapping.TryGetSession(id, out var session) &&
            session.Test is { } test && test.Testers.TryGetValue(user, out var tester) && tester.Mob == ent.Owner)
        {
            EndTest(user);
        }
    }

    /// <summary>
    /// A ship on a test level stays there: FTL would carry it into the shared FTL space, past the end of the test, or
    /// onto any other map that takes FTL arrivals, another session's included.
    /// </summary>
    private void OnFTLAttempt(ref ConsoleFTLAttemptEvent args)
    {
        if (args.Cancelled || Transform(args.Uid).MapUid is not { } map || !_mapping.IsTestMap(map))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("wf-mapping-test-no-ftl");
    }

    /// <summary>Radio, which ignores maps, stays inside a test: nothing goes in or out of a test's levels.</summary>
    private void OnRadioReceiveAttempt(ref RadioReceiveAttemptEvent args)
    {
        if (args.Cancelled || _testing.Count == 0)
            return;

        var source = Transform(args.RadioSource).MapUid;
        var receiver = Transform(args.RadioReceiver).MapUid;
        if (source == receiver)
            return;

        var from = source is { } sourceMap ? _mapping.TestSessionOf(sourceMap) : null;
        var to = receiver is { } receiverMap ? _mapping.TestSessionOf(receiverMap) : null;
        if (from != to)
            args.Cancelled = true;
    }

    private bool TryGetTester(EntityUid mob, [NotNullWhen(true)] out WFMappingTester? tester)
    {
        tester = null;
        foreach (var session in _mapping.Sessions)
        {
            if (session.Test is not { } test)
                continue;

            foreach (var candidate in test.Testers.Values)
            {
                if (candidate.Mob != mob)
                    continue;

                tester = candidate;
                return true;
            }
        }

        return false;
    }
}
