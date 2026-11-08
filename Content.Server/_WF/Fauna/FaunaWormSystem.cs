using System.Linq;
using System.Numerics;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Moves the body of <see cref="FaunaWormComponent"/> worms along the head's trail, picks their sprites, and swallows
/// and digests what their charge hits.
/// </summary>
public sealed partial class FaunaWormSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StackSystem _stack = default!;

    private readonly HashSet<Entity<ItemComponent>> _items = new();

    private static readonly string[] HeadStates = { "spacewormhead0", "spacewormhead1" };
    private static readonly string[] HeadHuntStates = { "spacewormhead0_hunt", "spacewormhead1_hunt" };
    private static readonly string[] HeadDeadStates = { "spacewormhead0_dead", "spacewormhead1_dead" };
    private const string BodyState = "spaceworm3";
    private const string TailState = "spacewormtail";

    // A longer jump, such as a teleport, restarts the path instead of filling it in.
    private const float MaxJump = 8f;
    private const float TrailSlack = 1.5f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaWormComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FaunaWormComponent, FaunaChargeHitEvent>(OnChargeHit);
        SubscribeLocalEvent<FaunaWormComponent, MobStateChangedEvent>(OnHeadStateChanged);
        SubscribeLocalEvent<FaunaWormComponent, EntityTerminatingEvent>(OnHeadTerminating);
        SubscribeLocalEvent<FaunaWormSegmentComponent, MobStateChangedEvent>(OnSegmentStateChanged);
        SubscribeLocalEvent<FaunaWormSegmentComponent, EntityTerminatingEvent>(OnSegmentTerminating);
        SubscribeLocalEvent<FaunaWormSegmentComponent, PreventCollideEvent>(OnSegmentPreventCollide);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaWormComponent>();
        while (query.MoveNext(out var uid, out var worm))
        {
            if (!_mobState.IsAlive(uid))
                continue;

            UpdateTrail((uid, worm));
            UpdateHeadState((uid, worm));

            // Every update, so a moving head eats what it passes over.
            if (worm.SwallowItemRange > 0)
                SwallowItems((uid, worm));

            if (now < worm.NextTick)
                continue;

            worm.NextTick = now + worm.TickInterval;

            if (_random.Prob(worm.ProcessChance))
                Digest((uid, worm));

            if (worm.ShareDamage)
                ShareDamage((uid, worm));
        }
    }

    private void ShareDamage(Entity<FaunaWormComponent> ent)
    {
        var ahead = ent.Owner;
        // Index loop: SetDamage can kill a segment, and Sever then shortens the list.
        for (var i = 0; i < ent.Comp.Segments.Count; i++)
        {
            var segment = ent.Comp.Segments[i];
            if (TerminatingOrDeleted(segment) || !_mobState.IsAlive(segment))
                break;

            if (TryComp<DamageableComponent>(ahead, out var front) && TryComp<DamageableComponent>(segment, out var back))
            {
                var even = (front.Damage + back.Damage) / 2f;
                _damageable.SetDamage(ahead, front, even);
                _damageable.SetDamage(segment, back, new DamageSpecifier(even));
            }

            ahead = segment;
        }
    }

    private void OnMapInit(Entity<FaunaWormComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Stomach = _container.EnsureContainer<Container>(ent, ent.Comp.StomachId);
        ent.Comp.NextTick = _timing.CurTime + ent.Comp.TickInterval;

        var coords = Transform(ent).Coordinates;
        for (var i = 0; i < ent.Comp.SegmentCount; i++)
        {
            var segment = Spawn(ent.Comp.SegmentPrototype, coords);
            EnsureComp<FaunaWormSegmentComponent>(segment).Head = ent;
            ent.Comp.Segments.Add(segment);
        }

        UpdateSegments(ent);
        UpdateHeadState(ent);
    }

    private void OnChargeHit(Entity<FaunaWormComponent> ent, ref FaunaChargeHitEvent args)
    {
        if (HasComp<FaunaWormSegmentComponent>(args.Target) || HasComp<FaunaWormComponent>(args.Target))
            return;

        if (!_container.Insert(args.Target, ent.Comp.Stomach) || ent.Comp.SwallowPopup is not { } popup)
            return;

        var text = Loc.GetString(popup, ("user", ent.Owner), ("target", args.Target));
        _popup.PopupEntity(text, ent, PopupType.LargeCaution);
    }

    private void OnHeadStateChanged(Entity<FaunaWormComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        _container.EmptyContainer(ent.Comp.Stomach);
        Sever(ent, 0);
        UpdateHeadState(ent);
    }

    private void OnHeadTerminating(Entity<FaunaWormComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Stomach != null)
            _container.EmptyContainer(ent.Comp.Stomach);

        Sever(ent, 0);
    }

    private void OnSegmentStateChanged(Entity<FaunaWormSegmentComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            SeverAt(ent);
    }

    private void OnSegmentTerminating(Entity<FaunaWormSegmentComponent> ent, ref EntityTerminatingEvent args)
    {
        SeverAt(ent);
    }

    /// <summary>
    /// Lets the head and its own segments pass through each other, so a charge reaches its target.
    /// </summary>
    private void OnSegmentPreventCollide(Entity<FaunaWormSegmentComponent> ent, ref PreventCollideEvent args)
    {
        if (ent.Comp.Head is not { } head)
            return;

        if (args.OtherEntity == head
            || TryComp<FaunaWormSegmentComponent>(args.OtherEntity, out var other) && other.Head == head)
            args.Cancelled = true;
    }

    private void SeverAt(Entity<FaunaWormSegmentComponent> ent)
    {
        if (ent.Comp.Head is not { } head || !TryComp<FaunaWormComponent>(head, out var worm))
            return;

        var index = worm.Segments.IndexOf(ent);
        if (index >= 0)
            Sever((head, worm), index);
    }

    /// <summary>
    /// Detaches every segment from <paramref name="index"/> to the tail and kills them, like a cut worm.
    /// </summary>
    private void Sever(Entity<FaunaWormComponent> ent, int index)
    {
        if (index >= ent.Comp.Segments.Count)
            return;

        var cut = ent.Comp.Segments.GetRange(index, ent.Comp.Segments.Count - index);
        ent.Comp.Segments.RemoveRange(index, cut.Count);
        TrimTrail(ent);

        foreach (var segment in cut)
        {
            if (TerminatingOrDeleted(segment))
                continue;

            if (TryComp<FaunaWormSegmentComponent>(segment, out var comp))
                comp.Head = null;

            if (_mobState.IsAlive(segment))
                _mobState.ChangeMobState(segment, MobState.Dead);

            SetState(segment, AddDead(GetState(segment)));
        }

        UpdateSegments(ent);
    }

    private void UpdateTrail(Entity<FaunaWormComponent> ent)
    {
        var head = Transform(ent).Coordinates;
        if (ent.Comp.LastHead is { } last && last.Equals(head))
            return;

        ent.Comp.LastHead = head;
        var trail = ent.Comp.Trail;
        if (trail.Count > 0 && trail[0].EntityId != head.EntityId)
            MoveTrail(ent, head.EntityId);

        if (trail.Count == 0 || (head.Position - trail[0].Position).Length() > MaxJump)
        {
            trail.Clear();
            trail.Add(head);
        }

        var spacing = MathF.Max(ent.Comp.PointSpacing, 0.05f);

        // A step back, such as an NPC juke, retracts the path so the body slides back along it.
        while (trail.Count > 1 &&
               ((head.Position - trail[1].Position).Length() < spacing ||
                Vector2.Dot(head.Position - trail[0].Position, trail[0].Position - trail[1].Position) < 0f))
        {
            trail.RemoveAt(0);
        }

        while ((head.Position - trail[0].Position).Length() >= spacing)
        {
            var step = Vector2.Normalize(head.Position - trail[0].Position) * spacing;
            trail.Insert(0, trail[0].Offset(step));
        }

        TrimTrail(ent);
        UpdateSegments(ent);
    }

    /// <summary>
    /// Moves the path onto the head's new parent, such as when it crosses from a grid into space.
    /// </summary>
    private void MoveTrail(Entity<FaunaWormComponent> ent, EntityUid parent)
    {
        var trail = ent.Comp.Trail;
        for (var i = trail.Count - 1; i >= 0; i--)
        {
            if (TerminatingOrDeleted(trail[i].EntityId))
                trail.RemoveAt(i);
            else
                trail[i] = _transform.WithEntityId(trail[i], parent);
        }
    }

    /// <summary>
    /// Drops path points past the tail, keeping some slack so a retract still leaves the tail a path.
    /// </summary>
    private void TrimTrail(Entity<FaunaWormComponent> ent)
    {
        var spacing = MathF.Max(ent.Comp.PointSpacing, 0.05f);
        var max = (int) MathF.Ceiling(ent.Comp.Segments.Count * ent.Comp.SegmentSpacing / spacing * TrailSlack) + 2;
        if (ent.Comp.Trail.Count > max)
            ent.Comp.Trail.RemoveRange(max, ent.Comp.Trail.Count - max);
    }

    /// <summary>
    /// Places each segment a fixed distance back along the head's path, facing along it.
    /// </summary>
    private void UpdateSegments(Entity<FaunaWormComponent> ent)
    {
        var segments = ent.Comp.Segments;
        var trail = ent.Comp.Trail;
        var head = Transform(ent).Coordinates;

        // Walks the path from the head, which is point -1, carrying the arc length covered so far.
        var index = -1;
        var front = head.Position;
        var walked = 0f;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var wanted = (i + 1) * ent.Comp.SegmentSpacing;
            var here = front;
            var facing = Vector2.Zero;
            while (true)
            {
                if (index + 1 >= trail.Count)
                {
                    here = trail.Count > 0 ? trail[^1].Position : head.Position;
                    break;
                }

                var back = trail[index + 1].Position;
                var length = (front - back).Length();
                if (length > 0f)
                    facing = front - back;

                if (walked + length >= wanted)
                {
                    here = length > 0f ? Vector2.Lerp(front, back, (wanted - walked) / length) : back;
                    break;
                }

                walked += length;
                front = back;
                index++;
            }

            _transform.SetCoordinates(segment, new EntityCoordinates(head.EntityId, here));
            if (facing != Vector2.Zero)
                _transform.SetLocalRotation(segment, facing.ToWorldAngle());

            SetState(segment, i + 1 >= segments.Count ? TailState : BodyState);
        }
    }

    private void UpdateHeadState(Entity<FaunaWormComponent> ent)
    {
        var body = ent.Comp.Segments.Count > 0 ? 1 : 0;
        string[] states;
        if (!_mobState.IsAlive(ent))
            states = HeadDeadStates;
        else if (!TryComp<FaunaChargeComponent>(ent, out var charge) || !charge.Charging)
            states = HeadHuntStates;
        else
            states = HeadStates;

        SetState(ent, states[body]);
    }

    private void SwallowItems(Entity<FaunaWormComponent> ent)
    {
        _items.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.SwallowItemRange, _items);
        foreach (var item in _items)
        {
            if (Transform(item).Anchored ||
                _container.IsEntityInContainer(item) ||
                HasComp<MobStateComponent>(item))
                continue;

            _container.Insert(item.Owner, ent.Comp.Stomach);
        }
    }

    private void Digest(Entity<FaunaWormComponent> ent)
    {
        var coords = Transform(ent).Coordinates;
        foreach (var eaten in ent.Comp.Stomach.ContainedEntities.ToList())
        {
            if (!_random.Prob(ent.Comp.DigestChance))
                continue;

            int amount;
            if (TryComp<StackComponent>(eaten, out var stack))
            {
                if (stack.StackTypeId == ent.Comp.DigestStack)
                    continue;

                amount = stack.Count;
                QueueDel(eaten);
            }
            else if (HasComp<MobStateComponent>(eaten))
            {
                if (_mobState.IsDead(eaten))
                    continue;

                amount = ent.Comp.FlatDigestAmount;
                var hits = _random.Next(3, 6);
                for (var i = 0; i < hits; i++)
                {
                    _damageable.TryChangeDamage(eaten, ent.Comp.DigestDamage, origin: ent);
                }

                if (ent.Comp.DigestPopup is { } digest)
                    _popup.PopupEntity(Loc.GetString(digest), eaten, eaten, PopupType.MediumCaution);
            }
            else
            {
                amount = TryComp<ItemComponent>(eaten, out var item)
                    ? _item.GetItemSizeWeight(item.Size)
                    : ent.Comp.FlatDigestAmount;
                QueueDel(eaten);
            }

            amount = AddToPile(ent, amount);
            if (amount <= 0)
                continue;

            var sheets = _stack.Spawn(amount, ent.Comp.DigestStack, coords);
            _container.Insert(sheets, ent.Comp.Stomach);
        }
    }

    /// <summary>
    /// Adds digested sheets to a pile already in the stomach and returns what did not fit.
    /// </summary>
    private int AddToPile(Entity<FaunaWormComponent> ent, int amount)
    {
        foreach (var held in ent.Comp.Stomach.ContainedEntities)
        {
            if (amount <= 0)
                break;

            if (!TryComp<StackComponent>(held, out var pile) || pile.StackTypeId != ent.Comp.DigestStack)
                continue;

            var added = Math.Min(amount, _stack.GetAvailableSpace(pile));
            if (added <= 0)
                continue;

            _stack.SetCount(held, pile.Count + added, pile);
            amount -= added;
        }

        return amount;
    }

    private string GetState(EntityUid uid)
    {
        return _appearance.TryGetData<string>(uid, FaunaWormVisuals.State, out var state) ? state : BodyState;
    }

    private static string AddDead(string state)
    {
        return state.EndsWith("_dead") ? state : state + "_dead";
    }

    private void SetState(EntityUid uid, string state)
    {
        _appearance.SetData(uid, FaunaWormVisuals.State, state);
    }
}
