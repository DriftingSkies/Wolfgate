using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipShields;

/// <summary>Explains why a field is offline and whether automatic recovery can be timed.</summary>
[Serializable, NetSerializable]
public enum WGShipShieldRecoveryStatus : byte
{
    None,
    Ready,
    Recharging,
    Overloaded,
    RechargingAndOverloaded,
    NoPower,
    Lowered,
    Disabled,
    FtlLocked,
    Venting,
}

/// <summary>Identifies the shield generator's shared control panel.</summary>
[Serializable, NetSerializable]
public enum WGShipShieldUiKey : byte
{
    Key,
}

/// <summary>Requests a manual field enable or disable from an authorized control panel.</summary>
[Serializable, NetSerializable]
public sealed class WGShipShieldSetEnabledMessage : BoundUserInterfaceMessage
{
    /// <summary>The requested manual operating state.</summary>
    public bool Enabled;

    /// <summary>Creates one manual operating request.</summary>
    public WGShipShieldSetEnabledMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

/// <summary>Requests a vent of the ship's heat from an authorized control panel.</summary>
[Serializable, NetSerializable]
public sealed class WGShipShieldVentMessage : BoundUserInterfaceMessage;

/// <summary>Requests a new overload strength at a generator that allows it.</summary>
[Serializable, NetSerializable]
public sealed class WGShipShieldSetStrengthMessage : BoundUserInterfaceMessage
{
    /// <summary>The damage the field should take before it overloads.</summary>
    public float Strength;

    /// <summary>Creates one strength request.</summary>
    public WGShipShieldSetStrengthMessage(float strength)
    {
        Strength = strength;
    }
}

/// <summary>Publishes the generator's ship-wide shield settings and health.</summary>
[Serializable, NetSerializable]
public sealed class WGShipShieldGeneratorUiState : BoundUserInterfaceState
{
    /// <summary>The same authoritative shield state shown at the helm.</summary>
    public WGShipShieldShuntState ShieldShunt;

    /// <summary>Creates one generator panel snapshot.</summary>
    public WGShipShieldGeneratorUiState(WGShipShieldShuntState shieldShunt)
    {
        ShieldShunt = shieldShunt;
    }
}

/// <summary>Updates shield controls without rebuilding the helm's navigation snapshot.</summary>
[Serializable, NetSerializable]
public sealed class WGShipShieldHelmUpdateMessage : BoundUserInterfaceMessage
{
    /// <summary>The current shield allocation and integrity.</summary>
    public WGShipShieldShuntState ShieldShunt;

    /// <summary>Creates one shield-only helm update.</summary>
    public WGShipShieldHelmUpdateMessage(WGShipShieldShuntState shieldShunt)
    {
        ShieldShunt = shieldShunt;
    }
}

/// <summary>Requests a ship-local target allocation from live controls.</summary>
[Serializable, NetSerializable]
public sealed class WGShipShieldSetShuntMessage : BoundUserInterfaceMessage
{
    /// <summary>Requested ship-local sector direction.</summary>
    public float DirectionRadians;
    /// <summary>Fraction of capacity redirected into the sector.</summary>
    public float Concentration;
    /// <summary>Width of the selected sector.</summary>
    public float ArcRadians;

    /// <summary>Creates one target allocation request.</summary>
    public WGShipShieldSetShuntMessage(float directionRadians, float concentration, float arcRadians)
    {
        DirectionRadians = directionRadians;
        Concentration = concentration;
        ArcRadians = arcRadians;
    }
}

/// <summary>Authoritative allocation and health shared by every helm on the ship.</summary>
[Serializable, NetSerializable]
public sealed class WGShipShieldShuntState
{
    /// <summary>The ship has an emitter and accepts allocation settings.</summary>
    public bool Available;
    /// <summary>The ship's installed emitters are manually enabled.</summary>
    public bool Enabled;
    /// <summary>A shield field currently exists.</summary>
    public bool Active;
    /// <summary>The current automatic recovery or waiting condition.</summary>
    public WGShipShieldRecoveryStatus RecoveryStatus;
    /// <summary>Whole seconds until automatic startup, or minus one when recovery cannot be timed.</summary>
    public int RecoverySeconds;
    /// <summary>Runtime specifications of the emitter supplying this snapshot.</summary>
    public WGShipShieldGeneratorStats? Stats;
    /// <summary>Remaining shield capacity.</summary>
    public float Health;
    /// <summary>Current ship-local sector direction.</summary>
    public float DirectionRadians;
    /// <summary>Current redirected capacity fraction.</summary>
    public float Concentration;
    /// <summary>Current selected sector width.</summary>
    public float ArcRadians;

    /// <summary>Requested sector direction during redistribution.</summary>
    public float TargetDirectionRadians;
    /// <summary>Requested redirected capacity fraction.</summary>
    public float TargetConcentration;
    /// <summary>Requested reinforced sector width.</summary>
    public float TargetArcRadians;

    /// <summary>Whole seconds the ship's guns stay locked after its field was beaten down; 0 when they are free.</summary>
    public int GunLockSeconds;
    /// <summary>Whether the ship is too hot to fire through its raised field.</summary>
    public bool GunsHeld;
    /// <summary>The part of the heat gauge that is hard heat: it came through the shield and stays while the field is up.</summary>
    public float HardHeat;
    /// <summary>Whole seconds the ship's vent still needs, or minus one when it can't be timed; 0 when it isn't venting.</summary>
    public int VentSeconds;
    /// <summary>Whether the ship could start a vent now.</summary>
    public bool CanVent;
    /// <summary>The console speaks for a ship, which holds heat and can vent it with or without an emitter.</summary>
    public bool Hull;
    /// <summary>The ship's heat as a share of what it can hold.</summary>
    public float Heat;

    /// <summary>Creates an authoritative snapshot for all helms on the ship.</summary>
    public WGShipShieldShuntState(bool available, bool active, float health, float directionRadians, float concentration, float arcRadians, bool enabled = true)
    {
        Available = available;
        Enabled = enabled;
        Active = active;
        TargetDirectionRadians = directionRadians;
        TargetConcentration = concentration;
        TargetArcRadians = arcRadians;
        Health = health;
        DirectionRadians = directionRadians;
        Concentration = concentration;
        ArcRadians = arcRadians;
    }
}
