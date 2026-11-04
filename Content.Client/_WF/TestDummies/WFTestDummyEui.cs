using Content.Client.Eui;
using Content.Shared._WF.TestDummies;
using Content.Shared.Eui;

namespace Content.Client._WF.TestDummies;

/// <summary>Client half of the test dummy window.</summary>
public sealed class WFTestDummyEui : BaseEui
{
    private readonly WFTestDummyWindow _window;

    public WFTestDummyEui()
    {
        _window = new WFTestDummyWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSend += request => SendMessage(new WFTestDummySendMessage(request));
        _window.OnEnd += () => SendMessage(new WFTestDummyEndMessage());
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WFTestDummyEuiState dummies)
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
