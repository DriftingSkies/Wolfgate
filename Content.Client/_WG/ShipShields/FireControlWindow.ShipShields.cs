using Content.Client._WG.CombatConsole;
using Content.Client._WG.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Client.UserInterface;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    /// <summary>Width of the shield column on the right of the gunnery console.</summary>
    public const float WgShieldPanelWidth = 196;

    private WGShipShieldShuntScreen _wgShieldScreen = default!;
    private Control _wgShieldPanel = default!;

    /// <summary>
    /// Builds the gunner's simplified shield control. It stays hidden until the ship reports a shield generator.
    /// </summary>
    private Control WgCreateShieldPanel()
    {
        _wgShieldScreen = new WGShipShieldShuntScreen();
        _wgShieldScreen.AllocationRequested += (direction, concentration, arc) =>
            CombatMessage?.Invoke(new WGShipShieldSetShuntMessage(direction, concentration, arc));
        _wgShieldScreen.OnSetEnabled += enabled => CombatMessage?.Invoke(new WGShipShieldSetEnabledMessage(enabled));

        var panel = WGInstrumentTheme.Panel("wg-shield-helm-tab", _wgShieldScreen.WgGunneryShield(), true);
        panel.Name = "WgGunneryShields";
        panel.SetWidth = WgShieldPanelWidth;
        panel.HorizontalExpand = false;
        panel.Visible = false;
        return _wgShieldPanel = panel;
    }

    /// <summary>Refreshes the gunner's shield control from the ship's authoritative shield state.</summary>
    public void WgUpdateShields(WGShipShieldShuntState state)
    {
        _wgShieldPanel.Visible = state.Available;
        _wgShieldScreen.UpdateState(state, 0f);
    }
}
