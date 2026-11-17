using Content.Shared._Crescent.ShipShields;

namespace Content.Server._Crescent.ShipShields;

/// <summary>Reads and sets how much strength a ship's shields have left, for Shipmaster.</summary>
public sealed partial class ShipShieldsSystem
{
    /// <summary>The strength the ship's shields have left, from 0 to 1: the weakest of its generators. False without one.</summary>
    public bool TryGetWolfgateShieldStrength(EntityUid grid, out float strength)
    {
        strength = 1f;
        var found = false;
        var emitters = EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
        while (emitters.MoveNext(out _, out var emitter, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            found = true;
            strength = MathF.Min(strength, GetWolfgateShieldHealth(emitter));
        }

        return found;
    }

    /// <summary>
    /// Sets the strength every shield generator on the ship has left, from 0 to 1. Full strength also ends a
    /// recharge or an overload, so the shield comes straight back up if it is powered and switched on.
    /// </summary>
    /// <returns>How many generators were set.</returns>
    public int SetWolfgateShieldStrength(EntityUid grid, float strength)
    {
        strength = Math.Clamp(strength, 0f, 1f);
        var count = 0;
        var emitters = EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
        while (emitters.MoveNext(out _, out var emitter, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            count++;
            emitter.Damage = WolfgateDamageFor(emitter, 1f - strength);
            if (strength < 1f)
                continue;

            emitter.Recharging = false;
            emitter.OverloadAccumulator = 0f;
        }

        return count;
    }

    /// <summary>
    /// The damage that leaves a generator with a share of its strength lost. Strength is lost to damage against the
    /// limit and to the extra power that damage draws, whichever is worse, so the smaller of the two answers.
    /// </summary>
    private static float WolfgateDamageFor(ShipShieldEmitterComponent emitter, float lost)
    {
        if (lost <= 0f)
            return 0f;

        var byLimit = lost * MathF.Max(emitter.DamageLimit, 1f);
        if (emitter.PowerModifier <= 0f || emitter.DamageExp <= 0f)
            return byLimit;

        var byDraw = MathF.Pow(lost * MathF.Max(emitter.MaxDraw, 1f) / emitter.PowerModifier, 1f / emitter.DamageExp);
        return float.IsFinite(byDraw) ? MathF.Min(byLimit, byDraw) : byLimit;
    }
}
