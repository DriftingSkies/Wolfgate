using Content.Shared.FixedPoint;

namespace Content.Shared._WG.RMCCore.Stealth;

[ByRefEvent]
public record struct ActiveInvisibleToggledEvent(
    bool enabled
);