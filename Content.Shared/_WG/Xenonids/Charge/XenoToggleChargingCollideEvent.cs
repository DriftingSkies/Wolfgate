namespace Content.Shared._WG.Xenonids.Charge;

[ByRefEvent]
public record struct XenoToggleChargingCollideEvent(Entity<ActiveXenoToggleChargingComponent> Charger, bool Handled = false);
