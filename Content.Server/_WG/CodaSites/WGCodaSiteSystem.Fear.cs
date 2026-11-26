using System.Linq;
using System.Text.RegularExpressions;
using Content.Server.Chat.Systems;
using Content.Shared._WG.Lattice;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WG.CodaSites;

/// <summary>
/// The fear sites: the Orchard's vines and hanged, the Graveyard Field's named rocks, the Waiting Room's board and the
/// Broadcast Tower's replays.
/// </summary>
public sealed partial class WGCodaSiteSystem
{
    private static readonly Regex Digits = new(@"\d", RegexOptions.Compiled);

    private readonly List<WGHeardMessage> _heard = new();

    private void InitializeFear()
    {
        SubscribeLocalEvent<WGOrchardVineComponent, DestructionEventArgs>(OnVineCut);
        SubscribeLocalEvent<WGGraveRockComponent, ExaminedEvent>(OnGraveExamined);
        SubscribeLocalEvent<WGGraveRockComponent, DestructionEventArgs>(OnGraveBroken);
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpokeOnRadio);
        SubscribeLocalEvent<WGBroadcastTowerComponent, Content.Server.Radio.RadioReceiveEvent>(OnTowerHears);
    }

    private void ResetFear()
    {
        _heard.Clear();
    }

    private void UpdateFear(TimeSpan now)
    {
        GrowVines(now);
        CallNumbers(now);
        Replay(now);
    }

    // The Orchard.

    private void GrowVines(TimeSpan now)
    {
        var roots = EntityQueryEnumerator<WGOrchardRootComponent, TransformComponent>();
        while (roots.MoveNext(out var uid, out var root, out var xform))
        {
            if (now < root.NextGrow || xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
                continue;

            root.NextGrow = now + root.GrowEvery;
            root.Vines.RemoveAll(v => TerminatingOrDeleted(v));
            if (root.Vines.Count >= root.Cap)
                continue;

            var centre = _transform.GetWorldPosition(xform);
            var target = Players().Select(p => p.Mob)
                .Where(m => Transform(m).GridUid == grid && (_transform.GetWorldPosition(m) - centre).Length() <= root.Range)
                .OrderBy(m => (_transform.GetWorldPosition(m) - centre).Length())
                .FirstOrDefault();
            if (target == default)
                continue;

            // Grow one tile onto the open floor beside any vine that brings it closest to the target.
            var goal = _map.TileIndicesFor(grid, gridComp, Transform(target).Coordinates);
            // Every vine on the grid can grow, the mapped ones as well as those grown since.
            var sources = new List<EntityUid> { uid };
            var vines = EntityQueryEnumerator<WGOrchardVineComponent, TransformComponent>();
            while (vines.MoveNext(out var vine, out _, out var vineXform))
            {
                if (vineXform.GridUid == grid)
                    sources.Add(vine);
            }

            var steps = sources
                .Select(v => _map.TileIndicesFor(grid, gridComp, Transform(v).Coordinates))
                .SelectMany(t => new[] { t + new Vector2i(1, 0), t + new Vector2i(-1, 0), t + new Vector2i(0, 1), t + new Vector2i(0, -1) })
                .Where(t => t != goal && Open(grid, gridComp, t))
                .OrderBy(t => (t - goal).LengthSquared)
                .ToList();
            if (steps.Count == 0)
                continue;

            var step = steps[0];
            root.Vines.Add(Spawn(root.Vine, _map.GridTileToLocal(grid, gridComp, step)));
        }
    }

    /// <summary>Floor with nothing anchored on it.</summary>
    private bool Open(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        if (_map.GetTileRef(grid, gridComp, tile).Tile.IsEmpty)
            return false;

        foreach (var _ in _map.GetAnchoredEntities(grid, gridComp, tile))
            return false;
        return true;
    }

    private void OnVineCut(Entity<WGOrchardVineComponent> ent, ref DestructionEventArgs args)
    {
        foreach (var hanged in _lookup.GetEntitiesInRange<WGOrchardHangedComponent>(Transform(ent).Coordinates, 1.6f))
        {
            // Two vines cut in one tick: a body already rising is not raised twice.
            if (EntityManager.IsQueuedForDeletion(hanged))
                continue;

            if (hanged.Comp.Rises.Count > 0)
                Spawn(_random.Pick(hanged.Comp.Rises), Transform(hanged).Coordinates);
            QueueDel(hanged);
        }
    }

    // The Graveyard Field.

    private void OnGraveExamined(Entity<WGGraveRockComponent> ent, ref ExaminedEvent args)
    {
        var dead = DeadThisRound();
        args.PushMarkup(ent.Comp.Index < dead.Count
            ? Loc.GetString("wg-codasite-grave-name", ("name", dead[ent.Comp.Index]))
            : Loc.GetString("wg-codasite-grave-blank"));
    }

    private void OnGraveBroken(Entity<WGGraveRockComponent> ent, ref DestructionEventArgs args)
    {
        var dead = DeadThisRound();
        if (ent.Comp.Index >= dead.Count)
            return;

        var coords = Transform(ent).Coordinates;
        var nearest = Players()
            .Where(p => Transform(p.Mob).GridUid == Transform(ent).GridUid)
            .OrderBy(p => (_transform.GetWorldPosition(p.Mob) - _transform.ToMapCoordinates(coords).Position).Length())
            .FirstOrDefault();
        if (nearest.Mob == default)
            return;

        _driftMind.Add(nearest.Mob, ent.Comp.Drift, WGDriftFlavour.Unmooring);
        _popup.PopupEntity(Loc.GetString("wg-codasite-grave-weight", ("name", dead[ent.Comp.Index])), nearest.Mob,
            nearest.Session, PopupType.MediumCaution);
    }

    // The Waiting Room.

    private void CallNumbers(TimeSpan now)
    {
        var boards = EntityQueryEnumerator<WGWaitingBoardComponent, TransformComponent>();
        while (boards.MoveNext(out var uid, out var board, out var boardXform))
        {
            // Anyone who comes aboard takes a ticket, and is called from then on, wherever they go.
            foreach (var (visitor, body) in Players())
            {
                if (Transform(body).GridUid == boardXform.GridUid && board.Ticketed.Add(visitor.UserId))
                    _popup.PopupEntity(Loc.GetString("wg-codasite-waiting-ticket"), body, visitor, PopupType.MediumCaution);
            }

            if (board.NextCall == TimeSpan.Zero)
                board.NextCall = now + board.CallEvery;
            if (now < board.NextCall)
                continue;

            board.NextCall = now + board.CallEvery;
            var living = Players()
                .Where(p => board.Ticketed.Contains(p.Session.UserId)
                    && TryComp<MobStateComponent>(p.Mob, out var state) && state.CurrentState == Shared.Mobs.MobState.Alive)
                .OrderBy(p => p.Session.UserId.UserId)
                .ToList();
            if (living.Count == 0)
                continue;

            board.Number++;
            var (session, mob) = living[(board.Number - 1) % living.Count];
            _driftMind.Add(mob, board.Drift, WGDriftFlavour.Unmooring);
            _popup.PopupEntity(Loc.GetString("wg-codasite-waiting-called", ("number", board.Number)), mob, session,
                PopupType.LargeCaution);
            _popup.PopupEntity(Loc.GetString("wg-codasite-waiting-board", ("number", board.Number)), uid);
            SendFigure(mob, session);
        }
    }

    // The Broadcast Tower.

    /// <summary>Words said aloud: the tower's mast and the Echo's figures listen for those. Radio comes through the
    /// mast's own receiver instead, so a message that never reached the air is never recorded.</summary>
    private void OnSpokeOnRadio(EntitySpokeEvent args)
    {
        if (args.Channel == null && !HasComp<WGBroadcastTowerComponent>(args.Source))
            HeardAloud(args);
    }

    /// <summary>The mast's receiver, tuned to Common alone, records what the sector says over the air.</summary>
    private void OnTowerHears(Entity<WGBroadcastTowerComponent> ent, ref Content.Server.Radio.RadioReceiveEvent args)
    {
        if (args.MessageSource == ent.Owner)
            return;

        // Every tower hears the same message; it is logged once.
        var now = _timing.CurTime;
        var said = args.OriginalChatMsg.Message;
        if (_heard.Count > 0 && _heard[^1].When == now && _heard[^1].Message == said)
            return;

        NetUserId? user = TryComp<ActorComponent>(args.MessageSource, out var actor) ? actor.PlayerSession.UserId : null;
        RecordHeard(new WGHeardMessage(Name(args.MessageSource), said, now, user));
    }

    /// <summary>How many messages the tower's log holds at most.</summary>
    private const int HeardCap = 400;

    /// <summary>
    /// Adds to the tower's log, keeping it bounded without losing what is still to be replayed. Once full it first
    /// lets go of the oldest message every tower has already replayed; if none has, it drops one waiting message at
    /// random, so a busy sector thins the replay rather than starving it.
    /// </summary>
    private void RecordHeard(WGHeardMessage message)
    {
        _heard.Add(message);
        if (_heard.Count <= HeardCap)
            return;

        var replayedByAll = int.MaxValue;
        var towers = EntityQueryEnumerator<WGBroadcastTowerComponent>();
        while (towers.MoveNext(out _, out var tower))
        {
            replayedByAll = Math.Min(replayedByAll, tower.Replayed);
        }

        RemoveHeard(replayedByAll > 0 ? 0 : _random.Next(0, _heard.Count - 1));
    }

    /// <summary>Removes one logged message, moving back every tower's place that was past it.</summary>
    private void RemoveHeard(int index)
    {
        _heard.RemoveAt(index);
        var towers = EntityQueryEnumerator<WGBroadcastTowerComponent>();
        while (towers.MoveNext(out _, out var tower))
        {
            if (tower.Replayed > index)
                tower.Replayed--;
        }
    }

    /// <summary>
    /// Sends a replay to everyone in the Coda as if it came over Common, and to no one else: the tower's lies are the
    /// Coda's, not the sector's.
    /// </summary>
    private void BroadcastInCoda(EntityUid tower, string speaker, string text)
    {
        var channel = _proto.Index<Content.Shared.Radio.RadioChannelPrototype>("Common");
        var wrapped = Loc.GetString("chat-radio-message-wrap",
            ("color", channel.Color),
            ("fontType", "Default"),
            ("fontSize", 12),
            ("verb", Loc.GetString("chat-speech-verb-default")),
            ("channel", $"\\[{channel.LocalizedName}\\]"),
            ("name", speaker),
            ("languageColor", channel.Color),
            ("message", text));
        var clients = new List<Robust.Shared.Network.INetChannel>();
        foreach (var (session, mob) in Players())
        {
            if (InCoda(mob))
                clients.Add(session.Channel);
        }

        if (clients.Count > 0)
            _chatManager.ChatMessageToMany(Content.Shared.Chat.ChatChannel.Radio, text, wrapped, tower, false, true, clients);
    }

    /// <summary>Whether someone is in the Coda's hazard bands.</summary>
    private bool InCoda(EntityUid mob)
    {
        var point = _transform.GetMapCoordinates(mob);
        if (_lattice.BandOf(point, out var profile) == null || profile == null || Transform(mob).MapUid is not { } map)
            return false;

        return _lattice.Locate(map, point.Position).Band >= profile.HazardBand;
    }

    private void Replay(TimeSpan now)
    {
        var towers = EntityQueryEnumerator<WGBroadcastTowerComponent>();
        while (towers.MoveNext(out var uid, out var tower))
        {
            // The heard list drops its oldest, so replay by count within what is still kept.
            var due = _heard.Count(m => now - m.When >= tower.Delay);
            tower.Replayed = Math.Min(tower.Replayed, due);
            if (tower.Replayed >= due)
                continue;

            var message = _heard[tower.Replayed];
            tower.Replayed++;
            var speakers = _heard.Select(m => m.Speaker).Distinct().Where(s => s != message.Speaker).ToList();
            var speaker = speakers.Count > 0 ? _random.Pick(speakers) : message.Speaker;
            var text = Digits.Replace(message.Message, _ => _random.Next(10).ToString());
            BroadcastInCoda(uid, speaker, text);
        }
    }
}
