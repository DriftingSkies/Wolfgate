namespace Content.Shared._WG.Xenonids.Leap;

[ByRefEvent]
public readonly record struct XenoLeapHitEvent(XenoLeapingComponent Leaping, EntityUid Hit);
