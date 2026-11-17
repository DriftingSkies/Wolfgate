using Content.Client.Eui;
using Content.Shared._WG.Defcon;
using Content.Shared.Eui;

namespace Content.Client._WG.Defcon;

/// <summary>Client half of the DEFCON admin panel.</summary>
public sealed class WGDefconEui : BaseEui
{
    private readonly WGDefconWindow _window;

    public WGDefconEui()
    {
        _window = new WGDefconWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSetLevel += (level, locked, announce) => SendMessage(new WGDefconSetLevelMessage(level, locked, announce));
        _window.OnUnlock += () => SendMessage(new WGDefconUnlockMessage());
        _window.OnBias += bias => SendMessage(new WGDefconBiasMessage(bias));
        _window.OnEvaluate += () => SendMessage(new WGDefconEvaluateMessage());
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WGDefconEuiState defcon)
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
