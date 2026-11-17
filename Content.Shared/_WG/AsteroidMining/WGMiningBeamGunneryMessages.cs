using Robust.Shared.Serialization;

namespace Content.Shared._WG.AsteroidMining;

/// <summary>Sets the power, from zero to one, of every mining laser on the gunnery console's ship.</summary>
[Serializable, NetSerializable]
public sealed class WGMiningBeamPowerMessage(float power) : BoundUserInterfaceMessage
{
    public readonly float Power = power;
}

/// <summary>The mining lasers of a gunnery console's ship and the rock they are working, for its mining panel.</summary>
[Serializable, NetSerializable]
public sealed class WGMiningBeamGunneryUpdateMessage(WGMiningBeamGunneryState state) : BoundUserInterfaceMessage
{
    public readonly WGMiningBeamGunneryState State = state;
}

[Serializable, NetSerializable]
public readonly record struct WGMiningBeamGunneryState(
    int Lasers,
    float Power,
    // Seconds until an overloaded laser can fire again.
    float Cooldown,
    WGMiningBeamTargetEntry? Target);

[Serializable, NetSerializable]
public readonly record struct WGMiningBeamOreEntry(string Name, float Fraction, Color Color);

[Serializable, NetSerializable]
public readonly record struct WGMiningBeamTargetEntry(
    string Name,
    float Mass,
    WGMiningBeamOreEntry[] Ores,
    float Resistance,
    float WindowLow,
    float WindowHigh,
    float Charge,
    float Progress,
    // Overcharge time as a share of the time it takes to blow up.
    float Overcharge,
    // Share of the mass a crack would keep now, after time spent in the hot band.
    float Yield);
