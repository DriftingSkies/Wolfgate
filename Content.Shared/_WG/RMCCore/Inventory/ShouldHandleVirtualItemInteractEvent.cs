using Content.Shared.Interaction;

namespace Content.Shared._WG.RMCCore.Inventory;

[ByRefEvent]
public record struct ShouldHandleVirtualItemInteractEvent(BeforeRangedInteractEvent Event, bool Handle = false);
