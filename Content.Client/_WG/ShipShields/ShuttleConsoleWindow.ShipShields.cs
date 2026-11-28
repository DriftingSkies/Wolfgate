using Content.Client._WG.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private WGShipShieldShuntScreen _shieldScreen = default!;
    private Angle? _wgShieldHelmRotation;
    private Button _shieldModeButton = default!;
    /// <summary>The pilot changes shield deployment.</summary>
    public event Action<bool>? ShieldEnabledRequested;
    /// <summary>The pilot vents the ship's heat.</summary>
    public event Action? ShieldVentRequested;
    /// <summary>The pilot commits a shield allocation.</summary>
    public event Action<float, float, float>? ShieldShuntRequested;

    private void WgShieldInitialize()
    {
        var button = _shieldModeButton = new Button
        {
            Text = Loc.GetString("wg-shield-helm-tab"), ToggleMode = true, Visible = false,
            HorizontalExpand = true, VerticalExpand = true, Group = NavModeButton.Group,
        };
        button.AddStyleClass("ButtonSquare");
        button.OnPressed += _ => SwitchMode(ShuttleConsoleMode.Shields);
        ModeButtons.AddChild(button);
        _shieldScreen = new WGShipShieldShuntScreen { Visible = false };
        _shieldScreen.AllocationRequested += (direction, concentration, arc) =>
            ShieldShuntRequested?.Invoke(direction, concentration, arc);
        _shieldScreen.OnSetEnabled += enabled => ShieldEnabledRequested?.Invoke(enabled);
        _shieldScreen.OnVent += () => ShieldVentRequested?.Invoke();
        Contents.AddChild(_shieldScreen);
    }

    /// <summary>Refreshes shield controls while retaining the latest navigation rotation.</summary>
    public void UpdateShieldShuntSnapshot(WGShipShieldShuntState state)
    {
        WgShieldUpdateState(state, _wgShieldHelmRotation);
    }

    private void WgShieldUpdateState(WGShipShieldShuntState? state, Angle? helmRotation)
    {
        _wgShieldHelmRotation = helmRotation;
        // A ship with no generator keeps the page for its heat and vent.
        _shieldModeButton.Visible = state is { Available: true } or { Hull: true };
        _shieldModeButton.Text = Loc.GetString(state is { Available: false, Hull: true } ? "wg-shield-helm-tab-heat" : "wg-shield-helm-tab");
        if (!_shieldModeButton.Visible && _mode == ShuttleConsoleMode.Shields && !WgCockpitActive)
        {
            NavModeButton.Pressed = true;
            SwitchMode(ShuttleConsoleMode.Nav);
        }
        _shieldScreen.UpdateState(state, (float) (helmRotation?.Theta ?? 0));
    }
}
