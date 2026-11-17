using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server.Silicons.Borgs;

public sealed partial class BorgSystem
{
    /// <summary>
    /// Refreshes the cyborg's window after a charge change, but only while it is open and only if the charge it shows
    /// (whole percent) or the battery's presence changed. Any new state sends every UI state on the cyborg to everyone
    /// who can see it.
    /// </summary>
    private void WgUpdateChargeUi(EntityUid uid, BorgChassisComponent component)
    {
        if (!_ui.IsUiOpen(uid, BorgUiKey.Key))
            return;

        var hasBattery = false;
        var percent = 0;
        if (_powerCell.TryGetBatteryFromSlot(uid, out var battery))
        {
            hasBattery = true;
            percent = WgShownPercent(battery.CurrentCharge / battery.MaxCharge);
        }

        if (_ui.TryGetUiState<BorgBuiState>(uid, BorgUiKey.Key, out var shown)
            && shown.HasBattery == hasBattery
            && WgShownPercent(shown.ChargePercent) == percent)
            return;

        UpdateUI(uid, component);
    }

    /// <summary>The whole percent the window's charge label shows for a charge fraction.</summary>
    private static int WgShownPercent(float fraction)
    {
        return (int) MathF.Round(fraction * 100);
    }
}
