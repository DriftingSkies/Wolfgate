using Content.Client._WG.Cockpit;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>Reads the existing navigation mode without depending on button visibility.</summary>
    public WGCockpitStatusReading WgCockpitDampeningStatus(bool parking) =>
        WGCockpitStatusReading.Dampening(_shuttleEntity == null ? null : NavRadar.DampeningMode, parking);
}
