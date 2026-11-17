using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Weapons.Ranged.Components;
using Content.Shared._WG.Tether;
using Content.Shared._WG.Tether.Harpoon;
using Content.Shared.Actions;
using Content.Shared.Examine;
using Content.Shared.Verbs;
using Content.Shared.Interaction;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Server._WG.Tether.Harpoon;

/// <summary>
/// The server half of the harpoon turret: the cable that pays out behind a shot harpoon, whether the harpoon bit
/// or glanced off, the tow cable it leaves behind, and the winch the operator works it with.
/// </summary>
public sealed partial class ShipHarpoonTurretSystem : SharedShipHarpoonTurretSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private RopeSystem _rope = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedProjectileSystem _projectiles = default!;
    [Dependency] private SharedToolSystem _tools = default!;

    private const string ReelInAction = "WGActionHarpoonReelIn";
    private const string PayOutAction = "WGActionHarpoonPayOut";
    private const string ReleaseAction = "WGActionHarpoonRelease";
    private const string PryingQuality = "Prying";

    /// <summary>Loose bodies lighter than this are not worth sinking a harpoon into.</summary>
    private const float MinAnchorMass = 50f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipHarpoonTurretComponent, MoveEvent>(OnTurretMove);
        SubscribeLocalEvent<ShipHarpoonTurretComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<ShipHarpoonTurretComponent, RopeDetachedEvent>(OnRopeDetached);
        SubscribeLocalEvent<ShipHarpoonTurretComponent, RopeCoilTargetAttemptEvent>(OnTurretCoilAttempt);
        SubscribeLocalEvent<ShipHarpoonTurretComponent, ExaminedEvent>(OnTurretExamined);

        // Runs ahead of the projectile code so a glancing hit can drop the embed before it happens.
        SubscribeLocalEvent<ShipHarpoonComponent, StartCollideEvent>(OnHarpoonCollide,
            before: new[] { typeof(SharedProjectileSystem) });
        SubscribeLocalEvent<ShipHarpoonComponent, PreventCollideEvent>(OnHarpoonPreventCollide);
        // The projectile code takes all friction off anything that can be shot; give a loose harpoon its own back.
        SubscribeLocalEvent<ShipHarpoonComponent, TileFrictionEvent>(OnHarpoonFriction,
            after: new[] { typeof(SharedProjectileSystem) });
        SubscribeLocalEvent<ShipHarpoonComponent, EmbedEvent>(OnHarpoonEmbed);
        SubscribeLocalEvent<ShipHarpoonComponent, InteractUsingEvent>(OnHarpoonInteractUsing);
        SubscribeLocalEvent<ShipHarpoonComponent, HarpoonPryDoAfterEvent>(OnHarpoonPried);
        SubscribeLocalEvent<ShipHarpoonComponent, ExaminedEvent>(OnHarpoonExamined);
        SubscribeLocalEvent<ShipHarpoonComponent, GetVerbsEvent<AlternativeVerb>>(OnHarpoonAltVerbs);

        SubscribeLocalEvent<MannedTurretOperatorComponent, HarpoonReelInActionEvent>(OnReelIn);
        SubscribeLocalEvent<MannedTurretOperatorComponent, HarpoonPayOutActionEvent>(OnPayOut);
        SubscribeLocalEvent<MannedTurretOperatorComponent, HarpoonReleaseActionEvent>(OnRelease);
    }

    #region Controls

    protected override void GrantControls(EntityUid turret, EntityUid user, MannedTurretOperatorComponent component)
    {
        _actions.AddAction(user, ref component.ReelInAction, ReelInAction);
        _actions.AddAction(user, ref component.PayOutAction, PayOutAction);
        _actions.AddAction(user, ref component.ReleaseAction, ReleaseAction);
        _popup.PopupEntity(Loc.GetString("wg-harpoon-turret-manned"), turret, user);
    }

    protected override void RefuseUnpowered(EntityUid turret, EntityUid user)
    {
        _popup.PopupEntity(Loc.GetString("wg-harpoon-turret-unpowered"), turret, user);
    }

    /// <summary>The tow cable comes with the drum; a hand coil tied here would only block the turret from firing.</summary>
    private void OnTurretCoilAttempt(Entity<ShipHarpoonTurretComponent> turret, ref RopeCoilTargetAttemptEvent args)
    {
        if (args.AttachPoint != turret.Owner)
            return;

        args.AttachPoint = null;
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("wg-harpoon-turret-coil-refused"), turret, args.User);
    }

    private void OnTurretExamined(Entity<ShipHarpoonTurretComponent> turret, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !turret.Comp.Manned)
            return;

        args.PushMarkup(Loc.GetString(IsPowered(turret) ? "wg-harpoon-turret-examine-powered" : "wg-harpoon-turret-examine-unpowered"));
        args.PushMarkup(Loc.GetString("wg-harpoon-turret-examine-cable"));
        args.PushMarkup(Loc.GetString("wg-harpoon-turret-examine-fire"));
        args.PushMarkup(Loc.GetString("wg-harpoon-examine-shields"));
    }

    protected override void RevokeControls(EntityUid user, MannedTurretOperatorComponent component)
    {
        _actions.RemoveAction(user, component.ReelInAction);
        _actions.RemoveAction(user, component.PayOutAction);
        _actions.RemoveAction(user, component.ReleaseAction);
        component.ReelInAction = null;
        component.PayOutAction = null;
        component.ReleaseAction = null;

        // Operator is already cleared by the time controls are revoked, so resolve the turret directly.
        if (TryGetEntity(component.Turret, out var turret) && TryComp<ShipHarpoonTurretComponent>(turret, out var comp))
            SetReeling((turret.Value, comp), 0);
    }

    private void OnReelIn(EntityUid uid, MannedTurretOperatorComponent component, HarpoonReelInActionEvent args)
    {
        args.Handled = Toggle(uid, component, 1);
    }

    private void OnPayOut(EntityUid uid, MannedTurretOperatorComponent component, HarpoonPayOutActionEvent args)
    {
        args.Handled = Toggle(uid, component, -1);
    }

    private void OnRelease(EntityUid uid, MannedTurretOperatorComponent component, HarpoonReleaseActionEvent args)
    {
        if (!TryGetTurret(uid, component, out var turret))
            return;

        args.Handled = true;
        if (turret.Comp.Rope is not { } rope)
        {
            _popup.PopupEntity(Loc.GetString("wg-harpoon-turret-no-cable"), turret.Owner, uid);
            return;
        }

        SetReeling(turret, 0);
        _audio.PlayPvs(turret.Comp.ReleaseSound, turret.Owner);
        _rope.BreakRope(rope);
    }

    /// <summary>Starts or stops the winch. Pressing the same control twice stops it.</summary>
    private bool Toggle(EntityUid user, MannedTurretOperatorComponent component, int direction)
    {
        if (!TryGetTurret(user, component, out var turret))
            return false;

        if (turret.Comp.Rope == null)
        {
            _popup.PopupEntity(Loc.GetString("wg-harpoon-turret-no-cable"), turret.Owner, user);
            return true;
        }

        SetReeling(turret, turret.Comp.Reeling == direction ? 0 : direction);
        return true;
    }

    private void SetReeling(Entity<ShipHarpoonTurretComponent> turret, int direction)
    {
        if (turret.Comp.Reeling == direction)
            return;

        turret.Comp.Reeling = direction;
        if (direction == 0)
        {
            turret.Comp.ReelStream = _audio.Stop(turret.Comp.ReelStream);
            return;
        }

        turret.Comp.ReelStream ??= _audio
            .PlayPvs(turret.Comp.ReelSound, turret.Owner, AudioParams.Default.WithLoop(true))?.Entity;
    }

    private bool TryGetTurret(EntityUid user, MannedTurretOperatorComponent component, out Entity<ShipHarpoonTurretComponent> turret)
    {
        turret = default;
        if (!TryGetEntity(component.Turret, out var uid) || !TryComp<ShipHarpoonTurretComponent>(uid, out var comp) ||
            comp.Operator != GetNetEntity(user))
            return false;

        turret = (uid.Value, comp);
        return true;
    }

    /// <summary>
    /// Turning an unmanned turret turns its mount with it: unpacking, building or rotating it all set the rotation
    /// after map init. A manned turret's rotation is only its aim.
    /// </summary>
    private void OnTurretMove(Entity<ShipHarpoonTurretComponent> turret, ref MoveEvent args)
    {
        if (turret.Comp.Operator != null || args.NewRotation.EqualsApprox(turret.Comp.MountRotation))
            return;

        turret.Comp.MountRotation = args.NewRotation;
        Dirty(turret);
    }

    #endregion

    #region Firing

    /// <summary>A fired harpoon belongs to the turret until it is cut loose, and trails a rope while it flies.</summary>
    private void OnGunShot(Entity<ShipHarpoonTurretComponent> turret, ref GunShotEvent args)
    {
        foreach (var (uid, _) in args.Ammo)
        {
            if (uid is not { } harpoon || !TryComp<ShipHarpoonComponent>(harpoon, out var comp))
                continue;

            ClearHarpoon(turret);
            Rearm(harpoon);
            Launch(harpoon);
            if (!turret.Comp.Manned)
                EnsureComp<ProjectileGridPhaseComponent>(harpoon).SourceGrid = Transform(turret).GridUid;

            if (comp.Expendable)
                EnsureComp<TimedDespawnComponent>(harpoon).Lifetime = (float) comp.FlightTime.TotalSeconds;

            comp.Turret = GetNetEntity(turret);
            Dirty(harpoon, comp);
            turret.Comp.Harpoon = GetNetEntity(harpoon);
            Dirty(turret);
            StartFlightRope(turret, harpoon);
        }
    }

    /// <summary>
    /// The operator is buckled to the turret, so a round spawned at their feet is born parented to the turret and
    /// would fly in the mount's frame. Put it on the hull it was fired from, keeping the speed it left with.
    /// </summary>
    private void Launch(EntityUid harpoon)
    {
        var xform = Transform(harpoon);
        if (xform.ParentUid == xform.GridUid || xform.ParentUid == xform.MapUid)
            return;

        var velocity = _physics.GetMapLinearVelocity(harpoon);
        Transforms.AttachToGridOrMap(harpoon, xform);
        _physics.SetLinearVelocity(harpoon, velocity - _physics.GetMapLinearVelocity(Transform(harpoon).ParentUid));
    }

    /// <summary>
    /// A visual-only rope, built the way stage 1 builds a carried end: no joint, no forces, just something to
    /// watch pay out behind the harpoon.
    /// </summary>
    private void StartFlightRope(Entity<ShipHarpoonTurretComponent> turret, EntityUid harpoon)
    {
        var uid = Spawn(null, Transform(turret).Coordinates);
        var rope = EnsureComp<RopeComponent>(uid);
        rope.EndA = GetNetEntity(turret);
        rope.EndB = GetNetEntity(harpoon);
        rope.RopeType = turret.Comp.RopeType;
        rope.Length = 1f;
        rope.Carried = true;
        Dirty(uid, rope);
        _pvs.AddGlobalOverride(uid);
        turret.Comp.FlightRope = uid;
    }

    /// <summary>Drops whatever the turret still holds: its flight rope, its cable and its harpoon.</summary>
    private void ClearHarpoon(Entity<ShipHarpoonTurretComponent> turret)
    {
        SetReeling(turret, 0);
        if (turret.Comp.FlightRope is { } flight && !TerminatingOrDeleted(flight))
            Del(flight);

        turret.Comp.FlightRope = null;
        if (turret.Comp.Rope is { } rope && !TerminatingOrDeleted(rope))
            _rope.BreakRope(rope);

        turret.Comp.Rope = null;
        if (TryGetEntity(turret.Comp.Harpoon, out var harpoon) && TryComp<ShipHarpoonComponent>(harpoon, out var comp))
        {
            comp.Turret = null;
            Dirty(harpoon.Value, comp);
        }

        turret.Comp.Harpoon = null;
        if (!TerminatingOrDeleted(turret))
            Dirty(turret);
    }

    /// <summary>The cable being cut, snapped or untied at either end leaves the turret free to fire again.</summary>
    private void OnRopeDetached(Entity<ShipHarpoonTurretComponent> turret, ref RopeDetachedEvent args)
    {
        if (turret.Comp.Rope != args.Rope)
            return;

        SetReeling(turret, 0);
        turret.Comp.Rope = null;
        turret.Comp.Harpoon = null;
        Dirty(turret);
    }

    #endregion

    #region Impact

    /// <summary>Fired and still able to bite: not loose, not spent, not sunk into anything.</summary>
    private bool InFlight(Entity<ShipHarpoonComponent> harpoon)
    {
        return !harpoon.Comp.Embedded && TryComp<ProjectileComponent>(harpoon, out var projectile) &&
               projectile.Weapon != null && !projectile.ProjectileSpent;
    }

    /// <summary>
    /// The point only strikes in flight; loose or loaded, a harpoon touches nothing with it.
    /// In flight it is wider than its point, so beside a seam it also brushes the neighbour of whatever it is
    /// about to strike. Only what its flight path runs into is hit: a brush would be judged against a face it
    /// never flew at, and a square-on shot would skip off the hidden side of the next wall along.
    /// </summary>
    private void OnHarpoonPreventCollide(Entity<ShipHarpoonComponent> harpoon, ref PreventCollideEvent args)
    {
        // The hard fixture is the item's own body and collides like any other item's.
        if (args.Cancelled || args.OurFixture.Hard)
            return;

        if (!InFlight(harpoon))
        {
            args.Cancelled = true;
            return;
        }

        // A shield has no faces to fly in through; it decides for itself what it stops.
        if (!args.OtherFixture.Hard || HasComp<ShipShieldComponent>(args.OtherEntity))
            return;

        var velocity = _physics.GetMapLinearVelocity(harpoon) - _physics.GetMapLinearVelocity(args.OtherEntity);
        var speed = velocity.Length();
        if (speed < harpoon.Comp.MinEmbedSpeed)
            return;

        if (EntryNormal(harpoon, args.OtherEntity, velocity / speed) == Vector2.Zero)
            args.Cancelled = true;
    }

    /// <summary>
    /// Decides whether the harpoon was shot well: fast enough, and square enough on to the surface. A glancing
    /// hit loses the embed before the projectile code gets to it, so the harpoon simply drops.
    /// </summary>
    private void OnHarpoonCollide(Entity<ShipHarpoonComponent> harpoon, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != SharedProjectileSystem.ProjectileFixture || !args.OtherFixture.Hard ||
            harpoon.Comp.Embedded || !TryComp<ProjectileComponent>(harpoon, out var projectile) ||
            projectile.Weapon == null || projectile.ProjectileSpent)
            return;

        // Nothing bites into a shield. One that lets this harpoon touch it at all stops it.
        if (HasComp<ShipShieldComponent>(args.OtherEntity))
        {
            Glance(harpoon);
            return;
        }

        var velocity = _physics.GetMapLinearVelocity(harpoon) - _physics.GetMapLinearVelocity(args.OtherEntity);
        var speed = velocity.Length();
        if (speed < harpoon.Comp.MinEmbedSpeed)
        {
            // Out of flight, it is a loose item again and hurts nothing it bumps into.
            Disarm(harpoon, projectile);
            Glance(harpoon);
            return;
        }

        if (!HasComp<EmbeddableProjectileComponent>(harpoon) || (CanHold(args.OtherEntity) &&
            Incidence(harpoon, args.OtherEntity, velocity / speed) <= harpoon.Comp.MaxIncidence.Theta))
            return;

        Glance(harpoon);
    }

    /// <summary>Clears the shot, so the projectile code treats the harpoon as never fired.</summary>
    private void Disarm(EntityUid harpoon, ProjectileComponent projectile)
    {
        projectile.Shooter = null;
        projectile.Weapon = null;
        Dirty(harpoon, projectile);
    }

    /// <summary>Readies a recovered harpoon to hit and bite again after a glance or an earlier hit.</summary>
    private void Rearm(EntityUid harpoon)
    {
        if (TryComp<ProjectileComponent>(harpoon, out var projectile) && projectile.ProjectileSpent)
        {
            projectile.ProjectileSpent = false;
            Dirty(harpoon, projectile);
        }

        if (HasComp<EmbeddableProjectileComponent>(harpoon) || Prototype(harpoon) is not { } proto ||
            !proto.Components.TryGetValue(Factory.GetComponentName<EmbeddableProjectileComponent>(), out var embed))
            return;

        EntityManager.AddComponent(harpoon, embed);
    }

    /// <summary>Angle between the flight path and the struck surface's normal, in radians.</summary>
    private double Incidence(EntityUid harpoon, EntityUid target, Vector2 direction)
    {
        var normal = EntryNormal(harpoon, target, direction);
        if (normal.LengthSquared() < 0.0001f)
            return Math.PI;

        return Math.Acos(Math.Clamp(Vector2.Dot(-direction, normal), -1f, 1f));
    }

    /// <summary>
    /// The face the harpoon flew in through, as a slab test of its flight path against the struck entity's bounds,
    /// or zero if the path misses them.
    /// The contact's own normal is no use here: a fast projectile is teleported into what it hit before the contact
    /// is generated, so the manifold points along the deepest overlap rather than out of the surface.
    /// </summary>
    private Vector2 EntryNormal(EntityUid harpoon, EntityUid target, Vector2 direction)
    {
        // Work in the target's own frame, so a rotated hull's faces are its real faces and not a world box.
        var (_, targetRot, worldMatrix, invMatrix) = Transforms.GetWorldPositionRotationMatrixWithInv(target);
        var box = _lookup.GetAABBNoContainer(target, Vector2.Zero, Angle.Zero);
        var localDir = (-targetRot).RotateVec(direction);
        // Start well outside the bounds, so the slab test reads the entry face and not the overlap.
        var origin = Vector2.Transform(Transforms.GetWorldPosition(harpoon), invMatrix) - localDir * (box.Width + box.Height + 2f);
        var entry = float.NegativeInfinity;
        var exit = float.PositiveInfinity;
        var normal = Vector2.Zero;
        Axis(localDir.X, origin.X, box.Left, box.Right, new Vector2(-1f, 0f), new Vector2(1f, 0f));
        Axis(localDir.Y, origin.Y, box.Bottom, box.Top, new Vector2(0f, -1f), new Vector2(0f, 1f));
        return entry > exit ? Vector2.Zero : targetRot.RotateVec(normal);

        // The last axis to be entered is the one whose face was struck.
        void Axis(float d, float o, float min, float max, Vector2 low, Vector2 high)
        {
            if (MathF.Abs(d) < 0.0001f)
            {
                // Flying along this axis: the path is either between its two faces all the way, or never.
                if (o < min || o > max)
                    exit = float.NegativeInfinity;

                return;
            }

            exit = MathF.Min(exit, ((d > 0f ? max : min) - o) / d);
            var near = ((d > 0f ? min : max) - o) / d;
            if (near <= entry)
                return;

            entry = near;
            normal = d > 0f ? low : high;
        }
    }

    /// <summary>Hulls, walls and anything heavy enough to anchor a tow cable.</summary>
    private bool CanHold(EntityUid target)
    {
        if (HasComp<MapGridComponent>(target) || Transform(target).Anchored)
            return true;

        return TryComp<PhysicsComponent>(target, out var physics) && physics.Mass >= MinAnchorMass;
    }

    private void OnHarpoonFriction(Entity<ShipHarpoonComponent> harpoon, ref TileFrictionEvent args)
    {
        if (!InFlight(harpoon))
            args.Modifier = 1f;
    }

    /// <summary>A poor shot skips off, drops its cable and lies where it lands.</summary>
    private void Glance(Entity<ShipHarpoonComponent> harpoon)
    {
        RemComp<EmbeddableProjectileComponent>(harpoon);
        // Back on the deck as far as friction goes; a shot leaves it marked as airborne for good.
        _physics.SetBodyStatus(harpoon, Comp<PhysicsComponent>(harpoon), BodyStatus.OnGround);
        _audio.PlayPvs(harpoon.Comp.GlanceSound, harpoon);

        if (TryGetEntity(harpoon.Comp.Turret, out var turret) &&
            TryComp<ShipHarpoonTurretComponent>(turret, out var comp))
            ClearHarpoon((turret.Value, comp));

        if (harpoon.Comp.Expendable)
            QueueDel(harpoon);
    }

    /// <summary>A harpoon that bit becomes the far end of a real tow cable.</summary>
    private void OnHarpoonEmbed(Entity<ShipHarpoonComponent> harpoon, ref EmbedEvent args)
    {
        harpoon.Comp.Embedded = true;
        Dirty(harpoon);
        _appearance.SetData(harpoon, ShipHarpoonVisuals.Embedded, true);
        RemComp<TimedDespawnComponent>(harpoon);
        EnsureComp<RopeAttachPointComponent>(harpoon);

        if (!TryGetEntity(harpoon.Comp.Turret, out var turret) ||
            !TryComp<ShipHarpoonTurretComponent>(turret, out var comp))
            return;

        var ent = new Entity<ShipHarpoonTurretComponent>(turret.Value, comp);
        if (ent.Comp.FlightRope is { } flight && !TerminatingOrDeleted(flight))
            Del(flight);

        ent.Comp.FlightRope = null;
        var distance = Vector2.Distance(_rope.GetAnchorPosition(turret.Value), _rope.GetAnchorPosition(harpoon));
        if (_rope.TryCreateRope(turret.Value, harpoon, ent.Comp.RopeType, distance * ent.Comp.Slack, out var rope))
        {
            ent.Comp.Rope = rope;
            // The winch cable comes with the turret; untying it must not mint coils.
            if (rope is { } ropeUid && TryComp<RopeComponent>(ropeUid, out var ropeComp))
                ropeComp.Refundable = false;
        }

        Dirty(ent);
    }

    #endregion

    #region Removal

    private void OnHarpoonInteractUsing(EntityUid uid, ShipHarpoonComponent component, InteractUsingEvent args)
    {
        if (args.Handled || !component.Embedded)
            return;

        args.Handled = _tools.UseTool(args.Used, args.User, uid, component.PryTime,
            new[] { PryingQuality }, new HarpoonPryDoAfterEvent(), out _);
    }

    private void OnHarpoonExamined(EntityUid uid, ShipHarpoonComponent component, ExaminedEvent args)
    {
        if (component.Embedded)
        {
            args.PushMarkup(Loc.GetString("wg-harpoon-examine-embedded"));
            return;
        }

        // An expendable harpoon is a ship gun's round, and its description speaks for it.
        if (component.Expendable)
            return;

        args.PushMarkup(Loc.GetString("wg-harpoon-examine-load"));
        args.PushMarkup(Loc.GetString("wg-harpoon-examine-shields"));
    }

    /// <summary>The crowbar is the tool; the verb just makes it discoverable from the other hull.</summary>
    private void OnHarpoonAltVerbs(EntityUid uid, ShipHarpoonComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!component.Embedded || !args.CanAccess || !args.CanInteract || args.Using is not { } used ||
            !_tools.HasQuality(used, PryingQuality))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wg-harpoon-verb-pry"),
            Act = () => _tools.UseTool(used, user, uid, component.PryTime, new[] { PryingQuality },
                new HarpoonPryDoAfterEvent(), out _),
        });
    }

    private void OnHarpoonPried(EntityUid uid, ShipHarpoonComponent component, HarpoonPryDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !component.Embedded)
            return;

        args.Handled = true;
        component.Embedded = false;
        Dirty(uid, component);
        _appearance.SetData(uid, ShipHarpoonVisuals.Embedded, false);
        _projectiles.EmbedDetach(uid, null, args.User);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShipHarpoonTurretComponent>();
        while (query.MoveNext(out var uid, out var turret))
        {
            var ent = new Entity<ShipHarpoonTurretComponent>(uid, turret);
            UpdateFlight(ent);
            UpdateReel(ent, frameTime);
        }
    }

    /// <summary>Keeps the flight rope drawn along the harpoon, and cuts it once the harpoon outruns the cable.</summary>
    private void UpdateFlight(Entity<ShipHarpoonTurretComponent> turret)
    {
        if (turret.Comp.FlightRope is not { } flight)
            return;

        if (!TryComp<RopeComponent>(flight, out var rope) ||
            !TryGetEntity(turret.Comp.Harpoon, out var harpoon) || TerminatingOrDeleted(harpoon.Value))
        {
            ClearHarpoon(turret);
            return;
        }

        var distance = Vector2.Distance(
            _rope.GetAnchorPosition(turret.Owner), _rope.GetAnchorPosition(harpoon.Value));
        var max = _protos.TryIndex(turret.Comp.RopeType, out var proto) ? proto.MaxLength : 0f;
        if (max > 0f && distance > max)
        {
            ClearHarpoon(turret);
            if (TryComp<ShipHarpoonComponent>(harpoon, out var flying) && flying.Expendable)
                QueueDel(harpoon.Value);

            return;
        }

        if (MathHelper.CloseTo(rope.Length, distance, 0.05f))
            return;

        rope.Length = MathF.Max(distance, 1f);
        Dirty(flight, rope);
    }

    /// <summary>Whether the two bodies the cable joins are pressed against each other.</summary>
    private bool HullsTouch(RopeComponent rope)
    {
        if (rope.BodyA is not { } bodyA || rope.BodyB is not { } bodyB || TerminatingOrDeleted(bodyA))
            return false;

        var contacts = _physics.GetContacts(bodyA);
        while (contacts.MoveNext(out var contact))
        {
            if (contact.IsTouching && contact.FixtureA is { Hard: true } && contact.FixtureB is { Hard: true } &&
                (contact.EntityA == bodyB || contact.EntityB == bodyB))
                return true;
        }

        return false;
    }

    /// <summary>Works the winch, stalling rather than snapping the cable when the load is too much.</summary>
    private void UpdateReel(Entity<ShipHarpoonTurretComponent> turret, float frameTime)
    {
        if (turret.Comp.Reeling == 0)
            return;

        if (turret.Comp.Rope is not { } rope || !TryComp<RopeComponent>(rope, out var comp))
        {
            SetReeling(turret, 0);
            return;
        }

        if (turret.Comp.Reeling > 0 && _rope.GetTension(rope) > turret.Comp.ReelMaxTension)
            return;

        // Hull against hull there is nothing left to take in; hauling on would only grind the two together.
        if (turret.Comp.Reeling > 0 && HullsTouch(comp))
        {
            if (TryGetEntity(comp.EndA, out var endA) && TryGetEntity(comp.EndB, out var endB))
            {
                // Ease the cable to what is actually out, so it holds the hulls there instead of squeezing them.
                var paidOut = Vector2.Distance(_rope.GetAnchorPosition(endA.Value), _rope.GetAnchorPosition(endB.Value));
                _rope.SetLength(rope, MathF.Max(comp.Length, paidOut));
            }

            SetReeling(turret, 0);
            if (TryGetEntity(turret.Comp.Operator, out var user))
                _popup.PopupEntity(Loc.GetString("wg-harpoon-turret-hulls-touching"), turret, user.Value);

            return;
        }

        // Reeling in shortens the cable, paying out lengthens it.
        var length = comp.Length - turret.Comp.Reeling * turret.Comp.ReelRate * frameTime;
        if (!_rope.SetLength(rope, length))
            SetReeling(turret, 0);
    }
}
