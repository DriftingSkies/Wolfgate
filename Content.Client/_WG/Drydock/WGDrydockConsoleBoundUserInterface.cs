using Content.Shared._WG.Drydock;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Drydock;

/// <summary>
/// Client UI for the drydock berth console: its shared state, and this viewer's own prices as a message.
/// </summary>
public sealed class WGDrydockConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WGDrydockConsoleWindow? _window;

    public WGDrydockConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WGDrydockConsoleWindow>();
        _window.OnRepairPressed += OnRepairPressed;
        _window.OnBreakPressed += () => SendMessage(new WGDrydockBreakMessage());
        _window.OnCleanDriftPressed += () => SendMessage(new WGDrydockCleanDriftMessage());
        _window.OnUpgradePressed += OnUpgradePressed;
        _window.OnCancelBookingPressed += OnCancelBookingPressed;
        _window.OnBerthLightsPressed += OnBerthLightsPressed;
        SendMessage(new WGDrydockQuoteRequestMessage());
    }

    private void OnBerthLightsPressed()
    {
        SendMessage(new WGDrydockToggleBerthLightsMessage());
    }

    private void OnRepairPressed()
    {
        SendMessage(new WGDrydockRepairMessage());
    }

    private void OnUpgradePressed(string upgrade)
    {
        SendMessage(new WGDrydockUpgradeMessage(upgrade));
    }

    private void OnCancelBookingPressed()
    {
        SendMessage(new WGDrydockCancelBookingMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not WGDrydockConsoleState castState)
            return;

        _window?.UpdateState(castState);
    }

    /// <summary>Takes this viewer's own prices, which the console sends apart from its shared state.</summary>
    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is WGDrydockQuoteMessage quote)
            _window?.UpdateQuote(quote.Quote);
    }
}
