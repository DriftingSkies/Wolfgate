using Content.Client._WG.Cockpit;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private bool? _wgCockpitAutopilotActive;

    /// <summary>Updates only the authoritative autopilot lamp without disrupting the current MFD.</summary>
    public void WgUpdateCockpitAutopilot(bool? active) => _wgCockpitAutopilotActive = active;

    /// <summary>Builds the permanent status bank from the console's existing live telemetry.</summary>
    public Control WgCockpitStatus() => new WGCockpitStatusLights(
        ("wg-cockpit-status-autopilot", () => WGCockpitStatusReading.Autopilot(_wgCockpitAutopilotActive)),
        ("wg-cockpit-status-ftl", MapContainer.WgCockpitFtlStatus),
        ("wg-cockpit-status-damp", () => NavContainer.WgCockpitDampeningStatus(false)),
        ("wg-cockpit-status-park", () => NavContainer.WgCockpitDampeningStatus(true)),
        ("wg-cockpit-status-dock", DockContainer.WgCockpitDockedStatus),
        ("wg-cockpit-status-shield", _shieldScreen.WgCockpitShieldStatus));
}
