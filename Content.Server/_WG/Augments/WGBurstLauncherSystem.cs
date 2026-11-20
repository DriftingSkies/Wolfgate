using Content.Server.Administration.Logs;
using Content.Shared._WG.Augments;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Server._WG.Augments;

/// <summary>
/// Gives a body with a halo launcher its action, throws the ring of orbs and holds the wearer in its shield until they
/// go off, blasts round each orb, and brings spent bursts back one at a time.
/// </summary>
public sealed partial class WGBurstLauncherSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<Entity<PhysicsComponent>> _caught = new();

    public override void Initialize()
    {
        base.Initialize();
        Subscribe<WGHaloBurstComponent>();
        Subscribe<WGHaloStunComponent>();
    }

    private void Subscribe<T>() where T : WGBurstLauncherComponent
    {
        SubscribeLocalEvent<T, ComponentInit>((uid, comp, _) =>
            _actions.AddAction(uid, ref comp.ActionEntity, comp.Action));
        SubscribeLocalEvent<T, ComponentShutdown>((_, comp, _) =>
        {
            QueueDel(comp.ActionEntity);
            comp.ActionEntity = null;
        });
        SubscribeLocalEvent<T, WGFireBurstLauncherEvent>((uid, comp, args) => OnFire((uid, comp), args));
    }

    private void OnFire(Entity<WGBurstLauncherComponent> ent, WGFireBurstLauncherEvent args)
    {
        // Both models answer the same event; each fires only for its own action, and only one ring is out at a time.
        if (args.Handled || args.Action.Owner != ent.Comp.ActionEntity || HasComp<WGHaloArmingComponent>(ent))
            return;

        args.Handled = true;
        Launch(ent);
    }

    /// <summary>Throws the ring of orbs out and holds the body in its shield until they go off.</summary>
    public void Launch(Entity<WGBurstLauncherComponent> ent)
    {
        var detonateAt = _timing.CurTime + ent.Comp.Fuse;
        var arming = EnsureComp<WGHaloArmingComponent>(ent);
        arming.End = detonateAt;
        arming.Shield = SpawnAttachedTo(ent.Comp.Shield, new EntityCoordinates(ent, Vector2.Zero));
        Dirty(ent, arming);

        var origin = _transform.GetMapCoordinates(ent);
        var segment = 360f / Math.Max(ent.Comp.Orbs, 1);
        var turn = _random.NextFloat(segment);
        // Slowing evenly to a stop covers half the distance the launch speed would.
        var speed = 2f * ent.Comp.OrbDistance / (float) ent.Comp.OrbFlight.TotalSeconds;
        for (var i = 0; i < ent.Comp.Orbs; i++)
        {
            var orb = Spawn(ent.Comp.Orb, origin);
            if (!TryComp<WGHaloOrbComponent>(orb, out var charge) || !TryComp<PhysicsComponent>(orb, out var body))
                continue;

            charge.DetonateAt = detonateAt;
            charge.Source = ent;
            charge.LaunchVelocity = Angle.FromDegrees(turn + segment * i).ToVec() * speed;
            charge.LaunchedAt = _timing.CurTime;
            charge.StopAt = _timing.CurTime + ent.Comp.OrbFlight;
            _physics.SetBodyStatus(orb, body, BodyStatus.InAir);
            _physics.SetLinearVelocity(orb, charge.LaunchVelocity, body: body);
        }

        _audio.PlayPvs(ent.Comp.ArmSound, ent);
        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(ent):user} threw a halo ({ent.Comp.Orb})");
    }

    /// <summary>
    /// Blasts every mob in reach of the orb and in its sight, except the body that threw it, and throws everything loose
    /// there outward from that body, as an explosion would.
    /// </summary>
    public void Detonate(Entity<WGHaloOrbComponent> orb)
    {
        var xform = Transform(orb);
        var position = _transform.GetMapCoordinates(orb, xform);
        Spawn(orb.Comp.Blast, position);
        _audio.PlayPvs(orb.Comp.Sound, xform.Coordinates);

        // The ring pushes out from its middle; with the thrower gone, each blast pushes out from itself.
        var centre = position.Position;
        if (orb.Comp.Source is { } source && !TerminatingOrDeleted(source))
        {
            var from = _transform.GetMapCoordinates(source);
            if (from.MapId == position.MapId)
                centre = from.Position;
        }

        _caught.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, orb.Comp.Radius, _caught);
        foreach (var hit in _caught)
        {
            if (hit.Owner == orb.Comp.Source
                || HasComp<WGHaloOrbComponent>(hit)
                || !_interaction.InRangeUnobstructed(orb.Owner, hit.Owner, orb.Comp.Radius + 0.5f))
                continue;

            if (HasComp<MobStateComponent>(hit))
            {
                if (orb.Comp.Damage is { } damage)
                    _damageable.TryChangeDamage(hit.Owner, damage, origin: orb.Comp.Source, tool: orb);

                if (orb.Comp.StaminaDamage > 0)
                    _stamina.TakeStaminaDamage(hit.Owner, orb.Comp.StaminaDamage, source: orb.Comp.Source, with: orb);
            }

            // Mobs are kinematic controllers; the throw moves them as it does loose items.
            if (orb.Comp.Knockback <= 0f
                || (hit.Comp.BodyType & (BodyType.Dynamic | BodyType.KinematicController)) == 0
                || Transform(hit).Anchored)
                continue;

            var away = _transform.GetWorldPosition(hit) - centre;
            if (away.LengthSquared() < 0.0001f)
                away = _random.NextAngle().ToVec();
            _throwing.TryThrow(hit, Vector2.Normalize(away) * orb.Comp.Knockback, orb.Comp.KnockbackSpeed,
                compensateFriction: true, playSound: false);
        }

        QueueDel(orb);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var orbs = EntityQueryEnumerator<WGHaloOrbComponent, PhysicsComponent>();
        while (orbs.MoveNext(out var uid, out var orb, out var body))
        {
            if (now >= orb.DetonateAt)
            {
                if (!TerminatingOrDeleted(uid))
                    Detonate((uid, orb));
                continue;
            }

            if (orb.StopAt is not { } stop)
                continue;

            // Floor friction rewrites damping every tick, so the orb is slowed here instead.
            var left = now >= stop ? 0f : (float) ((stop - now) / (stop - orb.LaunchedAt));
            _physics.SetLinearVelocity(uid, orb.LaunchVelocity * left, body: body);
            if (left <= 0f)
                orb.StopAt = null;
        }

        var arming = EntityQueryEnumerator<WGHaloArmingComponent>();
        while (arming.MoveNext(out var uid, out var comp))
        {
            if (now < comp.End)
                continue;

            QueueDel(comp.Shield);
            RemCompDeferred<WGHaloArmingComponent>(uid);
        }

        var burst = EntityQueryEnumerator<WGHaloBurstComponent>();
        while (burst.MoveNext(out var uid, out var comp))
        {
            Recharge((uid, comp), now);
        }

        var stun = EntityQueryEnumerator<WGHaloStunComponent>();
        while (stun.MoveNext(out var uid, out var comp))
        {
            Recharge((uid, comp), now);
        }
    }

    /// <summary>
    /// Brings one spent burst back each <see cref="WGBurstLauncherComponent.RechargeTime"/> until it is full, and shows
    /// the wait on the button: the fuse while orbs are out, then the next charge while it is empty.
    /// </summary>
    private void Recharge(Entity<WGBurstLauncherComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (!_actions.TryGetActionData(comp.ActionEntity, out var action, logError: false)
            || action.Charges is not { } charges
            || action.MaxCharges is not { } max)
            return;

        if (charges >= max)
            comp.NextCharge = null;
        else if (comp.NextCharge is not { } next)
            comp.NextCharge = now + comp.RechargeTime;
        else if (now >= next)
        {
            _actions.AddCharges(comp.ActionEntity, 1);
            // An empty action switches itself off.
            _actions.SetEnabled(comp.ActionEntity, true);
            charges++;
            comp.NextCharge = charges < max ? now + comp.RechargeTime : null;
            _popup.PopupEntity(Loc.GetString("wg-burst-launcher-recharged", ("charges", charges), ("max", max)), ent, ent);
        }

        (TimeSpan Start, TimeSpan End)? wait = null;
        if (TryComp<WGHaloArmingComponent>(ent, out var arming) && arming.End > now)
            wait = (arming.End - comp.Fuse, arming.End);
        else if (charges == 0 && comp.NextCharge is { } due)
            wait = (due - comp.RechargeTime, due);

        if (wait is { } w && action.Cooldown != w)
            _actions.SetCooldown(comp.ActionEntity, w.Start, w.End);
    }
}
