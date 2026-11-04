using Content.Client.Eui;
using Content.Shared._WF.Warheads;
using Content.Shared.Eui;

namespace Content.Client._WF.Warheads;

/// <summary>Client half of the nuke designer.</summary>
public sealed class WFNukeDesignerEui : BaseEui
{
    private readonly WFNukeDesignerWindow _window;

    public WFNukeDesignerEui()
    {
        _window = new WFNukeDesignerWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSpawn += design => SendMessage(new WFNukeDesignerSpawnMessage(design));
        _window.OnDetonate += payload => SendMessage(new WFNukeDesignerDetonateMessage(payload));
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WFNukeDesignerEuiState designer)
            _window.Update(designer);
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
