using Content.Shared.Inventory;

namespace Content.Shared._WG.RMCCore.Armor;

[ByRefEvent]
public record struct RefreshArmorSpeedTierEvent(
    SlotFlags TargetSlots,
    string? SpeedTier = ""
) : IInventoryRelayEvent;
