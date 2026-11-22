using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.Wolfmed.Consciousness;
using Content.Shared.Chat;
using Content.Shared.Doors.Components;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>How a mustered crew stands towards the hostiles aboard.</summary>
public enum WGCrewMusterPosture : byte
{
    /// <summary>Not mustered: each crewman fights by his own skill.</summary>
    None,

    /// <summary>Gathered at the rally point with every gun on the ways in; nobody goes after anyone.</summary>
    Hold,

    /// <summary>Going after the hostiles together.</summary>
    Push,
}

/// <summary>
/// Mustering: a crew that is losing a boarding fight stops meeting the boarder one or two at a time. With two of its
/// own down, on the floor conscious or not, and a crewman aboard whose skill musters, its fighters pull back to a post
/// the hostile is neither near nor looking at, hold it together, and go after him as one once he leaves them alone.
/// Meanwhile the helm and the guns stay manned. The leader calls both moves aloud first; with nobody left to call
/// them, each crewman fights by his own skill again.
/// </summary>
public sealed class WGCrewMusterSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WGCrewAlertSystem _alerts = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;

    /// <summary>How many of a crew must go down within <see cref="LossMemory"/> for it to muster.</summary>
    public const int LossesToMuster = 2;

    /// <summary>How many fighters must be on their feet for a muster to be called, and to keep one going.</summary>
    public const int MinToMuster = 3;
    public const int MinToHold = 2;

    /// <summary>How long a downed crewman counts towards a muster.</summary>
    private static readonly TimeSpan LossMemory = TimeSpan.FromSeconds(120);

    /// <summary>How long the crew holds without sight of a hostile before it pushes.</summary>
    public static readonly TimeSpan HoldQuiet = TimeSpan.FromSeconds(10);

    /// <summary>How long the hold waits for stragglers before pushing without them.</summary>
    private static readonly TimeSpan HoldLimit = TimeSpan.FromSeconds(45);

    /// <summary>The least a push lasts, and how long after the last sight of a hostile it carries on.</summary>
    private static readonly TimeSpan PushTime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PushLull = TimeSpan.FromSeconds(10);

    /// <summary>How long without sight of a hostile before the muster is given up.</summary>
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(90);

    /// <summary>How long a muster outlasts the last hostile aboard, in case he comes back.</summary>
    private static readonly TimeSpan AbsentGrace = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan CheckGap = TimeSpan.FromSeconds(1);

    /// <summary>How long a crew that could not muster waits before trying again.</summary>
    private static readonly TimeSpan CallRetry = TimeSpan.FromSeconds(5);

    /// <summary>The closest a hostile may be to a rally point, in tiles, and how far he is taken to see.</summary>
    private const float Standoff = 5f;
    private const float SightRange = 10f;

    /// <summary>How far from the rally point the crew's places are, in tiles, how far apart, and how close counts as in place.</summary>
    private const int PlaceRange = 2;
    private const float PlaceSpacing = 1.5f;
    private const float InPlace = 1f;

    private static readonly LocId[] MusterLines = { "wg-crew-muster-1", "wg-crew-muster-2", "wg-crew-muster-3" };
    private static readonly LocId[] PushLines = { "wg-crew-push-1", "wg-crew-push-2", "wg-crew-push-3" };

    private readonly Dictionary<(EntityUid Grid, string Group), Muster> _musters = new();
    private readonly Dictionary<EntityUid, Muster> _followers = new();
    private readonly Dictionary<(EntityUid Grid, string Group), Losses> _losses = new();
    private readonly List<(EntityUid Grid, string Group)> _keys = new();
    private readonly List<EntityUid> _fighters = new();
    private readonly List<EntityUid> _hostiles = new();
    private readonly List<EntityUid> _gone = new();
    private readonly List<(EntityCoordinates Post, float Travel)> _posts = new();
    private readonly List<(MapCoordinates Spot, float Clear)> _spots = new();
    private readonly List<MapCoordinates> _picked = new();
    private TimeSpan _nextCheck;

    private sealed class Losses
    {
        /// <summary>When each crewman went down. One who is down again, or worse, is not counted twice.</summary>
        public readonly Dictionary<EntityUid, TimeSpan> Fallen = new();

        /// <summary>Losses up to here went into a muster that has ended; the next takes fresh ones.</summary>
        public TimeSpan Spent;

        public TimeSpan NextCall;
    }

    private sealed class Muster
    {
        public EntityUid Leader;
        public WGCrewMusterPosture Posture;

        /// <summary>When the current posture began.</summary>
        public TimeSpan Since;

        /// <summary>When a follower last had a hostile in sight.</summary>
        public TimeSpan Contact;

        /// <summary>When a hostile was last aboard.</summary>
        public TimeSpan Aboard;

        public readonly HashSet<EntityUid> Followers = new();
        public readonly Dictionary<EntityUid, EntityCoordinates> Places = new();

        /// <summary>Places nobody holds yet, for a fighter who joins late.</summary>
        public readonly List<EntityCoordinates> Spare = new();
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCrewMemberDownEvent>(OnCrewDown);
        SubscribeLocalEvent<WGCrewComponent, WolfmedConsciousnessChangedEvent>(OnConsciousnessChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _musters.Clear();
            _followers.Clear();
            _losses.Clear();
        });
    }

    /// <summary>The posture of the muster this crewman follows.</summary>
    public WGCrewMusterPosture Posture(EntityUid uid)
    {
        return _followers.TryGetValue(uid, out var muster) ? muster.Posture : WGCrewMusterPosture.None;
    }

    /// <summary>Where this crewman stands while his crew holds.</summary>
    public bool TryGetPlace(EntityUid uid, out EntityCoordinates place)
    {
        place = default;
        return _followers.TryGetValue(uid, out var muster) && muster.Posture == WGCrewMusterPosture.Hold
               && muster.Places.TryGetValue(uid, out place);
    }

    /// <summary>Whether a crew is mustered: those of it who are not in the muster leave the hunting to it.</summary>
    public bool Stands(EntityUid grid, string group)
    {
        return _musters.ContainsKey((grid, group));
    }

    private void OnCrewDown(ref WGCrewMemberDownEvent args)
    {
        if (TryComp<WGCrewComponent>(args.Mob, out var crew))
            Lost(args.Mob, crew);
    }

    /// <summary>A crewman on the floor is out of the fight while his mob state still says alive.</summary>
    private void OnConsciousnessChanged(Entity<WGCrewComponent> ent, ref WolfmedConsciousnessChangedEvent args)
    {
        if (args.OldState == WolfmedConsciousness.Up && args.NewState != WolfmedConsciousness.Up)
            Lost(ent, ent.Comp);
    }

    private void Lost(EntityUid mob, WGCrewComponent crew)
    {
        if (HasComp<ActorComponent>(mob) || _crew.HomeGrid(mob, crew) is not { } grid)
            return;

        var key = (grid, crew.Group);
        if (!_losses.TryGetValue(key, out var losses))
            _losses[key] = losses = new Losses();
        losses.Fallen.TryAdd(mob, _timing.CurTime);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;
        _nextCheck = now + CheckGap;

        _keys.Clear();
        foreach (var (key, losses) in _losses)
        {
            var fresh = 0;
            foreach (var (mob, at) in losses.Fallen)
            {
                if (now >= at + LossMemory)
                    losses.Fallen.Remove(mob);
                else if (at > losses.Spent)
                    fresh++;
            }

            if (losses.Fallen.Count == 0)
                _losses.Remove(key);
            else if (fresh >= LossesToMuster && now >= losses.NextCall && !_musters.ContainsKey(key))
                _keys.Add(key);
        }

        foreach (var key in _keys)
        {
            if (!Call(key, now))
                _losses[key].NextCall = now + CallRetry;
        }

        _keys.Clear();
        _keys.AddRange(_musters.Keys);
        foreach (var key in _keys)
            Maintain(key, _musters[key], now);
    }

    /// <summary>Calls a muster if the crew has a leader, the fighters and a rally point for one.</summary>
    private bool Call((EntityUid Grid, string Group) key, TimeSpan now)
    {
        if (!TryComp<MapGridComponent>(key.Grid, out var grid)
            || Gather(key, null) is not { } leader || _fighters.Count < MinToMuster || !KnowsHostiles(key))
            return false;

        var muster = new Muster { Leader = leader, Contact = now, Aboard = now };
        if (!Form(key, grid, muster, now))
            return false;

        _musters[key] = muster;
        return true;
    }

    /// <summary>Keeps a muster's followers, their grudge and its posture current, and ends it when it has no leader, too few fighters or no hostile left.</summary>
    private void Maintain((EntityUid Grid, string Group) key, Muster muster, TimeSpan now)
    {
        if (!TryComp<MapGridComponent>(key.Grid, out var grid)
            || Gather(key, muster.Leader) is not { } leader || _fighters.Count < MinToHold)
        {
            Dissolve(key, muster);
            return;
        }

        muster.Leader = leader;
        _gone.Clear();
        foreach (var uid in muster.Followers)
        {
            if (!_fighters.Contains(uid))
                _gone.Add(uid);
        }

        foreach (var uid in _gone)
            Release(muster, uid);

        foreach (var uid in _fighters)
        {
            if (!muster.Followers.Add(uid))
                continue;

            _followers[uid] = muster;
            if (muster.Spare.Count > 0)
            {
                muster.Places[uid] = muster.Spare[^1];
                muster.Spare.RemoveAt(muster.Spare.Count - 1);
            }

            Stir(uid);
        }

        if (KnowsHostiles(key))
            muster.Aboard = now;
        else if (now >= muster.Aboard + AbsentGrace)
        {
            Dissolve(key, muster);
            return;
        }

        foreach (var uid in _fighters)
        {
            TryComp<NPCRetaliationComponent>(uid, out var retaliation);
            foreach (var hostile in _hostiles)
            {
                // The crew's grudge outlasts any one man's memory of the attack for as long as it stands together.
                if (retaliation != null && !_factions.IsIgnored(uid, hostile))
                    _crew.Remember(uid, retaliation, hostile);
                if (_weapons.CanSee(uid, hostile))
                    muster.Contact = now;
            }

            // Weapons stay up on the way to the rally point, not only once there.
            if (muster.Posture == WGCrewMusterPosture.Hold && _hostiles.Count > 0
                && TryComp<WGCrewWeaponComponent>(uid, out var weapon))
                _weapons.Brace((uid, weapon), _weapons.IsBracing((uid, weapon)) ? weapon.Braced!.Value : _hostiles[0]);
        }

        if (now >= muster.Contact + GiveUp)
        {
            Dissolve(key, muster);
            return;
        }

        if (muster.Posture == WGCrewMusterPosture.Push)
        {
            // Out of contact, the push is spent: back to a rally point, wherever one can now be had.
            if (now >= muster.Since + PushTime && now >= muster.Contact + PushLull && !Form(key, grid, muster, now))
                Dissolve(key, muster);
            return;
        }

        var quietSince = muster.Contact > muster.Since ? muster.Contact : muster.Since;
        if (now < quietSince + HoldQuiet || !Gathered(muster) && now < muster.Since + HoldLimit)
            return;

        muster.Posture = WGCrewMusterPosture.Push;
        muster.Since = now;
        foreach (var uid in muster.Followers)
            Stir(uid);
        Shout(muster.Leader, PushLines);
    }

    /// <summary>
    /// Puts the crew in a hold at a fresh rally point, called aloud by the leader. Expects <see cref="_fighters"/> and
    /// <see cref="_hostiles"/> to be current. False when there is no point to hold.
    /// </summary>
    private bool Form((EntityUid Grid, string Group) key, MapGridComponent grid, Muster muster, TimeSpan now)
    {
        if (!PickRally(key, grid))
            return false;

        muster.Places.Clear();
        muster.Spare.Clear();
        // The best places are first; the rest are kept for whoever joins late.
        for (var i = _picked.Count - 1; i >= _fighters.Count; i--)
        {
            muster.Spare.Add(_xform.ToCoordinates(key.Grid, _picked[i]));
            _picked.RemoveAt(i);
        }

        muster.Posture = WGCrewMusterPosture.Hold;
        muster.Since = now;
        foreach (var uid in _fighters)
        {
            muster.Followers.Add(uid);
            _followers[uid] = muster;
            if (_picked.Count > 0)
            {
                var here = _xform.GetWorldPosition(uid);
                var nearest = 0;
                for (var i = 1; i < _picked.Count; i++)
                {
                    if (Vector2.DistanceSquared(_picked[i].Position, here) < Vector2.DistanceSquared(_picked[nearest].Position, here))
                        nearest = i;
                }

                muster.Places[uid] = _xform.ToCoordinates(key.Grid, _picked[nearest]);
                _picked.RemoveAt(nearest);
            }

            Stir(uid);
        }

        Shout(muster.Leader, MusterLines);
        return true;
    }

    private void Dissolve((EntityUid Grid, string Group) key, Muster muster)
    {
        _gone.Clear();
        _gone.AddRange(muster.Followers);
        foreach (var uid in _gone)
            Release(muster, uid);

        _musters.Remove(key);
        if (_losses.TryGetValue(key, out var losses))
            losses.Spent = _timing.CurTime;
    }

    private void Release(Muster muster, EntityUid uid)
    {
        muster.Followers.Remove(uid);
        // His place is kept for him, or for whoever joins next.
        if (muster.Places.Remove(uid, out var place))
            muster.Spare.Add(place);
        if (_followers.TryGetValue(uid, out var followed) && followed == muster)
            _followers.Remove(uid);
        if (!TerminatingOrDeleted(uid))
            Stir(uid);
    }

    /// <summary>Has a crewman look round and plan afresh, so a change of posture takes on this tick.</summary>
    private void Stir(EntityUid uid)
    {
        _weapons.ForgetThreat(uid);
        if (TryComp<HTNComponent>(uid, out var htn))
            _htn.Replan(htn);
    }

    /// <summary>
    /// Lists the crew's fighters on their feet aboard in <see cref="_fighters"/>, and returns who leads them: the
    /// current leader while he stands, else the most seasoned crewman aboard whose skill musters.
    /// </summary>
    private EntityUid? Gather((EntityUid Grid, string Group) key, EntityUid? current)
    {
        _fighters.Clear();
        EntityUid? leader = null;
        var best = -1;
        var query = EntityQueryEnumerator<WGCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (crew.Group != key.Group || xform.GridUid != key.Grid || crew.Engagement == WGCrewEngagement.Never
                || HasComp<ActorComponent>(uid) || _crew.HomeGrid(uid, crew) != key.Grid || !Standing(uid))
                continue;

            var level = uid == current ? int.MaxValue : (int) WGCrewSkills.Effective(crew);
            if (WGCrewSkills.Of(crew).Musters && level > best)
            {
                best = level;
                leader = uid;
            }

            if (WGCrewSystem.IsFighter(crew) && !_weapons.IsStationCrew(uid))
                _fighters.Add(uid);
        }

        return leader;
    }

    /// <summary>Alive and, for a body the consciousness system drives, on his feet.</summary>
    private bool Standing(EntityUid uid)
    {
        return _mobs.IsAlive(uid)
               && CompOrNull<WolfmedConsciousnessComponent>(uid)?.State is null or WolfmedConsciousness.Up;
    }

    /// <summary>
    /// Lists in <see cref="_hostiles"/> the living hostiles aboard that the crew's alert or any of
    /// <see cref="_fighters"/> knows. False when there are none.
    /// </summary>
    private bool KnowsHostiles((EntityUid Grid, string Group) key)
    {
        _hostiles.Clear();
        if (_fighters.Count == 0)
            return false;

        foreach (var hostile in _alerts.GetSharedHostiles(key.Grid, key.Group))
            Add(hostile);

        foreach (var uid in _fighters)
        {
            foreach (var hostile in _factions.GetHostiles(uid))
                Add(hostile);
        }

        return _hostiles.Count > 0;

        void Add(EntityUid hostile)
        {
            if (_hostiles.Contains(hostile) || TerminatingOrDeleted(hostile) || !_mobs.IsAlive(hostile)
                || Transform(hostile).GridUid != key.Grid
                || _crew.SameCrew(_fighters[0], hostile) || _crew.Forgives(_fighters[0], hostile))
                return;

            _hostiles.Add(hostile);
        }
    }

    /// <summary>
    /// Picks the rally point and lists its places in <see cref="_picked"/>: of the crew's posts aboard that no hostile
    /// is near or looking at, the one its fighters have the least way to go to that has a place for each of them.
    /// Failing that, the nearest with room for a hold; whoever gets no place covers from where he stands.
    /// </summary>
    private bool PickRally((EntityUid Grid, string Group) key, MapGridComponent grid)
    {
        _posts.Clear();
        var query = EntityQueryEnumerator<WGCrewComponent>();
        while (query.MoveNext(out _, out var crew))
        {
            if (crew.Group != key.Group || crew.Post is not { } post || post.EntityId != key.Grid)
                continue;

            var spot = _xform.ToMapCoordinates(post);
            if (Exposed(spot))
                continue;

            var travel = 0f;
            foreach (var uid in _fighters)
                travel += Vector2.Distance(_xform.GetWorldPosition(uid), spot.Position);
            _posts.Add((post, travel));
        }

        _posts.Sort((a, b) => a.Travel.CompareTo(b.Travel));
        EntityCoordinates? cramped = null;
        foreach (var (post, _) in _posts)
        {
            if (!PickPlaces(key.Grid, grid, post))
                continue;
            if (_picked.Count >= _fighters.Count)
                return true;

            cramped ??= post;
        }

        return cramped is { } tight && PickPlaces(key.Grid, grid, tight);
    }

    /// <summary>Whether a hostile is within the standoff of a spot or has a line of sight to it.</summary>
    private bool Exposed(MapCoordinates spot)
    {
        foreach (var hostile in _hostiles)
        {
            var eyes = _xform.GetMapCoordinates(hostile);
            var distance = Vector2.Distance(eyes.Position, spot.Position);
            if (distance < Standoff
                || distance <= SightRange && _interaction.InRangeUnobstructed(eyes, spot, distance + 0.01f,
                    CollisionGroup.Opaque, e => e == hostile || !Transform(e).Anchored))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Lists in <see cref="_picked"/> the places round the rally point: open floor a straight walk from it, the
    /// farthest from the hostiles first so the line faces the way in, spread apart while the room allows. False when
    /// there is not room for a hold.
    /// </summary>
    private bool PickPlaces(EntityUid gridUid, MapGridComponent grid, EntityCoordinates point)
    {
        var centre = _xform.ToMapCoordinates(point);
        var origin = _maps.TileIndicesFor(gridUid, grid, centre);
        _spots.Clear();
        for (var dx = -PlaceRange; dx <= PlaceRange; dx++)
        {
            for (var dy = -PlaceRange; dy <= PlaceRange; dy++)
            {
                // The post itself is left to whoever works it.
                if (dx == 0 && dy == 0)
                    continue;

                var tile = origin + new Vector2i(dx, dy);
                var spot = _maps.GridTileToWorld(gridUid, grid, tile);
                var distance = Vector2.Distance(spot.Position, centre.Position);
                if (!_maps.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty
                    || _turf.IsTileBlocked(tileRef, CollisionGroup.Impassable) || Doorway(gridUid, grid, tile)
                    || !_interaction.InRangeUnobstructed(centre, spot, distance + 0.01f, CollisionGroup.Impassable,
                        e => !Transform(e).Anchored))
                    continue;

                var clear = float.MaxValue;
                foreach (var hostile in _hostiles)
                    clear = Math.Min(clear, Vector2.Distance(_xform.GetWorldPosition(hostile), spot.Position));
                _spots.Add((spot, clear));
            }
        }

        _spots.Sort((a, b) => b.Clear.CompareTo(a.Clear));
        _picked.Clear();
        foreach (var (spot, _) in _spots)
        {
            var spaced = true;
            foreach (var taken in _picked)
                spaced &= Vector2.Distance(taken.Position, spot.Position) >= PlaceSpacing;
            if (spaced)
                _picked.Add(spot);
        }

        // A tight room: shoulder to shoulder beats one man left outside.
        foreach (var (spot, _) in _spots)
        {
            if (_picked.Count >= _fighters.Count)
                break;
            if (!_picked.Contains(spot))
                _picked.Add(spot);
        }

        return _picked.Count >= MinToHold;
    }

    private bool Doorway(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<DoorComponent>(uid))
                return true;
        }

        return false;
    }

    /// <summary>Whether most of those with a place, and two at the least, stand in it.</summary>
    private bool Gathered(Muster muster)
    {
        var there = 0;
        foreach (var (uid, place) in muster.Places)
        {
            if (Transform(uid).Coordinates.TryDistance(EntityManager, place, out var distance) && distance <= InPlace)
                there++;
        }

        return there >= Math.Max(MinToHold, (muster.Places.Count + 1) / 2);
    }

    /// <summary>Says a call aloud. It is the boarder's warning as much as the crew's order, so no speech cooldown holds it back.</summary>
    private void Shout(EntityUid uid, LocId[] lines)
    {
        _chat.TrySendInGameICMessage(uid, Loc.GetString(_random.Pick(lines)), InGameICChatType.Speak,
            hideChat: false, checkRadioPrefix: false);
    }
}
