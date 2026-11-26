using System.Linq;
using System.Numerics;
using Content.Server._Mono.Radar;
using Content.Server._WG.Lattice;
using Content.Server.Chat.Systems;
using Content.Shared.Power.Components;
using Robust.Shared.Random;
using Content.Shared._WG.Lattice;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.CodaSites;

/// <summary>
/// The sites' deeper behaviour. The Lighthouse sweeps a real beam and blinks on far radar, and its keeper gives each
/// visitor a flare and a moment's calm. The Last Anchor can be fed power cells or exotic matter to hold longer. The
/// Hospice leaves a mark: its attendants speak, and whispers follow the healed for a while. The Orchard's vines snare
/// anyone who stands still among them. Graves can be tended for a little calm. The Waiting Room sends a figure to stand
/// behind whoever it calls. The Broadcast Tower carries words spoken at its mast into the replays. And the Echo's figures
/// say players' words back to them, a few minutes late and not quite right.
/// </summary>
public sealed partial class WGCodaSiteSystem
{
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private Content.Shared.Stacks.SharedStackSystem _stacks = default!;
    [Dependency] private Robust.Shared.Containers.SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private Content.Server.Chat.Managers.IChatManager _chatManager = default!;

    private const string LampProto = "WGLighthouseLamp";
    private const string WaitingFigureProto = "WGWaitingFigure";
    private static readonly string[] ExoticMatter = { "WGExoticMatter", "WGMaterialExoticMatter" };
    private static readonly string[] EchoWords = { "you", "here", "still", "again", "wrong", "home", "us", "never" };

    private readonly Dictionary<(NetUserId User, int Index), bool> _tended = new();
    private readonly Dictionary<EntityUid, (Vector2 At, TimeSpan Since)> _standing = new();
    private readonly List<(NetUserId User, TimeSpan When, string Line)> _whispers = new();
    private readonly List<(EntityUid Grid, string Message, TimeSpan When)> _echoes = new();
    private TimeSpan _nextRefine;

    private void InitializeRefine()
    {
        SubscribeLocalEvent<WGLighthouseKeeperComponent, InteractHandEvent>(OnKeeperTouched);
        SubscribeLocalEvent<WGLastAnchorComponent, InteractUsingEvent>(OnAnchorFed);
        SubscribeLocalEvent<WGGraveRockComponent, InteractHandEvent>(OnGraveTended);
        SubscribeLocalEvent<WGHospiceMarkComponent, ExaminedEvent>(OnMarkExamined);
    }

    private void ResetRefine()
    {
        _tended.Clear();
        _standing.Clear();
        _whispers.Clear();
        _echoes.Clear();
    }

    private void UpdateRefine(TimeSpan now)
    {
        BlinkLighthouses(now);
        if (now < _nextRefine)
            return;

        _nextRefine = now + TimeSpan.FromSeconds(1);
        SnareStillOnes(now);
        DeliverWhispers(now);
        EchoBack(now);
    }

    // The Lighthouse.

    /// <summary>Lights the lamp on first sight and blinks its radar mark, one second on, one off.</summary>
    private void BlinkLighthouses(TimeSpan now)
    {
        var houses = EntityQueryEnumerator<WGLighthouseComponent, MapGridComponent>();
        while (houses.MoveNext(out var grid, out var house, out var gridComp))
        {
            if (house.Lamp == null || TerminatingOrDeleted(house.Lamp.Value))
                house.Lamp = Spawn(LampProto, LampSpot(grid, gridComp));

            if (now < house.NextBlink || !TryComp<RadarBlipComponent>(house.Lamp, out var blip))
                continue;

            house.NextBlink = now + TimeSpan.FromSeconds(1);
            blip.Enabled = !blip.Enabled;
        }
    }

    /// <summary>Where the lamp burns: over the tower's brazier, or the middle of the grid without one.</summary>
    private EntityCoordinates LampSpot(EntityUid grid, MapGridComponent gridComp)
    {
        var query = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out _, out var meta, out var xform))
        {
            if (xform.GridUid == grid && meta.EntityPrototype?.ID == "WGCodaBrazier")
                return xform.Coordinates;
        }

        return new EntityCoordinates(grid, Vector2.Zero);
    }

    /// <summary>The keeper gives each visitor, once a round, a flare and a cup of something warm that calms the head.</summary>
    private void OnKeeperTouched(Entity<WGLighthouseKeeperComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.User, out var actor))
            return;

        args.Handled = true;
        if (!ent.Comp.Given.Add(actor.PlayerSession.UserId))
        {
            _chat.TrySendInGameICMessage(ent, Loc.GetString("wg-codasite-keeper-given"), InGameICChatType.Speak, false);
            return;
        }

        var flare = Spawn(ent.Comp.Gift, Transform(args.User).Coordinates);
        _hands.TryPickupAnyHand(args.User, flare);
        _driftMind.Calm(args.User, ent.Comp.Calm);
        _chat.TrySendInGameICMessage(ent, Loc.GetString("wg-codasite-keeper-gives"), InGameICChatType.Speak, false);
        _popup.PopupEntity(Loc.GetString("wg-codasite-keeper-calm"), args.User, args.User, PopupType.Medium);
    }

    // The Last Anchor.

    /// <summary>A power cell or exotic matter fed to the core holds it longer, or brings it back while it fails.</summary>
    private void OnAnchorFed(Entity<WGLastAnchorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var proto = MetaData(args.Used).EntityPrototype?.ID;
        var exotic = proto != null && ExoticMatter.Contains(proto);
        var cell = HasComp<Content.Shared.PowerCell.Components.PowerCellComponent>(args.Used)
            && TryComp<BatteryComponent>(args.Used, out var battery) && battery.CurrentCharge > 0f;
        if (!exotic && !cell)
            return;

        args.Handled = true;
        var now = _timing.CurTime;
        var gain = TimeSpan.FromSeconds(ent.Comp.FeedSeconds * (exotic ? 3 : 1));
        if (ent.Comp.Holding)
        {
            var until = ent.Comp.NextSwitch + gain;
            var cap = now + ent.Comp.MaxAhead;
            ent.Comp.NextSwitch = until > cap ? cap : until;
        }
        else
        {
            // It catches at once and holds for what it was fed.
            ent.Comp.NextSwitch = now;
            ent.Comp.FedHold = gain;
        }

        // A stack gives up one piece; a cell is used up whole.
        if (!TryComp<Content.Shared.Stacks.StackComponent>(args.Used, out var stack) || !_stacks.Use(args.Used, 1, stack))
            QueueDel(args.Used);
        _popup.PopupEntity(Loc.GetString("wg-codasite-anchor-fed"), ent, args.User, PopupType.Medium);
    }

    // The Hospice.

    /// <summary>Called once a cot has healed someone: an attendant speaks, the patient is marked, and whispers follow.</summary>
    private void MarkHealed(EntityUid patient, EntityUid cot)
    {
        var coords = _transform.GetMapCoordinates(cot);
        var attendants = EntityQueryEnumerator<WGHospiceAttendantComponent, TransformComponent>();
        EntityUid? nearest = null;
        var best = float.MaxValue;
        while (attendants.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != coords.MapId)
                continue;

            var distance = (_transform.GetWorldPosition(xform) - coords.Position).Length();
            if (distance < best)
            {
                best = distance;
                nearest = uid;
            }
        }

        if (nearest is { } attendant && best < 20f)
        {
            _chat.TrySendInGameICMessage(attendant, Loc.GetString($"wg-codasite-hospice-attendant-{_random.Next(1, 5)}"),
                InGameICChatType.Speak, false);
        }

        EnsureComp<WGHospiceMarkComponent>(patient);
        if (!TryComp<ActorComponent>(patient, out var actor))
            return;

        var now = _timing.CurTime;
        foreach (var minutes in new[] { 2, 5, 9 })
        {
            _whispers.Add((actor.PlayerSession.UserId, now + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(_random.Next(0, 40)),
                Loc.GetString($"wg-codasite-hospice-whisper-{_random.Next(1, 6)}")));
        }
    }

    private void DeliverWhispers(TimeSpan now)
    {
        for (var i = _whispers.Count - 1; i >= 0; i--)
        {
            var (user, when, line) = _whispers[i];
            if (now < when)
                continue;

            _whispers.RemoveAt(i);
            if (_players.TryGetSessionById(user, out var session) && session.AttachedEntity is { } mob)
                _popup.PopupEntity(line, mob, session, PopupType.Small);
        }
    }

    private void OnMarkExamined(Entity<WGHospiceMarkComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-codasite-hospice-marked"));
    }

    /// <summary>
    /// Makes a planetside hospital into the Hospice: sealed against space round its whole edge, its beds made into cots
    /// with attendants among them, and nothing hostile left to spawn. It is self-powered and its air never drains.
    /// </summary>
    public void PrepareHospice(EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        EnsureComp<WGSelfPoweredGridComponent>(grid);
        var air = EnsureComp<WGFixedAtmosphereComponent>(grid);
        air.Pressure = 101.3f;
        air.Temperature = 293f;
        air.OxygenPressure = 21.27f;
        EnsureComp<WGHullRegenComponent>(grid);
        EnsureComp<WGCodaHazardImmuneComponent>(grid);
        EnsureComp<Content.Server.Atmos.Components.GridAtmosphereComponent>(grid);

        // Seal: every empty tile touching the grid gets plating and a reinforced wall.
        var plating = _tiles["Plating"].TileId;
        var tiles = _map.GetAllTiles(grid, gridComp).Select(t => t.GridIndices).ToHashSet();
        var edge = new HashSet<Vector2i>();
        foreach (var tile in tiles)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var next = tile + new Vector2i(dx, dy);
                    if (!tiles.Contains(next))
                        edge.Add(next);
                }
            }
        }

        foreach (var tile in edge)
        {
            _map.SetTile(grid, gridComp, tile, new Tile(plating));
            Spawn("WallReinforced", _map.GridTileToLocal(grid, gridComp, tile));
        }

        // Beds become cots; spawners that could bring anything hostile go.
        var cots = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        var swap = new List<(EntityUid Uid, EntityCoordinates At, Angle Rotation)>();
        var drop = new List<EntityUid>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            var id = meta.EntityPrototype?.ID;
            if (id is "MedicalBed" or "ParaBedPsych" or "N14JunkBed1")
                swap.Add((uid, xform.Coordinates, xform.LocalRotation));
            else if (id is "RandomSpawner" or "RandomSpawner100")
                drop.Add(uid);
        }

        foreach (var uid in drop)
            QueueDel(uid);

        foreach (var (uid, at, rotation) in swap)
        {
            QueueDel(uid);
            var cot = Spawn("WGHospiceCot", at);
            Transform(cot).LocalRotation = rotation;
            cots.Add(at);
        }

        _random.Shuffle(cots);
        foreach (var at in cots.Take(2))
            Spawn("WGHospiceAttendant", at.Offset(new Vector2(1f, 0f)));

        Log.Info($"The Hospice: sealed {edge.Count} edge tiles, made {cots.Count} cots.");
    }

    // The Orchard.

    /// <summary>Anyone who stands still among the vines for a few seconds is wound in and slowed until they move off.</summary>
    private void SnareStillOnes(TimeSpan now)
    {
        var vines = new Dictionary<EntityUid, List<Vector2>>();
        var query = EntityQueryEnumerator<WGOrchardVineComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is not { } grid)
                continue;

            if (!vines.TryGetValue(grid, out var list))
                vines[grid] = list = new List<Vector2>();
            list.Add(_transform.GetWorldPosition(xform));
        }

        foreach (var (session, mob) in Players())
        {
            if (Transform(mob).GridUid is not { } grid || !vines.TryGetValue(grid, out var near))
            {
                _standing.Remove(mob);
                continue;
            }

            var at = _transform.GetWorldPosition(mob);
            if (!_standing.TryGetValue(mob, out var still) || (still.At - at).Length() > 0.4f)
            {
                _standing[mob] = (at, now);
                continue;
            }

            if (now - still.Since < TimeSpan.FromSeconds(4) || !near.Any(v => (v - at).Length() < 1.6f))
                continue;

            if (_stun.TrySlowdown(mob, TimeSpan.FromSeconds(3), true, 0.15f, 0.15f) && now - still.Since < TimeSpan.FromSeconds(5))
                _popup.PopupEntity(Loc.GetString("wg-codasite-orchard-snared"), mob, session, PopupType.MediumCaution);
        }
    }

    // The Graveyard Field.

    /// <summary>Tending a named grave gives a little calm, once per grave for each player.</summary>
    private void OnGraveTended(Entity<WGGraveRockComponent> ent, ref InteractHandEvent args)
    {
        var dead = DeadThisRound();
        if (args.Handled || ent.Comp.Index >= dead.Count || !TryComp<ActorComponent>(args.User, out var actor))
            return;

        args.Handled = true;
        var name = dead[ent.Comp.Index];
        if (!_tended.TryAdd((actor.PlayerSession.UserId, ent.Comp.Index), true))
        {
            _popup.PopupEntity(Loc.GetString("wg-codasite-grave-tended-already", ("name", name)), args.User, args.User);
            return;
        }

        _driftMind.Calm(args.User, ent.Comp.Calm);
        _popup.PopupEntity(Loc.GetString("wg-codasite-grave-tended", ("name", name)), args.User, args.User, PopupType.Medium);
    }

    // The Waiting Room.

    /// <summary>A figure stands a few steps behind whoever was called, and is gone when they turn round to look.</summary>
    private void SendFigure(EntityUid mob, ICommonSession session)
    {
        var at = Transform(mob).Coordinates.Offset(_random.NextAngle().ToVec() * _random.NextFloat(2f, 3.5f));
        Spawn(WaitingFigureProto, at);
        _popup.PopupEntity(Loc.GetString("wg-codasite-waiting-behind"), mob, session, PopupType.MediumCaution);
    }

    // The Broadcast Tower and the Echo: spoken words.

    /// <summary>Words said aloud at the tower's mast go into the replays; words said aboard the Echo come back later.</summary>
    private void HeardAloud(EntitySpokeEvent args)
    {
        if (!TryComp<ActorComponent>(args.Source, out var actor))
            return;

        var xform = Transform(args.Source);
        var here = _transform.GetWorldPosition(xform);
        var towers = EntityQueryEnumerator<WGBroadcastTowerComponent, TransformComponent>();
        while (towers.MoveNext(out var tower, out _, out var towerXform))
        {
            if (towerXform.MapID != xform.MapID || (_transform.GetWorldPosition(towerXform) - here).Length() > 3f)
                continue;

            RecordHeard(new WGHeardMessage(Name(args.Source), args.Message, _timing.CurTime, actor.PlayerSession.UserId));
            _popup.PopupEntity(Loc.GetString("wg-codasite-tower-took"), args.Source, actor.PlayerSession, PopupType.Small);
            break;
        }

        if (xform.GridUid is { } grid && HasComp<WGEchoStationComponent>(grid))
        {
            _echoes.Add((grid, args.Message, _timing.CurTime + TimeSpan.FromSeconds(_random.Next(120, 260))));
            if (_echoes.Count > 60)
                _echoes.RemoveAt(0);
        }
    }

    /// <summary>A figure on the Echo says a player's words back, changed a little.</summary>
    private void EchoBack(TimeSpan now)
    {
        for (var i = _echoes.Count - 1; i >= 0; i--)
        {
            var (grid, message, when) = _echoes[i];
            if (now < when)
                continue;

            _echoes.RemoveAt(i);
            var figures = EntityQueryEnumerator<WGEchoFigureComponent, TransformComponent>();
            var pool = new List<EntityUid>();
            while (figures.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    pool.Add(uid);
            }

            if (pool.Count > 0)
                _chat.TrySendInGameICMessage(_random.Pick(pool), Warp(message), InGameICChatType.Speak, false);
        }
    }

    /// <summary>The same words, not quite: one changed, two swapped, or the last one lost.</summary>
    private string Warp(string message)
    {
        var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count == 0)
            return message;

        switch (_random.Next(3))
        {
            case 0:
                words[_random.Next(words.Count)] = _random.Pick(EchoWords);
                break;
            case 1 when words.Count > 2:
                var a = _random.Next(words.Count);
                var b = _random.Next(words.Count);
                (words[a], words[b]) = (words[b], words[a]);
                break;
            default:
                if (words.Count > 1)
                    words.RemoveAt(words.Count - 1);
                break;
        }

        return string.Join(' ', words);
    }
}
