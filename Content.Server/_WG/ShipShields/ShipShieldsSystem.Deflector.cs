using Content.Server._WG.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Weapons.Ranged.Components;
using Content.Shared._WG.ShipShields;
using Content.Shared.Examine;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Prototypes;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    private void InitializeWolfgateDeflectors()
    {
        SubscribeLocalEvent<WGShipShieldDeflectorComponent, ExaminedEvent>(OnWolfgateDeflectorExamined);
    }

    private void OnWolfgateDeflectorExamined(EntityUid uid, WGShipShieldDeflectorComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;
        args.PushMarkup(Loc.GetString("wg-shield-examine-deflector", ("reduction", MathF.Round(Math.Clamp(component.Reduction, 0f, 1f) * 100f))));
        var arc = WolfgateEmitterMaxArc(uid);
        if (arc < WGShipShieldShuntMath.FullArc - 0.00001f)
            args.PushMarkup(Loc.GetString("wg-shield-examine-arc", ("arc", MathF.Round(arc * 180f / MathF.PI))));
    }

    /// <summary>Widest arc an emitter can cover, in radians; a full circle when it has no limit.</summary>
    private float WolfgateEmitterMaxArc(EntityUid emitter)
    {
        return TryComp<WGShipShieldDeflectorComponent>(emitter, out var deflector)
            ? Math.Clamp(deflector.MaxArc * MathF.PI / 180f, WGShipShieldShuntMath.MinimumArc, WGShipShieldShuntMath.FullArc)
            : WGShipShieldShuntMath.FullArc;
    }

    /// <summary>Arc limit of the emitter holding the grid's field, or of its widest installed emitter while the field is down.</summary>
    private float WolfgateShieldMaxArc(EntityUid grid)
    {
        if (TryComp<ShipShieldedComponent>(grid, out var shielded) && shielded.Source is { } source)
            return WolfgateEmitterMaxArc(source);
        var widest = 0f;
        var query = EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.GridUid == grid && transform.Anchored)
                widest = MathF.Max(widest, WolfgateEmitterMaxArc(uid));
        }
        return widest > 0f ? widest : WGShipShieldShuntMath.FullArc;
    }

    /// <summary>Keeps a requested allocation inside a limited emitter's arc, with all power in it.</summary>
    private void LimitWolfgateShieldShunt(EntityUid grid, ref float concentration, ref float arcRadians)
    {
        var max = WolfgateShieldMaxArc(grid);
        if (max >= WGShipShieldShuntMath.FullArc - 0.00001f)
            return;
        concentration = 1f;
        arcRadians = MathF.Min(arcRadians, max);
    }

    /// <summary>Fits the ship's allocation to the arc of the emitter that raised this field; an untouched allocation takes the whole arc.</summary>
    private void EnforceWolfgateShieldArcLimit(EntityUid shield, EntityUid grid, WGShipShieldShuntComponent allocation)
    {
        if (!TryComp<ShipShieldComponent>(shield, out var field) || field.Source is not { } emitter)
            return;
        var max = WolfgateEmitterMaxArc(emitter);
        if (max >= WGShipShieldShuntMath.FullArc - 0.00001f)
            return;
        var arc = allocation.TargetInitialized ? MathF.Min(allocation.ArcRadians, max) : max;
        if (!allocation.TargetInitialized)
        {
            allocation.TargetInitialized = true;
            allocation.TargetDirectionRadians = allocation.DirectionRadians;
            allocation.TargetArcRadians = max;
        }
        allocation.TargetConcentration = 1f;
        allocation.TargetArcRadians = MathF.Min(allocation.TargetArcRadians, max);
        if (allocation.Concentration == 1f && allocation.ArcRadians == arc)
            return;
        allocation.Concentration = 1f;
        allocation.ArcRadians = arc;
        DirtyFields(grid, allocation, null, nameof(WGShipShieldShuntComponent.Concentration), nameof(WGShipShieldShuntComponent.ArcRadians));
    }

    /// <summary>True once a deflector field has slowed this projectile and let it through.</summary>
    private bool HasWolfgateDeflectorPassed(EntityUid shield, EntityUid projectile)
    {
        return TryComp<WGShipShieldDeflectedComponent>(projectile, out var deflected) && deflected.Shields.Contains(shield);
    }

    /// <summary>Slows a solid round at a deflector field and lets it through; false when the field stops it as a normal shield would.</summary>
    private bool TryDeflectWolfgateProjectile(EntityUid shield, ShipShieldComponent field, EntityUid source,
        EntityUid uid, ProjectileComponent projectile, float strength)
    {
        if (!TryComp<WGShipShieldDeflectorComponent>(source, out var deflector) || IsWolfgateDeflectorStopped(deflector, uid) ||
            !TryComp<ShipShieldEmitterComponent>(source, out var emitter))
            return false;

        var reduction = Math.Clamp(deflector.Reduction, 0f, 1f);
        var removed = (float) projectile.Damage.GetTotal() * reduction;
        projectile.Damage = projectile.Damage * (1f - reduction);
        if (TryComp<ExplosiveComponent>(uid, out var explosive))
        {
            if (_prototypeManager.TryIndex(explosive.ExplosionType, out var type))
                removed += explosive.TotalIntensity * (float) type.DamagePerIntensity.GetTotal() * reduction;
            // Scaling all three weakens the blast on every tile over the same footprint; the total alone only shrinks it.
            explosive.TotalIntensity *= 1f - reduction;
            if (reduction < 1f)
            {
                explosive.MaxIntensity *= 1f - reduction;
                explosive.IntensitySlope *= 1f - reduction;
            }
        }

        var charged = removed / strength;
        emitter.Damage += charged;
        EnsureComp<WGShipShieldDeflectedComponent>(uid).Shields.Add(shield);
        ReportWolfgateShieldAttack(field, charged, CompOrNull<ProjectileGridPhaseComponent>(uid)?.SourceGrid,
            projectile.Weapon, projectile.Shooter);
        WolfgateShieldImpact(shield, WolfgateShieldImpactPosition(shield, uid), WGShipShieldEffects.ImpactStrength(charged,
            WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp)));
        return true;
    }

    /// <summary>Checks a projectile and its parents against the deflector's stop list.</summary>
    private bool IsWolfgateDeflectorStopped(WGShipShieldDeflectorComponent deflector, EntityUid projectile)
    {
        if (deflector.Stopped.Count == 0 || MetaData(projectile).EntityPrototype is not { } prototype)
            return false;
        foreach (var (id, _) in _prototypeManager.EnumerateAllParents<EntityPrototype>(prototype.ID, includeSelf: true))
        {
            if (deflector.Stopped.Contains(id))
                return true;
        }
        return false;
    }

    /// <summary>Handles a physics contact before the hit is resolved; true when the round carries on through a deflector field.</summary>
    public bool PassWolfgateDeflector(EntityUid shield, EntityUid uid, ProjectileComponent projectile)
    {
        if (HasWolfgateDeflectorPassed(shield, uid))
            return true;
        if (!TryComp<ShipShieldComponent>(shield, out var field) || field.Source is not { } source ||
            !HasComp<WGShipShieldDeflectorComponent>(source) || projectile.ProjectileSpent ||
            !_shipWeaponProjectileQuery.HasComponent(uid) || IsWolfgateShieldFriendlyProjectile(shield, uid))
            return false;
        var strength = WolfgateShieldStrength(shield, uid);
        return strength > 0f && TryDeflectWolfgateProjectile(shield, field, source, uid, projectile, strength);
    }
}
