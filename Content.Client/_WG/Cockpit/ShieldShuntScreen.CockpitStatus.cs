using Content.Client._WG.Cockpit;

namespace Content.Client._WG.ShipShields;

public sealed partial class WGShipShieldShuntScreen
{
    /// <summary>Reports shield deployment from the current server snapshot.</summary>
    public WGCockpitStatusReading WgCockpitShieldStatus() => WGCockpitStatusReading.Shield(_state);
}
