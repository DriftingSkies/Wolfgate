using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipShields;

/// <summary>Publishes the selected emitter's runtime operating specifications.</summary>
[Serializable, NetSerializable]
public sealed class WGShipShieldGeneratorStats
{
    /// <summary>The selected generator's display name.</summary>
    public string Name;
    /// <summary>Heat the generator holds before power or overload forces recharge; a ship's hull adds its own.</summary>
    public float Capacity;
    /// <summary>Heat the hull it is installed on holds by its size, added to a ship generator's capacity; 0 otherwise.</summary>
    public float HullCapacity;
    /// <summary>Heat that triggers overload punishment.</summary>
    public float DamageLimit;
    /// <summary>Heat shed per second with the field up, the hull's part included.</summary>
    public float HealPerSecond;
    /// <summary>The part of that cooling the hull it is installed on does by its size; 0 off a ship.</summary>
    public float HullCooling;
    /// <summary>Heat shed per second at the fast rate.</summary>
    public float RechargePerSecond;
    /// <summary>Idle power demand in watts.</summary>
    public float BasePowerWatts;
    /// <summary>Capped total power demand in watts.</summary>
    public float MaximumPowerWatts;
    /// <summary>Overload cooldown duration in seconds.</summary>
    public float OverloadSeconds;
    /// <summary>Share of linear thrust the ship keeps while the field is up.</summary>
    public float ThrustFactor = 1f;
    /// <summary>Share of turning thrust the ship keeps while the field is up.</summary>
    public float TurnFactor = 1f;
    /// <summary>Seconds the ship's guns stay locked after damage beats the field down.</summary>
    public float GunLockSeconds;
    /// <summary>Seconds the raised field must go unhit before the heat it absorbed starts to cool; 0 for an outpost's generator.</summary>
    public float HardHeatHold;
    /// <summary>Whether the generator sheds heat at its fast rate whenever its field is down, not only in recharge mode.</summary>
    public bool Vents;
    /// <summary>Heat each point of damage the field absorbs adds.</summary>
    public float HitHeat = 1f;
    /// <summary>Share of the ship's capacity its heat drops to when damage beats the field down; 0 when a collapse sheds nothing.</summary>
    public float CollapseHeat;

    /// <summary>Creates one runtime generator specification.</summary>
    public WGShipShieldGeneratorStats(string name, float capacity, float damageLimit, float healPerSecond,
        float rechargePerSecond, float basePowerWatts, float maximumPowerWatts, float overloadSeconds)
    {
        Name = name;
        Capacity = capacity;
        DamageLimit = damageLimit;
        HealPerSecond = healPerSecond;
        RechargePerSecond = rechargePerSecond;
        BasePowerWatts = basePowerWatts;
        MaximumPowerWatts = maximumPowerWatts;
        OverloadSeconds = overloadSeconds;
    }

    /// <summary>Checks whether optional specifications have the same visible values.</summary>
    public static bool Same(WGShipShieldGeneratorStats? left, WGShipShieldGeneratorStats? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        return left != null && right != null && left.Name == right.Name && left.Capacity == right.Capacity &&
            left.HullCapacity == right.HullCapacity && left.HullCooling == right.HullCooling &&
            left.DamageLimit == right.DamageLimit && left.HealPerSecond == right.HealPerSecond &&
            left.RechargePerSecond == right.RechargePerSecond && left.BasePowerWatts == right.BasePowerWatts &&
            left.MaximumPowerWatts == right.MaximumPowerWatts && left.OverloadSeconds == right.OverloadSeconds &&
            left.ThrustFactor == right.ThrustFactor && left.TurnFactor == right.TurnFactor &&
            left.GunLockSeconds == right.GunLockSeconds && left.HardHeatHold == right.HardHeatHold && left.Vents == right.Vents &&
            left.HitHeat == right.HitHeat && left.CollapseHeat == right.CollapseHeat;
    }
}
