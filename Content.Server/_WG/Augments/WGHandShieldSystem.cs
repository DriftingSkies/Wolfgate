using System.Diagnostics.CodeAnalysis;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Augments;

/// <summary>
/// Marks a body as shielded while a hand shield is raised, soaks the shots, beams and blows (stamina included) it
/// covers, and recharges lowered shields. Only one shield goes up at a time.
/// </summary>
public sealed class WGHandShieldSystem : SharedWGHandShieldSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WGDeployableImplantSystem _deployables = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGHandShieldComponent, WGDeployAttemptEvent>(OnDeployAttempt);
        SubscribeLocalEvent<WGHandShieldUserComponent, HitScanReflectAttemptEvent>(OnHitscan);
        // Ahead of Wolfmed's routing, which takes over the body's damage and cancels the event itself.
        SubscribeLocalEvent<WGHandShieldUserComponent, BeforeDamageChangedEvent>(OnBeforeDamage,
            before: [typeof(WoundDamageRoutingSystem)]);
        SubscribeLocalEvent<WGHandShieldUserComponent, BeforeStaminaDamageEvent>(OnBeforeStamina);
    }

    private void OnDeployAttempt(Entity<WGHandShieldComponent> ent, ref WGDeployAttemptEvent args)
    {
        if (ent.Comp.Collapsed)
        {
            _popup.PopupEntity(Loc.GetString("wg-hand-shield-recharging"), args.Body, args.Body);
            args.Cancelled = true;
        }
        else if (TryComp<WGHandShieldUserComponent>(args.Body, out var user) && user.Part != ent.Owner)
        {
            _popup.PopupEntity(Loc.GetString("wg-hand-shield-other"), args.Body, args.Body);
            args.Cancelled = true;
        }
    }

    protected override void ProjectileBlocked(Entity<WGHandShieldUserComponent> ent, WGHandShieldComponent shield,
        EntityUid projectile, float damage)
    {
        Soak(ent, shield, damage, true);
        QueueDel(projectile);
    }

    private void OnHitscan(Entity<WGHandShieldUserComponent> ent, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected || !Covers(ent, -args.Direction, out var shield))
            return;

        Soak(ent, shield, args.Damage?.GetTotal().Float() ?? 0f, true);
        var spread = _random.NextAngle(-shield.DeflectSpread / 2, shield.DeflectSpread / 2);
        args.Direction = -spread.RotateVec(args.Direction);
        args.Reflected = true;
    }

    private void OnBeforeDamage(Entity<WGHandShieldUserComponent> ent, ref BeforeDamageChangedEvent args)
    {
        var damage = args.Damage.GetTotal().Float();
        if (!args.Cancelled && damage > 0 && TryBlockBlow(ent, args.Origin, args.Tool, damage))
            args.Cancelled = true;
    }

    private void OnBeforeStamina(Entity<WGHandShieldUserComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        if (!args.Cancelled && args.Value > 0 && TryBlockBlow(ent, args.Source, args.With, args.Value))
            args.Cancelled = true;
    }

    /// <summary>
    /// Soaks a blow if it is one (a weapon swung or a fist thrown by someone else, not a shot that already got past)
    /// and comes from where the shield faces.
    /// </summary>
    private bool TryBlockBlow(Entity<WGHandShieldUserComponent> ent, EntityUid? origin, EntityUid? tool, float amount)
    {
        if (!IsBlow(ent, origin, tool, out var attacker))
            return false;

        var from = _transform.GetWorldPosition(attacker.Value) - _transform.GetWorldPosition(ent);
        if (!Covers(ent, from, out var shield))
            return false;

        Soak(ent, shield, amount, false);
        return true;
    }

    private bool IsBlow(EntityUid body, EntityUid? origin, EntityUid? tool, [NotNullWhen(true)] out EntityUid? attacker)
    {
        attacker = origin;
        return origin is { } who
               && who != body
               && tool is { } with
               && HasComp<MeleeWeaponComponent>(with)
               && !HasComp<ProjectileComponent>(with);
    }

    /// <summary>Takes a covered hit out of the shield's charge, collapsing it if that drains it.</summary>
    private void Soak(Entity<WGHandShieldUserComponent> ent, WGHandShieldComponent shield, float amount, bool shot)
    {
        // A blow that both hurts and tires is one block, and one sound.
        if (shield.LastBlockSound != _timing.CurTime)
        {
            shield.LastBlockSound = _timing.CurTime;
            _audio.PlayPvs(shot ? shield.ShotSound : shield.BlowSound, ent, AudioParams.Default.WithVariation(0.05f));
        }

        shield.Charge -= amount;
        if (shield.Charge <= 0)
            Collapse(ent.Comp.Part, shield, ent);
    }

    private void Collapse(EntityUid part, WGHandShieldComponent shield, EntityUid body)
    {
        shield.Charge = 0;
        shield.Collapsed = true;
        Dirty(part, shield);
        _deployables.Stow(part, shield);
        Lower(part, shield);
        _audio.PlayPvs(shield.CollapseSound, body);
        _popup.PopupEntity(Loc.GetString("wg-hand-shield-collapse"), body, body, PopupType.MediumCaution);
        if (shield.ActionEntity is { } action)
            _actions.SetCooldown(action, TimeSpan.FromSeconds(shield.MaxCharge / shield.RechargeRate));
    }

    private void Raise(EntityUid part, WGHandShieldComponent shield, EntityUid body)
    {
        var user = EnsureComp<WGHandShieldUserComponent>(body);
        user.Part = part;
        Dirty(body, user);
        shield.RaisedBy = body;
    }

    private void Lower(EntityUid part, WGHandShieldComponent shield)
    {
        if (shield.RaisedBy is { } body
            && TryComp<WGHandShieldUserComponent>(body, out var user)
            && user.Part == part)
            RemCompDeferred(body, user);

        shield.RaisedBy = null;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WGHandShieldComponent, BodyPartComponent>();
        while (query.MoveNext(out var uid, out var shield, out var part))
        {
            var raisedBy = Exists(shield.Deployed) ? part.Body : null;
            if (raisedBy != shield.RaisedBy)
            {
                Lower(uid, shield);
                if (raisedBy is { } body)
                    Raise(uid, shield, body);
            }

            if (raisedBy != null || shield.Charge >= shield.MaxCharge)
                continue;

            shield.Charge = Math.Min(shield.MaxCharge, shield.Charge + shield.RechargeRate * frameTime);
            if (shield.Charge >= shield.MaxCharge && shield.Collapsed)
            {
                shield.Collapsed = false;
                Dirty(uid, shield);
            }
        }
    }
}
