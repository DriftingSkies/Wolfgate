using Content.Server._WG.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;

namespace Content.Server._Crescent.ShipShields;

// A ship generator recovers by Starsector's overload rule: heat overrunning the field, past its overload limit or at its
// power cap, holds it down for the generator's overload wait, and then it comes back with whatever heat is left. Losing
// power holds it down only until power returns. Outpost generators keep the upstream rule of recharging to zero.
public sealed partial class ShipShieldsSystem
{
    /// <summary>Starts a ship generator's overload wait when its heat has reached its power cap.</summary>
    private void OverloadWolfgateAtPowerCap(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        if (CalculateLoadDamage(emitter) >= emitter.MaxDraw && HasComp<WGShipShieldCostComponent>(uid))
            emitter.OverloadAccumulator = MathF.Max(emitter.OverloadAccumulator, WolfgateOverloadWait(uid, emitter));
    }

    /// <summary>Takes a powered ship generator out of recharge mode once its heat is below its power cap, so only the overload wait holds its field down.</summary>
    private void ReleaseWolfgateRecharge(EntityUid uid, ShipShieldEmitterComponent emitter, bool powered)
    {
        if (emitter.Recharging && powered && CalculateLoadDamage(emitter) < emitter.MaxDraw && HasComp<WGShipShieldCostComponent>(uid))
            emitter.Recharging = false;
    }

    /// <summary>
    /// Estimates automatic startup with unchanged power and no further damage. A ship's field is down while it waits,
    /// so it cools at the plain rate while overloaded, the field-down rate after and its vent rate while venting, and a
    /// vent holds the field down until no heat is left. Cooling is the ship's, so
    /// the generator's damage falls by its <paramref name="share"/> of it, and <paramref name="hullCooling"/> is what
    /// the hull adds to the generator's rate.
    /// </summary>
    public static (WGShipShieldRecoveryStatus Status, int Seconds) EstimateWolfgateShieldRecovery(
        ShipShieldEmitterComponent emitter, bool powered, bool enabled = true, bool disabled = false, bool ship = false,
        bool venting = false, float share = 1f, float hullCooling = 0f)
    {
        if (!enabled)
            return (WGShipShieldRecoveryStatus.Lowered, -1);
        if (disabled)
            return (WGShipShieldRecoveryStatus.Disabled, -1);
        if (!powered)
            return (WGShipShieldRecoveryStatus.NoPower, -1);

        var damage = emitter.Damage;
        var overload = emitter.OverloadAccumulator;
        var recharging = emitter.Recharging;
        var elapsed = Math.Max(0f, EmitterUpdateRate - emitter.Accumulator);
        var capacity = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
        var status = WGShipShieldRecoveryStatus.Ready;
        if (!float.IsFinite(damage) || !float.IsFinite(overload) || !float.IsFinite(elapsed) ||
            !float.IsFinite(emitter.MaxDraw) || emitter.MaxDraw < 0f)
            return (WGShipShieldRecoveryStatus.Ready, -1);
        // The hull's heat stops at the ship's capacity, which is the generator's own in its range; with the field down the excess is lost.
        if (ship)
            damage = MathF.Min(damage, capacity);
        // A ship generator's wait and vent scale with the whole ship's capacity, the generator's over its share.
        var shipCapacity = ship && share > 0f ? capacity / share : capacity;
        var wait = ship ? WGShipShieldEffects.OverloadWait(emitter.DamageOverloadTimePunishment, shipCapacity)
            : emitter.DamageOverloadTimePunishment;

        // Bound malformed or impractically long configurations without blocking UI updates.
        for (var tick = 0; tick < 2400; tick++)
        {
            var capped = WolfgateLoadAt(damage, emitter) >= emitter.MaxDraw;
            if (capped)
                recharging = true;
            var rechargeThisTick = recharging;
            if (overload > 0f)
                overload -= EmitterUpdateRate;
            // A ship generator at its power cap starts its full wait after this tick's countdown, as the update does.
            if (ship && capped)
                overload = MathF.Max(overload, wait);
            var plain = emitter.HealPerSecond + hullCooling;
            // A ship generator still waiting has its field down: the ship cools by the same rule as every other ship.
            var healed = (ship ? WGShipShieldEffects.CoolingRate(plain, shipCapacity, false, venting, overload >= 1f)
                : recharging ? plain * emitter.UnpoweredBonus : plain) * EmitterUpdateRate;
            healed *= share;
            var ventedThisTick = venting;
            damage -= healed;
            if (damage <= 0f)
            {
                damage = 0f;
                recharging = false;
                venting = false;
            }
            if (ship && WolfgateLoadAt(damage, emitter) < emitter.MaxDraw)
                recharging = false;
            if (damage > emitter.DamageLimit)
                overload = wait;
            if (tick == 0)
            {
                var overloaded = overload >= 1f;
                status = ventedThisTick ? WGShipShieldRecoveryStatus.Venting : rechargeThisTick
                    ? overloaded ? WGShipShieldRecoveryStatus.RechargingAndOverloaded : WGShipShieldRecoveryStatus.Recharging
                    : overloaded ? WGShipShieldRecoveryStatus.Overloaded : WGShipShieldRecoveryStatus.Ready;
            }
            if (!float.IsFinite(damage) || !float.IsFinite(overload) || !float.IsFinite(healed))
                return (status, -1);
            if (!recharging && !venting && overload < 1f)
                return (status, (int) Math.Ceiling(elapsed));
            if (damage > 0f && healed <= 0f && (recharging || venting || damage > emitter.DamageLimit))
                return (status, -1);
            elapsed += EmitterUpdateRate;
        }
        return (status, -1);
    }

    /// <summary>Seconds until an overloaded generator's wait ends at an update tick; 0 when it isn't overloaded.</summary>
    public static float WolfgateOverloadSeconds(ShipShieldEmitterComponent emitter)
    {
        // The wait drops by one tick's worth each tick and is over once below a second, when the field may rise.
        if (!(emitter.OverloadAccumulator >= 1f) || !float.IsFinite(emitter.OverloadAccumulator))
            return 0f;
        var ticks = MathF.Floor((emitter.OverloadAccumulator - 1f) / EmitterUpdateRate) + 1f;
        return MathF.Max(0f, EmitterUpdateRate - emitter.Accumulator) + (ticks - 1f) * EmitterUpdateRate;
    }

    /// <summary>The power a generator would draw for its damage at the given heat.</summary>
    private static float WolfgateLoadAt(float damage, ShipShieldEmitterComponent emitter)
    {
        return (float) Math.Clamp(Math.Pow(damage, emitter.DamageExp) * emitter.PowerModifier, 0d, emitter.MaxDraw);
    }
}
