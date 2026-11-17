namespace Content.Shared._WG.RMCCore.Dialog;

[ByRefEvent]
public readonly record struct DialogChosenEvent(EntityUid Actor, int Index);
