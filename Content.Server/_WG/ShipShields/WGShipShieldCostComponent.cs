using Content.Shared._WG.ShipShields;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipShields;

/// <summary>What a generator's raised field costs its ship: thrust, turning, stealth, and its guns when it is beaten down.</summary>
[RegisterComponent]
public sealed partial class WGShipShieldCostComponent : Component
{
    /// <summary>Share of linear thrust the ship keeps while this generator's field is up.</summary>
    [DataField]
    public float ThrustFactor = 0.8f;

    /// <summary>Share of turning thrust the ship keeps while this generator's field is up.</summary>
    [DataField]
    public float TurnFactor = 0.85f;

    /// <summary>Heat per second the raised field adds to the ship's thermal signature.</summary>
    [DataField]
    public float Signature = 1000000f;

    /// <summary>Signature heat added to the ship at once for each point of damage the field absorbs.</summary>
    [DataField]
    public float HitSignature = 400f;

    /// <summary>Heat each point of damage the field absorbs adds to the ship; hull collisions are charged in full.</summary>
    [DataField]
    public float HitHeat = WGShipShieldEffects.HitHeat;

    /// <summary>Share of the ship's capacity its heat drops to when damage beats the field down; left out, the collapse sheds nothing.</summary>
    [DataField]
    public float? CollapseHeat;

    /// <summary>Seconds this generator's raised field must go unhit before the heat it absorbed starts to cool.</summary>
    [DataField]
    public float HardHeatHold = WGShipShieldEffects.HardHeatHold;

    /// <summary>Seconds the ship's guns stay locked after damage beats the field down; left out, they stay locked through the overload wait.</summary>
    [DataField]
    public float? GunLockout;

    /// <summary>Effect spawned on each of the ship's mounted guns for as long as they are locked.</summary>
    [DataField]
    public EntProtoId? GunLockEffect = "WGEffectShieldGunLock";
}
