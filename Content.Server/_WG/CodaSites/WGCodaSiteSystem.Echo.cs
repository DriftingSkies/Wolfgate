using System.Linq;
using Content.Server._WG.Lattice;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.Lattice;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WG.CodaSites;

/// <summary>
/// The Echo Station: Central, loaded again in the Coda and decayed once it is placed. Its machines are stripped out,
/// most bulbs are dead and the rest flicker, flesh takes root, windows crack, docks stand open, and debris lies about.
/// Echoes stand where the consoles were and talk; noticeboards carry this round's dead as old news; a dead anchor
/// twitches when real ones fall; anything taken away crumbles; and Drift builds faster aboard.
/// </summary>
public sealed partial class WGCodaSiteSystem
{
    [Dependency] private SharedPoweredLightSystem _poweredLight = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly string[] DebrisProtos = { "Cobweb1", "ShardGlass", "SalvageSpawnerScrapCommon", "WGCodaBones" };

    /// <summary>How many names the Echo may go by; one is picked each round.</summary>
    private const int EchoNames = 5;

    /// <summary>Central's own anchor beacon: the dead anchor stands where it marks.</summary>
    private const string AnchorBeacon = "DefaultStationBeaconAnchor";

    /// <summary>Copied fittings that would send players, ghosts or warps to the Echo: removed outright.</summary>
    private static readonly string[] Unwanted = { "WarpPoint", "WarpPointAdmin", "ArrivalsShuttleTimer" };
    private TimeSpan _nextCrumble;

    private void InitializeEcho()
    {
        SubscribeLocalEvent<WGEchoNewsBoardComponent, ExaminedEvent>(OnNewsExamined);
        SubscribeLocalEvent<WGLatticeBreachEvent>(OnRealAnchorLost);
    }

    private void UpdateEcho(TimeSpan now)
    {
        var echoes = EntityQueryEnumerator<WGEchoStationComponent, MapGridComponent>();
        while (echoes.MoveNext(out var grid, out var echo, out var gridComp))
        {
            if (!echo.Decayed)
            {
                echo.Decayed = true;
                Decay((grid, echo, gridComp));
            }

            // The lights that still work flicker.
            echo.Flicker.RemoveAll(l => TerminatingOrDeleted(l));
            foreach (var light in echo.Flicker)
            {
                if (_random.Prob(0.2f))
                    _light.SetEnabled(light, _random.Prob(0.6f));
            }

            if (now >= echo.NextDrift)
            {
                echo.NextDrift = now + TimeSpan.FromSeconds(5);
                foreach (var (_, mob) in Players().Where(p => Transform(p.Mob).GridUid == grid))
                    _driftMind.Add(mob, echo.Drift, WGDriftFlavour.Unmooring);
            }

            if (echo.Ambience != null && now >= echo.NextAmbience)
            {
                echo.NextAmbience = now + echo.AmbienceEvery;
                var aboard = Filter.Empty().AddPlayers(Players().Where(p => Transform(p.Mob).GridUid == grid).Select(p => p.Session));
                _audio.PlayGlobal(echo.Ambience, aboard, false);
            }
        }

        var figures = EntityQueryEnumerator<WGEchoFigureComponent, TransformComponent>();
        while (figures.MoveNext(out var figure, out var comp, out var xform))
        {
            if (now < comp.NextLine)
                continue;

            var near = Players().Any(p => Transform(p.Mob).MapUid == xform.MapUid
                && (_transform.GetWorldPosition(p.Mob) - _transform.GetWorldPosition(xform)).Length() <= comp.Range);
            if (!near)
                continue;

            comp.NextLine = now + TimeSpan.FromSeconds(_random.Next(45, 100));
            _chat.TrySendInGameICMessage(figure, EchoLine(), InGameICChatType.Speak, false);
        }

        if (now >= _nextCrumble)
        {
            _nextCrumble = now + TimeSpan.FromSeconds(2);
            Crumble(now);
        }
    }

    /// <summary>A stale line from a shift years gone; now and then, a name from this round it shouldn't know.</summary>
    private string EchoLine()
    {
        var names = DeadThisRound().Concat(Players().Select(p => Name(p.Mob))).Distinct().ToList();
        if (names.Count > 0 && _random.Prob(0.2f))
            return Loc.GetString("wg-codasite-echo-name", ("name", _random.Pick(names)));

        return Loc.GetString($"wg-codasite-echo-line-{_random.Next(1, 11)}");
    }

    /// <summary>Strips, darkens, cracks and litters the Echo, and puts its figures, boards and dead anchor in place.</summary>
    private void Decay(Entity<WGEchoStationComponent, MapGridComponent> grid)
    {
        var (uid, echo, gridComp) = grid;
        // It is not Central any more, whatever it remembers being.
        _meta.SetEntityName(uid, Loc.GetString($"wg-codasite-echo-grid-name-{_random.Next(1, EchoNames + 1)}"));
        var posts = new List<EntityCoordinates>();
        var everything = GridEntities(uid);

        var blunt = _proto.Index<DamageTypePrototype>("Blunt");
        EntityCoordinates? beacon = null;
        foreach (var ent in everything)
        {
            if (TerminatingOrDeleted(ent))
                continue;

            var xform = Transform(ent);
            var proto = MetaData(ent).EntityPrototype?.ID ?? string.Empty;
            if (proto == AnchorBeacon)
            {
                beacon = xform.Coordinates;
                QueueDel(ent);
                continue;
            }

            // Nobody spawns, wakes or warps onto the Echo.
            if (proto.Contains("SpawnPoint") || Array.IndexOf(Unwanted, proto) >= 0)
            {
                QueueDel(ent);
                continue;
            }
            // Every machine with a window goes: no shopping, selling or broadcasting from the Echo.
            if (HasComp<UserInterfaceComponent>(ent) && !HasComp<DoorComponent>(ent) && !HasComp<ItemComponent>(ent))
            {
                if (xform.Anchored)
                    posts.Add(xform.Coordinates);
                QueueDel(ent);
                continue;
            }

            if (TryComp<PoweredLightComponent>(ent, out var light))
            {
                if (_random.Prob(echo.DeadBulbs))
                    _poweredLight.TryDestroyBulb(ent, light);
                else
                    echo.Flicker.Add(ent);
                continue;
            }

            if (MetaData(ent).EntityPrototype?.ID.Contains("Window") == true && HasComp<DamageableComponent>(ent))
            {
                _damageable.TryChangeDamage(ent, new DamageSpecifier(blunt, 25), true);
                continue;
            }

            if (HasComp<DockingComponent>(ent) && HasComp<DoorComponent>(ent))
            {
                _doors.TryOpen(ent);
                if (TryComp<DoorBoltComponent>(ent, out var bolts))
                    _doors.SetBoltsDown((ent, bolts), true);
                continue;
            }

        }

        var open = _map.GetAllTiles(uid, gridComp).Select(t => t.GridIndices).Where(t => Open(uid, gridComp, t)).ToList();
        _random.Shuffle(open);
        var take = 0;
        for (var i = 0; i < echo.FleshKnots && take < open.Count; i++, take++)
            Spawn("WGCodaFleshNode", _map.GridTileToLocal(uid, gridComp, open[take]));
        var debris = (int) (open.Count * echo.Debris);
        for (var i = 0; i < debris && take < open.Count; i++, take++)
            Spawn(_random.Pick(DebrisProtos), _map.GridTileToLocal(uid, gridComp, open[take]));

        _random.Shuffle(posts);
        foreach (var post in posts.Take(echo.Echoes))
            Spawn("WGEchoFigure", post);
        foreach (var post in posts.Skip(echo.Echoes).Take(echo.Boards))
            Spawn("WGEchoNewsBoard", post);

        // The dead anchor stands in Central's anchor room, on the open tile nearest its beacon.
        var anchorAt = posts.Count > 0 ? posts[0] : _map.GridTileToLocal(uid, gridComp, open.FirstOrDefault());
        if (beacon is { } mark)
        {
            var near = _map.TileIndicesFor(uid, gridComp, mark);
            var tile = _map.GetAllTiles(uid, gridComp).Select(t => t.GridIndices).Where(t => Open(uid, gridComp, t))
                .OrderBy(t => (t - near).LengthSquared).FirstOrDefault(near);
            anchorAt = _map.GridTileToLocal(uid, gridComp, tile);
        }

        Spawn("WGEchoAnchor", anchorAt);

        // Everything loose aboard is Echo matter, the figures' clothes too: it crumbles once taken away.
        var items = EntityQueryEnumerator<ItemComponent, TransformComponent>();
        while (items.MoveNext(out var item, out _, out var itemXform))
        {
            if (itemXform.GridUid == uid)
                EnsureComp<WGEchoItemComponent>(item).Home = uid;
        }
        Log.Info($"The Echo decayed: {posts.Count} machines stripped, {echo.Flicker.Count} lights left flickering.");
    }

    private void OnNewsExamined(Entity<WGEchoNewsBoardComponent> ent, ref ExaminedEvent args)
    {
        var dead = DeadThisRound();
        if (dead.Count == 0)
        {
            args.PushMarkup(Loc.GetString("wg-codasite-echo-news-blank"));
            return;
        }

        args.PushMarkup(Loc.GetString("wg-codasite-echo-news-heading"));
        foreach (var name in dead.TakeLast(5))
            args.PushMarkup(Loc.GetString("wg-codasite-echo-news-line", ("name", name), ("years", 3 + Math.Abs(name.GetHashCode()) % 40)));
    }

    private void OnRealAnchorLost(ref WGLatticeBreachEvent args)
    {
        var anchors = EntityQueryEnumerator<WGEchoAnchorComponent, TransformComponent>();
        while (anchors.MoveNext(out var uid, out var anchor, out var xform))
        {
            _popup.PopupEntity(Loc.GetString("wg-codasite-echo-anchor-twitch"), uid, PopupType.LargeCaution);
            foreach (var (session, mob) in Players().Where(p => Transform(p.Mob).GridUid == xform.GridUid))
                _driftMind.Add(mob, anchor.Drift, WGDriftFlavour.Unmooring);
        }
    }

    /// <summary>Anything taken off the Echo crumbles to dust once it has been away long enough.</summary>
    private void Crumble(TimeSpan now)
    {
        var items = EntityQueryEnumerator<WGEchoItemComponent, TransformComponent>();
        while (items.MoveNext(out var uid, out var item, out var xform))
        {
            if (xform.GridUid == item.Home)
            {
                item.LeftAt = null;
                continue;
            }

            item.LeftAt ??= now;
            if (now - item.LeftAt < item.CrumbleAfter)
                continue;

            var coords = _transform.GetMoverCoordinates(uid);
            // Whatever was put inside it isn't Echo matter: it falls out as the thing crumbles.
            if (TryComp<Robust.Shared.Containers.ContainerManagerComponent>(uid, out var manager))
            {
                foreach (var container in manager.Containers.Values.ToList())
                    _containers.EmptyContainer(container, true, coords);
            }

            _popup.PopupCoordinates(Loc.GetString("wg-codasite-echo-crumbles", ("item", uid)), coords);
            Spawn("Ash", coords);
            QueueDel(uid);
        }
    }
}
