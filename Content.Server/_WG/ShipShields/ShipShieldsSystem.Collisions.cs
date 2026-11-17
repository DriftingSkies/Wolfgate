using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Server._Crescent.ShipShields.Components;
using Content.Shared._WG.ShipShields;
using Content.Server.Power.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    /// <summary>Charges local shield capacity for hull collision protection and returns unabsorbed energy.</summary>
    public float AbsorbWolfgateCollision(EntityUid grid, Vector2 worldPoint, float energy, float damagePerEnergy)
    {
        if (!float.IsFinite(energy) || energy <= 0f || !float.IsFinite(damagePerEnergy) || damagePerEnergy <= 0f ||
            !float.IsFinite(worldPoint.X) || !float.IsFinite(worldPoint.Y) ||
            !TryComp<ShipShieldedComponent>(grid, out var shielded) || shielded.Source is not { } source ||
            !IsWolfgateShieldLive(shielded.Shield) || !TryComp<PhysicsComponent>(shielded.Shield, out var physics) ||
            !physics.CanCollide || HasComp<ShipShieldDisabledGridComponent>(grid) || !TryComp<ShipShieldEmitterComponent>(source, out var emitter) ||
            emitter.Recharging || emitter.OverloadAccumulator > 0f || !IsWolfgateShieldEnabled(grid) ||
            TerminatingOrDeleted(source) || EntityManager.IsQueuedForDeletion(source) ||
            emitter.Shield != shielded.Shield || emitter.Shielded != grid ||
            !TryComp<ShipShieldComponent>(shielded.Shield, out var field) || field.Source != source ||
            !TryComp<WGShipShieldVisualsComponent>(shielded.Shield, out var visuals) || visuals.Grid != grid ||
            Transform(source).GridUid != grid || !Transform(source).Anchored ||
            !TryComp<ApcPowerReceiverComponent>(source, out var power) || !power.Powered)
            return energy;

        var capacity = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
        var remaining = MathF.Max(0f, capacity - emitter.Damage);
        if (remaining <= 0f)
        {
            ExhaustWolfgateCollisionShield(grid, source, emitter);
            return energy;
        }
        var strength = 1f;
        if (TryComp<WGShipShieldShuntComponent>(grid, out var allocation))
        {
            var local = Vector2.Transform(worldPoint, _transformSystem.GetInvWorldMatrix(grid));
            strength = WGShipShieldShuntMath.StrengthMultiplier(local, allocation.Center, allocation.DirectionRadians,
                allocation.Concentration, allocation.ArcRadians);
        }
        if (strength <= 0f || !float.IsFinite(strength))
            return energy;
        var availableEnergy = (double)remaining * strength / damagePerEnergy;
        var absorbed = Math.Min(energy, availableEnergy);
        if (absorbed <= 0d)
            return energy;
        var damage = (float)(absorbed * damagePerEnergy / strength);
        emitter.Damage = absorbed >= availableEnergy ? capacity : Math.Min(capacity, emitter.Damage + damage);
        UpdateWolfgateShieldHealth(shielded.Shield, visuals, source);
        var position = GetWolfgateCollisionImpactPosition(shielded.Shield, worldPoint);
        WolfgateShieldImpact(shielded.Shield, position, WGShipShieldEffects.ImpactStrength(damage, capacity));
        if (emitter.Damage >= capacity)
            ExhaustWolfgateCollisionShield(grid, source, emitter);
        return MathF.Max(0f, energy - (float)absorbed);
    }

    /// <summary>Finds the visible perimeter point nearest a world-space hull contact.</summary>
    public Vector2 GetWolfgateCollisionImpactPosition(EntityUid shield, Vector2 worldPoint)
    {
        var visuals = Comp<WGShipShieldVisualsComponent>(shield);
        var reference = visuals.Grid ?? shield;
        var local = Vector2.Transform(worldPoint, _transformSystem.GetInvWorldMatrix(reference));
        return WGShipShieldGeometry.ClosestPoint(visuals.Contours, local);
    }

    /// <summary>Stops protection immediately when a collision consumes the remaining operating capacity.</summary>
    private void ExhaustWolfgateCollisionShield(EntityUid grid, EntityUid source, ShipShieldEmitterComponent emitter)
    {
        emitter.Recharging = true;
        if (emitter.Damage >= emitter.DamageLimit)
            emitter.OverloadAccumulator = MathF.Max(emitter.OverloadAccumulator, emitter.DamageOverloadTimePunishment);
        if (RemoveWolfgateEmitterShield(source, emitter, "collision capacity exhausted"))
            PlayWolfgateShieldPowerSound(source, grid, false);
        emitter.Shield = null;
        emitter.Shielded = null;
    }
}
