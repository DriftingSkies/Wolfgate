using System.Linq;
using Content.Client._WG.Cockpit;
using Content.Client._WG.CombatConsole;
using Content.Client._WG.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    /// <summary>Width of the shield column on the right of the gunnery console.</summary>
    public const float WgShieldPanelWidth = 196;

    private WGShipShieldShuntScreen _wgShieldScreen = default!;
    private Control _wgShieldPanel = default!;
    private Label? _wgShieldTitle;

    /// <summary>
    /// Builds the gunner's simplified shield control. It stays hidden until the console reports its ship; one with no
    /// shield generator shows only its heat and vent.
    /// </summary>
    private Control WgCreateShieldPanel()
    {
        _wgShieldScreen = new WGShipShieldShuntScreen();
        _wgShieldScreen.AllocationRequested += (direction, concentration, arc) =>
            CombatMessage?.Invoke(new WGShipShieldSetShuntMessage(direction, concentration, arc));
        _wgShieldScreen.OnSetEnabled += enabled => CombatMessage?.Invoke(new WGShipShieldSetEnabledMessage(enabled));
        _wgShieldScreen.OnVent += () => CombatMessage?.Invoke(new WGShipShieldVentMessage());

        var panel = WGInstrumentTheme.Panel("wg-shield-helm-tab", _wgShieldScreen.WgGunneryShield(), true);
        panel.Name = "WgGunneryShields";
        panel.SetWidth = WgShieldPanelWidth;
        panel.HorizontalExpand = false;
        panel.Visible = false;
        _wgShieldTitle = WGCockpitLease.Descendants(panel).OfType<Label>().FirstOrDefault();
        return _wgShieldPanel = panel;
    }

    /// <summary>Refreshes the gunner's shield control from the ship's authoritative shield state.</summary>
    public void WgUpdateShields(WGShipShieldShuntState state)
    {
        _wgShieldPanel.Visible = state.Available || state.Hull;
        if (_wgShieldTitle != null)
            _wgShieldTitle.Text = Loc.GetString(state.Available ? "wg-shield-helm-tab" : "wg-shield-helm-tab-heat");
        _wgShieldScreen.UpdateState(state, 0f);
    }
}
