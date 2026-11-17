using Content.Shared.FixedPoint;

namespace Content.Shared._WG.Xenonids.Energy;

[ByRefEvent]
public record struct XenoEnergyChangedEvent(FixedPoint2 NewEnergy);
