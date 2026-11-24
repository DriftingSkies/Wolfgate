using Content.Server._WG.NpcCrew.Components;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Disposal.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// A janitor's rubbish run: the nearest loose piece of trash on his ship that no other janitor is after (spent
/// medipens are trash too), picked up and put in the nearest disposal unit. <see cref="HTN.WGCrewTidyOperator"/>
/// walks him through it.
/// </summary>
public sealed class WGCrewTidySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private TagSystem _tags = default!;

    private static readonly ProtoId<TagPrototype> Trash = "Trash";

    /// <summary>How far, in metres, a janitor goes for rubbish.</summary>
    private const float Range = 60f;

    private const float Reach = 1.5f;

    /// <summary>A run that takes longer is given up, and its rubbish left a while.</summary>
    private static readonly TimeSpan RunTime = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan SkipTime = TimeSpan.FromMinutes(2);

    private readonly Dictionary<EntityUid, Run> _runs = new();
    private readonly Dictionary<EntityUid, TimeSpan> _skipped = new();

    private sealed class Run
    {
        public EntityUid Item;
        public EntityUid Bin;
        public bool Carrying;
        public TimeSpan Until;
    }

    /// <summary>Where the janitor walks next on a rubbish run, starting one if there is rubbish and a bin.</summary>
    public bool Destination(EntityUid mob, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!_runs.TryGetValue(mob, out var run))
        {
            run = Start(mob);
            if (run == null)
                return false;
        }

        if (!Valid(mob, run))
        {
            End(mob, run, skip: true);
            return false;
        }

        coordinates = Transform(run.Carrying ? run.Bin : run.Item).Coordinates;
        return true;
    }

    /// <summary>One step where he stands: picks the rubbish up, or bins it. True when he must walk again or is done.</summary>
    public bool Perform(EntityUid mob)
    {
        if (!_runs.TryGetValue(mob, out var run))
            return true;
        if (!Valid(mob, run))
        {
            End(mob, run, skip: true);
            return true;
        }

        if (!run.Carrying)
        {
            if (!_interaction.InRangeUnobstructed(mob, run.Item, Reach))
                return true;
            if (!_hands.TryPickupAnyHand(mob, run.Item))
            {
                End(mob, run, skip: true);
                return true;
            }
            run.Carrying = true;
            return true;
        }

        if (!_interaction.InRangeUnobstructed(mob, run.Bin, Reach))
            return true;
        _interaction.InteractUsing(mob, run.Item, run.Bin, Transform(run.Bin).Coordinates, checkCanInteract: false);
        // Still in his hand: the bin wouldn't take it (a whitelist), so it is left a while rather than fetched again.
        var rejected = _hands.IsHolding(mob, run.Item, out _);
        End(mob, run, skip: rejected);
        return true;
    }

    /// <summary>Whether a run can go on: the janitor alive and himself, the rubbish where it should be, time left.</summary>
    private bool Valid(EntityUid mob, Run run)
    {
        if (_timing.CurTime > run.Until || !_mobs.IsAlive(mob) || HasComp<ActorComponent>(mob)
            || TerminatingOrDeleted(run.Item) || TerminatingOrDeleted(run.Bin))
            return false;
        return run.Carrying ? _hands.IsHolding(mob, run.Item, out _) : Loose(run.Item, Transform(mob).GridUid);
    }

    private void End(EntityUid mob, Run run, bool skip)
    {
        _runs.Remove(mob);
        if (run.Carrying && !TerminatingOrDeleted(mob) && _hands.IsHolding(mob, run.Item, out _))
            _hands.TryDrop(mob, run.Item);
        if (skip && !TerminatingOrDeleted(run.Item))
            _skipped[run.Item] = _timing.CurTime + SkipTime;
    }

    /// <summary>Starts a run for a janitor on his ship: the nearest loose, unclaimed rubbish and the nearest bin.</summary>
    private Run? Start(EntityUid mob)
    {
        if (!TryComp<WGCrewComponent>(mob, out var crew) || crew.Duty != WGCrewDuties.Janitor
            || crew.Post?.EntityId is not { } home || Transform(mob).GridUid != home)
            return null;

        var now = _timing.CurTime;
        var here = _transform.GetWorldPosition(mob);
        EntityUid? best = null;
        var distance = Range * Range;
        var tagged = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (tagged.MoveNext(out var uid, out var tags, out var xform))
        {
            if (xform.ParentUid != home || xform.Anchored || !_tags.HasTag(tags, Trash)
                || _skipped.TryGetValue(uid, out var until) && now < until || Claimed(uid))
                continue;
            var squared = (_transform.GetWorldPosition(xform) - here).LengthSquared();
            if (squared < distance)
            {
                best = uid;
                distance = squared;
            }
        }
        if (best is not { } item || Bin(home, Transform(item).Coordinates) is not { } bin)
            return null;

        var run = new Run { Item = item, Bin = bin, Until = now + RunTime };
        _runs[mob] = run;
        return run;
    }

    /// <summary>Lying loose on the grid's floor: not in a bag, a hand or a bin, not fixed down.</summary>
    private bool Loose(EntityUid item, EntityUid? grid)
    {
        var xform = Transform(item);
        return grid != null && xform.ParentUid == grid && !xform.Anchored;
    }

    private bool Claimed(EntityUid item)
    {
        foreach (var run in _runs.Values)
        {
            if (run.Item == item)
                return true;
        }
        return false;
    }

    /// <summary>The disposal unit on the grid nearest a point.</summary>
    private EntityUid? Bin(EntityUid grid, EntityCoordinates near)
    {
        EntityUid? best = null;
        var distance = float.MaxValue;
        var at = _transform.ToMapCoordinates(near).Position;
        var bins = EntityQueryEnumerator<DisposalUnitComponent, TransformComponent>();
        while (bins.MoveNext(out var bin, out _, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;
            var squared = (_transform.GetWorldPosition(xform) - at).LengthSquared();
            if (squared < distance)
            {
                best = bin;
                distance = squared;
            }
        }
        return best;
    }

    private float _timer;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 5f)
            return;
        _timer = 0f;
        var now = _timing.CurTime;
        // A run whose janitor is gone, or that ran out of time without him coming back to it, frees its rubbish.
        foreach (var (mob, run) in new List<KeyValuePair<EntityUid, Run>>(_runs))
        {
            // A janitor still about whose run timed out leaves that rubbish a while, as a failed run does.
            if (TerminatingOrDeleted(mob))
                End(mob, run, skip: false);
            else if (now > run.Until)
                End(mob, run, skip: true);
        }
        foreach (var (item, until) in new List<KeyValuePair<EntityUid, TimeSpan>>(_skipped))
        {
            if (now >= until || TerminatingOrDeleted(item))
                _skipped.Remove(item);
        }
    }
}
