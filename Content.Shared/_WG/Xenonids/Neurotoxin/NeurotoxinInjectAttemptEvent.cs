namespace Content.Shared._WG.Xenonids.Neurotoxin;

[ByRefEvent]
public record struct NeurotoxinInjectAttemptEvent(bool Cancelled = false);
