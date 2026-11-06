using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Jittering;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Fires the spreads, spirals and death rings of <see cref="FaunaProjectileBurstComponent"/> mobs.
/// </summary>
public sealed partial class FaunaProjectileBurstSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPointLightSystem _light = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaProjectileBurstComponent, FaunaProjectileBurstActionEvent>(OnAction);
        SubscribeLocalEvent<FaunaProjectileBurstComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FaunaProjectileBurstComponent, DestructionEventArgs>(OnDestruction);
        SubscribeLocalEvent<FaunaProjectileBurstComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<FaunaProjectileBurstComponent, DamageChangedEvent>(OnDamageChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaProjectileBurstComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.FireAt == null && comp.VolleyShotsLeft <= 0 && comp.SpiralShotsLeft <= 0)
                continue;

            if (_mobState.IsDead(uid))
            {
                if (comp.FireAt != null)
                    SetWindup((uid, comp), false);

                comp.FireAt = null;
                comp.SpiralShotsLeft = 0;
                comp.VolleyShotsLeft = 0;
                _appearance.SetData(uid, FaunaProjectileBurstVisuals.Firing, false);
                continue;
            }

            if (comp.VolleyShotsLeft > 0 && now >= comp.NextVolleyShot)
            {
                comp.VolleyShotsLeft--;
                comp.NextVolleyShot = now + comp.VolleyDelay;
                if (Aim((uid, comp)) is { } aim)
                {
                    var jitter = comp.VolleyJitter.Theta;
                    Shoot((uid, comp), aim + new Angle(_random.NextDouble(-jitter, jitter)));
                }
            }

            if (comp.FireAt is { } fireAt && now >= fireAt)
            {
                comp.FireAt = null;
                SetWindup((uid, comp), false);
                Fire((uid, comp));
            }

            if (comp.SpiralShotsLeft > 0 && now >= comp.NextSpiralShot)
            {
                var shot = comp.SpiralCount - comp.SpiralShotsLeft;
                Shoot((uid, comp), comp.SpiralStep * shot);
                comp.SpiralShotsLeft--;
                comp.NextSpiralShot = now + comp.SpiralDelay;
            }

            var firing = comp.VolleyShotsLeft > 0 || comp.SpiralShotsLeft > 0;
            _appearance.SetData(uid, FaunaProjectileBurstVisuals.Firing, firing);
        }
    }

    private void OnAction(Entity<FaunaProjectileBurstComponent> ent, ref FaunaProjectileBurstActionEvent args)
    {
        if (args.Handled || ent.Comp.FireAt != null || ent.Comp.SpiralShotsLeft > 0 || ent.Comp.VolleyShotsLeft > 0)
            return;

        args.Handled = true;
        ent.Comp.Target = args.Target;
        ent.Comp.ActionProjectile = args.Projectile;
        PruneShots(ent);

        if (ent.Comp.Windup <= TimeSpan.Zero)
        {
            Fire(ent);
            return;
        }

        ent.Comp.FireAt = _timing.CurTime + ent.Comp.Windup;
        SetWindup(ent, true);
        _audio.PlayPvs(ent.Comp.WindupSound, ent);
        if ((args.Popup ?? ent.Comp.WindupPopup) is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner)), ent, PopupType.MediumCaution);
    }

    private void OnMobStateChanged(Entity<FaunaProjectileBurstComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            DeathBurst(ent);
    }

    private void OnDestruction(Entity<FaunaProjectileBurstComponent> ent, ref DestructionEventArgs args)
    {
        DeathBurst(ent);
    }

    private void OnDamageChanged(Entity<FaunaProjectileBurstComponent> ent, ref DamageChangedEvent args)
    {
        if (!ent.Comp.CancelOnHit
            || !args.DamageIncreased
            || args.Origin == null
            || ent.Comp.FireAt == null && ent.Comp.VolleyShotsLeft <= 0)
        {
            return;
        }

        if (ent.Comp.FireAt != null)
            SetWindup(ent, false);

        ent.Comp.FireAt = null;
        ent.Comp.VolleyShotsLeft = 0;
        _appearance.SetData(ent, FaunaProjectileBurstVisuals.Firing, ent.Comp.SpiralShotsLeft > 0);
    }

    private void OnTerminating(Entity<FaunaProjectileBurstComponent> ent, ref EntityTerminatingEvent args)
    {
        foreach (var shot in ent.Comp.Shots)
        {
            if (TerminatingOrDeleted(shot) || !TryComp<ProjectileComponent>(shot, out var projectile))
                continue;

            projectile.Shooter = null;
            projectile.Weapon = null;
            Dirty(shot, projectile);
        }
    }

    private void Fire(Entity<FaunaProjectileBurstComponent> ent)
    {
        if (_random.Prob(ent.Comp.SpiralChance))
        {
            ent.Comp.SpiralShotsLeft = ent.Comp.SpiralCount;
            ent.Comp.NextSpiralShot = _timing.CurTime;
            _audio.PlayPvs(ent.Comp.SpiralSound, ent);
            if (ent.Comp.SpiralPopup is { } spiral)
                _popup.PopupEntity(Loc.GetString(spiral, ("user", ent.Owner)), ent, PopupType.MediumCaution);
            return;
        }

        if (Aim(ent) is not { } aim)
            return;

        if (ent.Comp.VolleyCount > 0)
        {
            ent.Comp.VolleyShotsLeft = ent.Comp.VolleyCount;
            ent.Comp.NextVolleyShot = _timing.CurTime;
        }
        else
        {
            var first = -(ent.Comp.SpreadCount - 1) / 2f;
            for (var i = 0; i < ent.Comp.SpreadCount; i++)
            {
                Shoot(ent, aim + ent.Comp.SpreadStep * (first + i));
            }
        }

        _audio.PlayPvs(ent.Comp.SpreadSound, ent);
        if (ent.Comp.SpreadPopup is { } spread)
            _popup.PopupEntity(Loc.GetString(spread, ("user", ent.Owner)), ent, PopupType.MediumCaution);
    }

    private void SetWindup(Entity<FaunaProjectileBurstComponent> ent, bool windup)
    {
        _appearance.SetData(ent, FaunaProjectileBurstVisuals.Windup, windup);
        if (ent.Comp.WindupLight)
            _light.SetEnabled(ent, windup);

        if (ent.Comp.WindupJitter <= 0f)
            return;

        if (windup)
            _jitter.AddJitter(ent, ent.Comp.WindupJitter, 8f);
        else
            RemComp<JitteringComponent>(ent);
    }

    /// <summary>
    /// The angle from the mob to its target, or null when the target is gone.
    /// </summary>
    private Angle? Aim(Entity<FaunaProjectileBurstComponent> ent)
    {
        if (ent.Comp.Target is not { } target || Deleted(target))
            return null;

        var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(ent);
        return direction.ToWorldAngle();
    }

    private void DeathBurst(Entity<FaunaProjectileBurstComponent> ent)
    {
        if (ent.Comp.DeathBurstDone || ent.Comp.DeathCount <= 0)
            return;

        ent.Comp.DeathBurstDone = true;
        ent.Comp.ActionProjectile = null;
        PruneShots(ent);
        var step = Angle.FromDegrees(360.0 / ent.Comp.DeathCount);
        for (var i = 0; i < ent.Comp.DeathCount; i++)
        {
            Shoot(ent, step * i);
        }
    }

    private void Shoot(Entity<FaunaProjectileBurstComponent> ent, Angle angle)
    {
        var coords = _transform.GetMapCoordinates(ent);
        var projectile = Spawn(ent.Comp.ActionProjectile ?? ent.Comp.Projectile, coords);
        _gun.ShootProjectile(projectile, angle.ToWorldVec(), default, ent, ent, ent.Comp.Speed);
        ent.Comp.Shots.Add(projectile);
    }

    private void PruneShots(Entity<FaunaProjectileBurstComponent> ent)
    {
        ent.Comp.Shots.RemoveAll(shot => TerminatingOrDeleted(shot));
    }
}
