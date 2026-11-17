using Content.Shared._WG.ShipShields;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WG.ShipShields;

/// <summary>Uses the shared shield panel at an installed generator.</summary>
[UsedImplicitly]
public sealed class WGShipShieldGeneratorBoundUserInterface : BoundUserInterface
{
    private WGShipShieldGeneratorWindow? _window;

    /// <summary>Binds the panel to its generator.</summary>
    public WGShipShieldGeneratorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGShipShieldGeneratorWindow>();
        _window.ShieldPanel.AllocationRequested += (direction, concentration, arc) =>
            SendMessage(new WGShipShieldSetShuntMessage(direction, concentration, arc));
        _window.ShieldPanel.OnSetEnabled += enabled => SendMessage(new WGShipShieldSetEnabledMessage(enabled));
        _window.StrengthRequested += strength => SendMessage(new WGShipShieldSetStrengthMessage(strength));
        if (State is WGShipShieldGeneratorUiState state)
            Show(state);
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WGShipShieldGeneratorUiState shield)
            Show(shield);
    }

    private void Show(WGShipShieldGeneratorUiState state)
    {
        if (_window == null)
            return;

        _window.ShieldPanel.UpdateState(state.ShieldShunt, 0f);
        if (EntMan.HasComponent<WGShipShieldStrengthControlComponent>(Owner) && state.ShieldShunt.Stats is { } stats)
            _window.ShowStrength(stats.DamageLimit);
    }
}
