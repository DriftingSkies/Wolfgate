using Content.Client.Eui;
using Content.Shared._WF.Defcon;
using Content.Shared.Eui;

namespace Content.Client._WF.Defcon;

/// <summary>Client half of the DEFCON admin panel.</summary>
public sealed class WFDefconEui : BaseEui
{
    private readonly WFDefconWindow _window;

    public WFDefconEui()
    {
        _window = new WFDefconWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSetLevel += (level, locked, announce) => SendMessage(new WFDefconSetLevelMessage(level, locked, announce));
        _window.OnUnlock += () => SendMessage(new WFDefconUnlockMessage());
        _window.OnBias += bias => SendMessage(new WFDefconBiasMessage(bias));
        _window.OnEvaluate += () => SendMessage(new WFDefconEvaluateMessage());
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WFDefconEuiState defcon)
            _window.UpdateState(defcon);
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
