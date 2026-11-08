using Content.Server._WF.Husbandry.Components;
using Content.Server._WF.Planets;
using Content.Shared._WF.Husbandry;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._WF.Husbandry;

/// <summary>Lassos: an animal roped to whoever threw it, led on a few metres of rope.</summary>
public sealed partial class WFHusbandrySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedJointSystem _joints = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public const string LassoJoint = "wf-lasso";

    /// <summary>A lasso's rope gives way once its ends are this many times its length apart.</summary>
    private const float LassoSnap = 3f;

    private void InitializeLasso()
    {
        SubscribeLocalEvent<WFLassoComponent, AfterInteractEvent>(OnLassoAfterInteract);
        SubscribeLocalEvent<WFLassoComponent, WFLassoDoAfterEvent>(OnLassoDoAfter);
        SubscribeLocalEvent<WFLassoComponent, GotUnequippedHandEvent>(OnLassoDropped);
        SubscribeLocalEvent<WFLassoComponent, ComponentShutdown>(OnLassoShutdown);
        SubscribeLocalEvent<WFLassoedComponent, MobStateChangedEvent>(OnLassoedStateChanged);
        SubscribeLocalEvent<WFLassoedComponent, ComponentShutdown>(OnLassoedShutdown);
    }

    private void UpdateLassos()
    {
        var query = EntityQueryEnumerator<WFLassoedComponent, TransformComponent>();
        var loose = new List<EntityUid>();
        while (query.MoveNext(out var uid, out var lassoed, out var xform))
        {
            if (!TryComp<WFLassoComponent>(lassoed.Lasso, out var lasso) || TerminatingOrDeleted(lassoed.Holder)
                || !_transform.InRange(xform.Coordinates, Transform(lassoed.Holder).Coordinates, lasso.Length * LassoSnap))
                loose.Add(lassoed.Lasso);
        }

        foreach (var lasso in loose)
        {
            if (TryComp<WFLassoComponent>(lasso, out var comp))
                Release((lasso, comp));
        }
    }

    /// <summary>Whether an animal can be lassoed: a living mob nobody plays, not on a rope already.</summary>
    public bool CanLasso(EntityUid target)
    {
        return TryComp<MobStateComponent>(target, out var state) && _mobState.IsAlive(target, state)
               && !HasComp<ActorComponent>(target) && !HasComp<WFLassoedComponent>(target);
    }

    private void OnLassoAfterInteract(Entity<WFLassoComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target)
            return;

        if (ent.Comp.Roped == target)
        {
            Release(ent);
            _popup.PopupEntity(Loc.GetString("wf-lasso-released", ("animal", target)), args.User, args.User);
            args.Handled = true;
            return;
        }

        if (!CanLasso(target))
            return;

        args.Handled = TryStartLasso(args.User, ent, target);
    }

    /// <summary>Starts throwing a loop of the lasso over an animal in range.</summary>
    public bool TryStartLasso(EntityUid user, Entity<WFLassoComponent> lasso, EntityUid target)
    {
        if (!CanLasso(target) || !_transform.InRange(Transform(user).Coordinates, Transform(target).Coordinates, lasso.Comp.Range))
            return false;

        var args = new DoAfterArgs(EntityManager, user, lasso.Comp.Delay, new WFLassoDoAfterEvent(), lasso, target: target, used: lasso)
        {
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = lasso.Comp.Range + 1f,
        };

        if (!_doAfter.TryStartDoAfter(args))
            return false;

        _popup.PopupEntity(Loc.GetString("wf-lasso-throw", ("user", user), ("animal", target)), target, PopupType.Small);
        return true;
    }

    private void OnLassoDoAfter(Entity<WFLassoComponent> ent, ref WFLassoDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || !CanLasso(target))
            return;

        args.Handled = true;
        if (ent.Comp.Looped != target)
        {
            ent.Comp.Looped = target;
            ent.Comp.Loops = 0;
        }

        ent.Comp.Loops++;
        var needed = IsBig(target, ent.Comp) ? 2 : 1;
        if (ent.Comp.Loops < needed)
        {
            _popup.PopupEntity(Loc.GetString("wf-lasso-one-more", ("animal", target)), args.User, args.User);
            return;
        }

        ent.Comp.Looped = null;
        ent.Comp.Loops = 0;
        Tie(args.User, ent, target);
        _popup.PopupEntity(Loc.GetString("wf-lasso-tied", ("animal", target)), args.User, args.User);
    }

    /// <summary>Whether an animal is heavy enough to take two loops.</summary>
    public bool IsBig(EntityUid target, WFLassoComponent lasso)
    {
        return TryComp<PhysicsComponent>(target, out var physics) && physics.FixturesMass >= lasso.BigMass;
    }

    private void Tie(EntityUid holder, Entity<WFLassoComponent> lasso, EntityUid animal)
    {
        Release(lasso);
        var joint = _joints.CreateDistanceJoint(animal, holder, id: LassoJoint);
        joint.CollideConnected = false;
        joint.MinLength = 0f;
        joint.MaxLength = MathF.Max(lasso.Comp.Length, joint.Length + 0.15f);
        joint.Stiffness = 0f;

        var lassoed = EnsureComp<WFLassoedComponent>(animal);
        lassoed.Holder = holder;
        lassoed.Lasso = lasso;
        lasso.Comp.Roped = animal;

        var visuals = EnsureComp<JointVisualsComponent>(animal);
        visuals.Sprite = lasso.Comp.RopeSprite;
        visuals.Target = GetNetEntity(holder);
        Dirty(animal, visuals);

        if (TryComp<WFPlanetWildlifeComponent>(animal, out var wildlife))
            wildlife.Protected = true;
    }

    /// <summary>Lets go of whatever a lasso holds.</summary>
    public void Release(Entity<WFLassoComponent> lasso)
    {
        if (lasso.Comp.Roped is not { } animal)
            return;

        lasso.Comp.Roped = null;
        if (TerminatingOrDeleted(animal))
            return;

        _joints.RemoveJoint(animal, LassoJoint);
        RemComp<JointVisualsComponent>(animal);
        RemComp<WFLassoedComponent>(animal);
    }

    private void OnLassoDropped(Entity<WFLassoComponent> ent, ref GotUnequippedHandEvent args)
    {
        Release(ent);
    }

    private void OnLassoShutdown(Entity<WFLassoComponent> ent, ref ComponentShutdown args)
    {
        Release(ent);
    }

    private void OnLassoedStateChanged(Entity<WFLassoedComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive && TryComp<WFLassoComponent>(ent.Comp.Lasso, out var lasso))
            Release((ent.Comp.Lasso, lasso));
    }

    private void OnLassoedShutdown(Entity<WFLassoedComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<WFLassoComponent>(ent.Comp.Lasso, out var lasso) && lasso.Roped == ent.Owner)
            lasso.Roped = null;
    }
}
