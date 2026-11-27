using System.Linq;
using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._WG.Lattice;
using Content.Server.Administration.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Shared._WG.CodaSites;
using Content.Shared._WG.Lattice;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Server.Power.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.CodaSites;

/// <summary>
/// The Coda's sites: places each once per Lattice map at round start and runs them. The mercy
/// sites (the Lighthouse, the Last Anchor, the Hospice) are here; the fear sites are in WGCodaSiteSystem.Fear.cs.
/// What keeps a site standing (self-power, air, a healing hull) is WGCodaGridSystem's.
/// </summary>
public sealed partial class WGCodaSiteSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPowerReceiverSystem _receiver = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private WGLatticeSystem _lattice = default!;
    [Dependency] private WGDriftMindSystem _driftMind = default!;
    [Dependency] private WGChoirSystem _choir = default!;

    private readonly HashSet<(EntityUid Map, string Site)> _placed = new();
    private readonly Dictionary<NetUserId, int> _hospiceVisits = new();

    /// <summary>Player characters who have died this round, in the order they first died.</summary>
    private readonly List<string> _dead = new();
    private TimeSpan _nextPlacement;
    private TimeSpan _nextTick;

    /// <summary>The silent grid music every site plays.</summary>
    private const string SilenceMusic = "WGCodaSilence";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        InitializeFear();
        InitializeEcho();
        InitializeRefine();
    }

    private void Reset()
    {
        _placed.Clear();
        _placeTries.Clear();
        _hospiceVisits.Clear();
        _dead.Clear();
        ResetFear();
        ResetRefine();
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || !HasComp<ActorComponent>(args.Target))
            return;

        var name = Name(args.Target);
        if (!_dead.Contains(name))
            _dead.Add(name);
    }

    /// <summary>The player characters who have died this round, in order. The keeper and the grave rocks read it.</summary>
    public IReadOnlyList<string> DeadThisRound() => _dead;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now >= _nextPlacement)
        {
            _nextPlacement = now + TimeSpan.FromSeconds(10);
            PlaceWhereMissing();
        }

        if (now < _nextTick)
            return;

        _nextTick = now + TimeSpan.FromSeconds(1);
        UpdateLighthouses(now);
        UpdateLastAnchors(now);
        UpdateHospice(now);
        UpdateFear(now);
        UpdateEcho(now);
        UpdateRefine(now);
    }

    /// <summary>How many times a site is tried before it is given up on for the round.</summary>
    private const int MaxPlaceTries = 3;

    private readonly Dictionary<(EntityUid Map, string Site), int> _placeTries = new();

    private void PlaceWhereMissing()
    {
        var fields = EntityQueryEnumerator<WGLatticeFieldComponent>();
        while (fields.MoveNext(out var map, out var field))
        {
            if (field.Anchors.Count == 0)
                continue;

            foreach (var site in _proto.EnumeratePrototypes<WGCodaSitePrototype>())
            {
                if (_placed.Contains((map, site.ID)))
                    continue;

                if (SiteOn(map, site.ID) != null || TryPlace(map, site, out _))
                {
                    _placed.Add((map, site.ID));
                    continue;
                }

                // A site with nowhere to go is tried a few times, then left for the round.
                var tries = _placeTries.GetValueOrDefault((map, site.ID)) + 1;
                _placeTries[(map, site.ID)] = tries;
                if (tries >= MaxPlaceTries)
                {
                    _placed.Add((map, site.ID));
                    Log.Warning($"Gave up placing Coda site {site.ID} on {ToPrettyString(map)} after {tries} tries.");
                }
            }
        }
    }

    /// <summary>A site's grid on a map, if it has been placed.</summary>
    public EntityUid? SiteOn(EntityUid map, string site)
    {
        var query = EntityQueryEnumerator<WGCodaSiteGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Site == site && xform.MapUid == map)
                return uid;
        }

        return null;
    }

    /// <summary>Loads a site on a random bearing in its band, clear of every other grid.</summary>
    public bool TryPlace(EntityUid map, WGCodaSitePrototype site, out EntityUid grid)
    {
        grid = default;
        var mapId = Comp<MapComponent>(map).MapId;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var position = _random.NextAngle().ToVec() * _random.NextFloat(site.MinDistance, site.MaxDistance);
            if (_lattice.Locate(map, position).Band < site.Band || !Clear(map, position, site.Clearance))
                continue;

            if (site.WholeMap)
            {
                if (!TryLoadWholeMap(map, site.Map, position, out grid))
                    return false;
            }
            else
            {
                if (!_mapLoader.TryLoadGrid(mapId, site.Map, out var loaded, offset: position))
                    return false;

                grid = loaded.Value.Owner;
            }

            EnsureComp<WGCodaSiteGridComponent>(grid).Site = site.ID;
            // Sites stand far from anyone for most of the round, with no IFF and no APCs: grid cleanup would take them.
            EnsureComp<CleanupImmuneComponent>(grid);
            // Silent: the site's own music is nothing, so ambient music doesn't fall back to the biome's.
            var music = EnsureComp<Content.Shared._Crescent.Vessel.VesselMusicComponent>(grid);
            music.AmbientMusicPrototype = SilenceMusic;
            Dirty(grid, music);
            if (site.Kind == "Echo")
                EnsureComp<WGEchoStationComponent>(grid);
            else if (site.Kind == "Hospice")
                PrepareHospice(grid);
            Log.Info($"Coda site {site.ID} placed at {position} on {ToPrettyString(map)}.");
            return true;
        }

        Log.Warning($"Found no clear place for Coda site {site.ID} on {ToPrettyString(map)}.");
        return false;
    }

    /// <summary>
    /// Loads a whole map file on a scratch map, initialised so everything in it is set up, then moves its grids onto the
    /// target map round a point, its largest grid on the point.
    /// </summary>
    public bool TryLoadWholeMap(EntityUid map, Robust.Shared.Utility.ResPath path, Vector2 position, out EntityUid grid)
    {
        grid = default;
        var options = new Robust.Shared.EntitySerialization.DeserializationOptions { InitializeMaps = true };
        if (!_mapLoader.TryLoadMap(path, out var scratch, out var grids, options) || grids.Count == 0)
            return false;

        var largest = grids.MaxBy(g => g.Comp.LocalAABB.Size.LengthSquared());
        var origin = _transform.GetWorldPosition(largest);
        foreach (var loaded in grids)
        {
            var offset = _transform.GetWorldPosition(loaded) - origin;
            _transform.SetCoordinates(loaded, new EntityCoordinates(map, position + offset));
            EnsureComp<CleanupImmuneComponent>(loaded);
        }

        Del(scratch.Value.Owner);
        grid = largest.Owner;
        return true;
    }

    private bool Clear(EntityUid map, Vector2 position, float clearance)
    {
        var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid == map && (_transform.GetWorldPosition(xform) - position).Length() < clearance)
                return false;
        }

        return true;
    }

    private IEnumerable<(ICommonSession Session, EntityUid Mob)> Players()
    {
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } mob)
                yield return (session, mob);
        }
    }

    // The Lighthouse.

    private void UpdateLighthouses(TimeSpan now)
    {
        var houses = EntityQueryEnumerator<WGLighthouseComponent, TransformComponent>();
        while (houses.MoveNext(out var grid, out var house, out var xform))
        {
            if (now >= house.NextBeacon)
            {
                house.NextBeacon = now + house.BeaconEvery;
                var bearing = _transform.GetWorldPosition(xform);
                var degrees = (int) MathF.Round((MathF.Atan2(bearing.X, bearing.Y) * 180f / MathF.PI + 360f) % 360f);
                _radio.SendRadioMessage(grid, Loc.GetString("wg-codasite-lighthouse-beacon", ("bearing", degrees),
                    ("km", (int) (bearing.Length() / 1000f))), "Common", grid);
            }

            // A sleeper who wakes aboard finds the keeper over them.
            foreach (var (session, mob) in Players())
            {
                if (Transform(mob).GridUid != grid)
                    continue;

                if (HasComp<SleepingComponent>(mob))
                    house.Sleepers.Add(mob);
                else if (house.Sleepers.Remove(mob))
                    KeeperGreets(grid, mob, session);
            }
        }

        var keepers = EntityQueryEnumerator<WGLighthouseKeeperComponent, TransformComponent>();
        while (keepers.MoveNext(out var keeper, out var comp, out var xform))
        {
            if (now < comp.NextLine)
                continue;

            var near = Players().Any(p => Transform(p.Mob).MapUid == xform.MapUid
                && (_transform.GetWorldPosition(p.Mob) - _transform.GetWorldPosition(xform)).Length() <= comp.Range);
            if (!near)
                continue;

            comp.NextLine = now + TimeSpan.FromSeconds(_random.Next(50, 90));
            _chat.TrySendInGameICMessage(keeper, KeeperLine(), InGameICChatType.Speak, false);
        }
    }

    /// <summary>Something the keeper says: mostly ordinary, sometimes not quite right.</summary>
    private string KeeperLine()
    {
        var dead = DeadThisRound();
        if (dead.Count > 0 && _random.Prob(0.3f))
            return Loc.GetString("wg-codasite-keeper-asks", ("name", _random.Pick(dead)));

        return Loc.GetString($"wg-codasite-keeper-line-{_random.Next(1, 9)}");
    }

    private void KeeperGreets(EntityUid grid, EntityUid sleeper, ICommonSession session)
    {
        var keepers = EntityQueryEnumerator<WGLighthouseKeeperComponent, TransformComponent>();
        while (keepers.MoveNext(out var keeper, out _, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            _transform.SetCoordinates(keeper, Transform(sleeper).Coordinates.Offset(new Vector2(0f, 1f)));
            _chat.TrySendInGameICMessage(keeper, Loc.GetString("wg-codasite-keeper-woke"), InGameICChatType.Whisper, false);
            _popup.PopupEntity(Loc.GetString("wg-codasite-keeper-standing"), sleeper, session, PopupType.MediumCaution);
            return;
        }
    }

    // The Last Anchor.

    private void UpdateLastAnchors(TimeSpan now)
    {
        var anchors = EntityQueryEnumerator<WGLastAnchorComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (anchors.MoveNext(out var uid, out var anchor, out var receiver, out var xform))
        {
            // First seen: it begins its cycle holding.
            if (anchor.NextSwitch == TimeSpan.Zero)
            {
                anchor.NextSwitch = now + anchor.HoldTime;
                continue;
            }

            if (now >= anchor.NextSwitch)
            {
                anchor.Holding = !anchor.Holding;
                anchor.NextSwitch = now + (anchor.Holding ? anchor.FedHold ?? anchor.HoldTime : anchor.FailTime);
                anchor.FedHold = null;
                // It holds while it has power, and on its own grid nothing else powers it.
                _receiver.SetNeedsPower(uid, !anchor.Holding, receiver);
                SetSiteLights(xform.GridUid, anchor.Holding);
            }

            if (anchor.Holding || now < anchor.NextDrift)
                continue;

            anchor.NextDrift = now + TimeSpan.FromSeconds(5);
            var centre = _transform.GetWorldPosition(xform);
            foreach (var (session, mob) in Players())
            {
                if (Transform(mob).MapUid != xform.MapUid || (_transform.GetWorldPosition(mob) - centre).Length() > anchor.Range)
                    continue;

                _driftMind.Add(mob, anchor.Drift, WGDriftFlavour.Unmooring);
            }
        }
    }

    /// <summary>Lights on a site's grid go out while its anchor fails.</summary>
    private void SetSiteLights(EntityUid? grid, bool on)
    {
        if (grid == null)
            return;

        var lights = EntityQueryEnumerator<Robust.Server.GameObjects.PointLightComponent, TransformComponent>();
        while (lights.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && !HasComp<WGLastAnchorComponent>(uid))
                _light.SetEnabled(uid, on);
        }
    }

    // The Hospice.

    private void UpdateHospice(TimeSpan now)
    {
        var cots = EntityQueryEnumerator<WGHospiceCotComponent, StrapComponent>();
        while (cots.MoveNext(out var uid, out var cot, out var strap))
        {
            var patient = strap.BuckledEntities.FirstOrDefault();
            if (patient == default)
            {
                cot.Patient = null;
                continue;
            }

            if (cot.Patient != patient)
            {
                cot.Patient = patient;
                cot.Since = now;
                cot.Treated = false;
                continue;
            }

            if (cot.Treated || now - cot.Since < cot.HealTime)
                continue;

            cot.Treated = true;
            _rejuvenate.PerformRejuvenate(patient);
            _driftMind.Add(patient, cot.Drift, WGDriftFlavour.Unmooring);
            _popup.PopupEntity(Loc.GetString("wg-codasite-hospice-healed"), patient, patient, PopupType.Medium);
            MarkHealed(patient, uid);

            if (!TryComp<ActorComponent>(patient, out var actor))
                continue;

            var visits = _hospiceVisits.GetValueOrDefault(actor.PlayerSession.UserId) + 1;
            _hospiceVisits[actor.PlayerSession.UserId] = visits;
            if (visits == cot.SongOnVisit)
                _choir.Offer(actor.PlayerSession, patient);
        }
    }
}
