using Content.Server._WG.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Examine;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    /// <summary>Reads operating specifications from the selected emitter's runtime component.</summary>
    public WGShipShieldGeneratorStats GetWolfgateGeneratorStats(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        var cost = CompOrNull<WGShipShieldCostComponent>(uid);
        var hullCooling = WolfgateHullCooling(uid);
        var cooling = emitter.HealPerSecond + hullCooling;
        var capacity = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
        var shipCapacity = Transform(uid) is { Anchored: true, GridUid: { } shipGrid } ? WolfgateShipCapacity(shipGrid, uid, emitter) : capacity;
        var wait = WolfgateOverloadWait(uid, emitter);
        return new WGShipShieldGeneratorStats(Name(uid), capacity, emitter.DamageLimit, cooling,
            cost == null ? cooling * emitter.UnpoweredBonus : WGShipShieldEffects.VentRate(cooling, shipCapacity),
            emitter.BaseDraw, emitter.BaseDraw + emitter.MaxDraw, wait)
        {
            ThrustFactor = cost == null ? 1f : Math.Clamp(cost.ThrustFactor, MinShieldMobility, 1f),
            TurnFactor = cost == null ? 1f : Math.Clamp(cost.TurnFactor, MinShieldMobility, 1f),
            // A ship generator's collapse always waits out its overload, and the guns stay locked through it unless it sets its own lock.
            GunLockSeconds = cost == null ? 0f : MathF.Max(0f, cost.GunLockout ?? wait),
            HardHeatHold = MathF.Max(0f, cost?.HardHeatHold ?? 0f),
            Vents = cost != null,
            HitHeat = cost == null ? 1f : MathF.Max(0f, cost.HitHeat),
            CollapseHeat = Math.Clamp(cost?.CollapseHeat ?? 0f, 0f, 1f),
            HullCooling = hullCooling,
            HullCapacity = cost != null && Transform(uid) is { Anchored: true, GridUid: { } grid } &&
                TryComp<WGShipHeatComponent>(grid, out var hull) ? WolfgateHullCapacity(grid, hull) : 0f,
        };
    }

    /// <summary>Shows this generator's specifications before or after installation.</summary>
    private void ExamineWolfgateGeneratorStats(EntityUid uid, ShipShieldEmitterComponent emitter, ExaminedEvent args)
    {
        var stats = GetWolfgateGeneratorStats(uid, emitter);
        args.PushMarkup(Loc.GetString("wg-shield-examine-capacity", ("rate", WGShipShieldEffects.Display(stats.HitHeat)),
            ("capacity", MathF.Round(stats.Capacity, 0)), ("overload", MathF.Round(stats.DamageLimit, 0))));
        if (stats.Capacity < stats.DamageLimit)
            args.PushMarkup(Loc.GetString("wg-shield-examine-capacity-note"));
        if (stats.HullCapacity > 0f)
        {
            args.PushMarkup(Loc.GetString("wg-shield-examine-hull-capacity", ("hull", MathF.Round(stats.HullCapacity, 0)),
                ("total", MathF.Round(stats.Capacity + stats.HullCapacity, 0))));
        }
        else if (stats.Vents)
            args.PushMarkup(Loc.GetString("wg-shield-examine-adds-capacity"));
        args.PushMarkup(Loc.GetString(stats.Vents ? "wg-shield-examine-cooling" : "wg-shield-examine-recharge",
            ("online", WGShipShieldEffects.Display(stats.HealPerSecond)), ("offline", WGShipShieldEffects.Display(stats.RechargePerSecond)),
            ("down", WGShipShieldEffects.Display(stats.HealPerSecond * WGShipShieldEffects.FieldDownCooling)),
            ("hold", WGShipShieldEffects.Display(stats.HardHeatHold))));
        if (stats.HullCooling > 0f)
            args.PushMarkup(Loc.GetString("wg-shield-examine-hull-cooling", ("hull", WGShipShieldEffects.Display(stats.HullCooling))));
        args.PushMarkup(Loc.GetString("wg-shield-examine-power", ("idle", WGShipShieldEffects.Display(stats.BasePowerWatts / 1000f)), ("maximum", WGShipShieldEffects.Display(stats.MaximumPowerWatts / 1000f))));
        args.PushMarkup(Loc.GetString("wg-shield-examine-overload", ("seconds", WGShipShieldEffects.Display(stats.OverloadSeconds))));
        if (stats.ThrustFactor < 1f || stats.TurnFactor < 1f)
        {
            args.PushMarkup(Loc.GetString("wg-shield-examine-cost",
                ("thrust", MathF.Round((1f - stats.ThrustFactor) * 100f)), ("turn", MathF.Round((1f - stats.TurnFactor) * 100f))));
        }
        if (stats.GunLockSeconds > 0f)
            args.PushMarkup(Loc.GetString("wg-shield-examine-gun-lock", ("seconds", WGShipShieldEffects.Display(stats.GunLockSeconds))));
        if (stats.CollapseHeat > 0f)
            args.PushMarkup(Loc.GetString("wg-shield-collapse-heat", ("share", MathF.Round(stats.CollapseHeat * 100f))));
        if (HasComp<WGShipShieldCostComponent>(uid))
            args.PushMarkup(Loc.GetString("wg-shield-examine-one-per-ship"));
    }
}
