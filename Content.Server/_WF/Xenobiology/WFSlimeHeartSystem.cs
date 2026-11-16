using Content.Server.Destructible;
using Content.Shared._WF.Xenobiology;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenobiology;

/// <summary>
/// Runs slime hearts (<see cref="WFSlimeHeartComponent"/>): one beats per hull, eats extracts of its own breed, and
/// regrows whatever is bolted to the hull (walls, windows, thrusters, guns, machines) once it has been left alone for a
/// while. Destroyed parts stay destroyed.
/// </summary>
public sealed partial class WFSlimeHeartSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tag = default!;

    private EntityQuery<DamageableComponent> _damageableQuery;
    private EntityQuery<DestructibleComponent> _destructibleQuery;
    private EntityQuery<MobStateComponent> _mobQuery;

    public override void Initialize()
    {
        base.Initialize();
        _damageableQuery = GetEntityQuery<DamageableComponent>();
        _destructibleQuery = GetEntityQuery<DestructibleComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();

        SubscribeLocalEvent<WFSlimeHeartComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFSlimeHeartComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<WFSlimeHeartComponent, UserAnchoredEvent>(OnUserAnchored);
        SubscribeLocalEvent<WFSlimeHeartComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFSlimeHeartComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFSlimeHeartComponent>();
        while (query.MoveNext(out var uid, out var heart))
        {
            if (now < heart.NextUpdate)
                continue;

            heart.NextUpdate = now + heart.Interval;
            Tick((uid, heart));
        }
    }

    /// <summary>True while the heart is bolted to a ship's hull and fed.</summary>
    public bool IsBeating(Entity<WFSlimeHeartComponent> ent)
    {
        var xform = Transform(ent);
        // A planet's ground is a grid too, and far too large to walk.
        return xform.Anchored
            && _timing.CurTime < ent.Comp.FedUntil
            && xform.GridUid is { } grid
            && !HasComp<MapComponent>(grid);
    }

    /// <summary>
    /// True when another heart beats in the same hull and goes first (the lower uid). Wrenches refuse a second heart,
    /// but two can still meet, such as when the repair device merges a hull section back; only one of them works.
    /// </summary>
    public bool IsOutbeaten(Entity<WFSlimeHeartComponent> ent)
    {
        if (Transform(ent).GridUid is not { } grid)
            return false;

        var query = EntityQueryEnumerator<WFSlimeHeartComponent, TransformComponent>();
        while (query.MoveNext(out var other, out var comp, out var xform))
        {
            if (other != ent.Owner && xform.GridUid == grid && other.Id < ent.Owner.Id && IsBeating((other, comp)))
                return true;
        }

        return false;
    }

    /// <summary>One beat: regrows part of every damaged part bolted to the hull that has been left alone.</summary>
    public void Tick(Entity<WFSlimeHeartComponent> ent)
    {
        var beating = IsBeating(ent) && !IsOutbeaten(ent);
        var old = ent.Comp.Wounds;
        if (!beating)
        {
            old.Clear();
            SetState(ent, WFSlimeHeartState.Dormant);
            return;
        }

        var mended = new List<EntityCoordinates>();
        var now = _timing.CurTime;
        var wounds = new Dictionary<EntityUid, (FixedPoint2 Damage, TimeSpan Since)>();
        var children = Transform(Transform(ent).GridUid!.Value).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            // Only what is bolted to the hull; never crew or another heart.
            if (!_damageableQuery.TryComp(child, out var damageable)
                || !_destructibleQuery.TryComp(child, out var destructible)
                || !Transform(child).Anchored
                || _mobQuery.HasComp(child)
                || HasComp<WFSlimeHeartComponent>(child))
                continue;

            var total = damageable.TotalDamage;
            if (total <= FixedPoint2.Zero)
                continue;

            // New damage, or more of it, restarts the wait.
            if (!old.TryGetValue(child, out var wound) || total > wound.Damage)
            {
                wounds[child] = (total, now);
                continue;
            }

            if (now - wound.Since >= ent.Comp.QuietDelay)
            {
                var left = Regrow(ent, (child, damageable, destructible), total);
                if (left < total)
                    mended.Add(Transform(child).Coordinates);

                total = left;
            }

            if (total > FixedPoint2.Zero)
                wounds[child] = (total, wound.Since);
        }

        ent.Comp.Wounds = wounds;
        SetState(ent, mended.Count > 0 ? WFSlimeHeartState.Mending : WFSlimeHeartState.Beating);

        // Spawned after the walk, since an effect on the grid joins the children being walked.
        if (ent.Comp.RegrowEffect is { } effect)
        {
            foreach (var coords in mended)
            {
                Spawn(effect, coords);
            }
        }
    }

    private void SetState(Entity<WFSlimeHeartComponent> ent, WFSlimeHeartState state)
    {
        _light.SetEnabled(ent, state != WFSlimeHeartState.Dormant);
        _light.SetEnergy(ent, state == WFSlimeHeartState.Mending ? ent.Comp.MendingGlow : ent.Comp.Glow);
        _appearance.SetData(ent, WFSlimeHeartVisuals.State, state);
    }

    /// <summary>Heals one beat's worth of a hull part, spread over its damage types. Returns the damage left.</summary>
    private FixedPoint2 Regrow(Entity<WFSlimeHeartComponent> ent,
        Entity<DamageableComponent, DestructibleComponent> part,
        FixedPoint2 total)
    {
        var max = _destructible.DestroyedAt(part, part.Comp2);
        if (max == FixedPoint2.MaxValue || max <= FixedPoint2.Zero)
            return total;

        var heal = FixedPoint2.Min(total, max * ent.Comp.RegrowFraction);
        _damageable.TryChangeDamage(part, part.Comp1.Damage * -(heal.Float() / total.Float()), true, false);
        return part.Comp1.TotalDamage;
    }

    /// <summary>Feeds an unspent extract of the heart's breed to it. The extract is eaten.</summary>
    public bool TryFeed(Entity<WFSlimeHeartComponent> ent, EntityUid extract, EntityUid? user = null)
    {
        if (!_tag.HasTag(extract, ent.Comp.Food))
            return false;

        if (TryComp<SlimeExtractComponent>(extract, out var comp) && comp.Spent)
        {
            Popup("wf-slime-heart-feed-spent", ent, user);
            return false;
        }

        var now = _timing.CurTime;
        var left = ent.Comp.FedUntil > now ? ent.Comp.FedUntil - now : TimeSpan.Zero;
        if (left + ent.Comp.FeedPerExtract > ent.Comp.MaxFeed)
        {
            Popup("wf-slime-heart-feed-full", ent, user);
            return false;
        }

        ent.Comp.FedUntil = now + left + ent.Comp.FeedPerExtract;
        QueueDel(extract);
        _audio.PlayPvs(ent.Comp.FeedSound, ent);
        Popup("wf-slime-heart-feed", ent, user);
        return true;
    }

    private void OnMapInit(Entity<WFSlimeHeartComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.FedUntil = _timing.CurTime + ent.Comp.StartingFeed;
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.Interval;
    }

    private void OnAnchorAttempt(Entity<WFSlimeHeartComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled || !HasBoltedNeighbour(ent))
            return;

        _popup.PopupEntity(Loc.GetString("wf-slime-heart-anchor-second"), ent, args.User);
        args.Cancel();
    }

    private void OnUserAnchored(Entity<WFSlimeHeartComponent> ent, ref UserAnchoredEvent args)
    {
        // The attempt is checked when the wrench starts, so two hearts wrenched at once would both pass it.
        if (!HasBoltedNeighbour(ent))
            return;

        _transform.Unanchor(ent, Transform(ent));
        _popup.PopupEntity(Loc.GetString("wf-slime-heart-anchor-second"), ent, args.User);
    }

    /// <summary>True when another heart is bolted down on the hull this one stands on.</summary>
    private bool HasBoltedNeighbour(Entity<WFSlimeHeartComponent> ent)
    {
        if (Transform(ent).GridUid is not { } grid)
            return false;

        var query = EntityQueryEnumerator<WFSlimeHeartComponent, TransformComponent>();
        while (query.MoveNext(out var other, out _, out var xform))
        {
            if (other != ent.Owner && xform.Anchored && xform.GridUid == grid)
                return true;
        }

        return false;
    }

    private void OnInteractUsing(Entity<WFSlimeHeartComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tag.HasTag(args.Used, ent.Comp.Food))
            return;

        args.Handled = true;
        TryFeed(ent, args.Used, args.User);
    }

    private void OnExamined(Entity<WFSlimeHeartComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var left = ent.Comp.FedUntil - _timing.CurTime;
        if (left <= TimeSpan.Zero)
            args.PushMarkup(Loc.GetString("wf-slime-heart-examine-dormant"));
        else if (!Transform(ent).Anchored)
            args.PushMarkup(Loc.GetString("wf-slime-heart-examine-loose", ("minutes", Minutes(left))));
        else if (IsOutbeaten(ent))
            args.PushMarkup(Loc.GetString("wf-slime-heart-examine-outbeaten"));
        else
            args.PushMarkup(Loc.GetString("wf-slime-heart-examine-beating", ("minutes", Minutes(left))));
    }

    private static int Minutes(TimeSpan time)
    {
        return Math.Max(1, (int) Math.Ceiling(time.TotalMinutes));
    }

    private void Popup(string key, EntityUid heart, EntityUid? user)
    {
        if (user is { } recipient)
            _popup.PopupEntity(Loc.GetString(key), heart, recipient);
    }
}
