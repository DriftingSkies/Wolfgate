using Content.Client.Eui;
using Content.Shared._WG.TestDummies;
using Content.Shared.Eui;

namespace Content.Client._WG.TestDummies;

/// <summary>Client half of the test dummy window.</summary>
public sealed class WGTestDummyEui : BaseEui
{
    private readonly WGTestDummyWindow _window;

    public WGTestDummyEui()
    {
        _window = new WGTestDummyWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSend += request => SendMessage(new WGTestDummySendMessage(request));
        _window.OnEnd += () => SendMessage(new WGTestDummyEndMessage());
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WGTestDummyEuiState dummies)
            _window.Update(dummies);
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }
}
