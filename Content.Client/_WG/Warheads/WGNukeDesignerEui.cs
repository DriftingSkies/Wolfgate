using Content.Client.Eui;
using Content.Shared._WG.Warheads;
using Content.Shared.Eui;

namespace Content.Client._WG.Warheads;

/// <summary>Client half of the nuke designer.</summary>
public sealed class WGNukeDesignerEui : BaseEui
{
    private readonly WGNukeDesignerWindow _window;

    public WGNukeDesignerEui()
    {
        _window = new WGNukeDesignerWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSpawn += design => SendMessage(new WGNukeDesignerSpawnMessage(design));
        _window.OnDetonate += payload => SendMessage(new WGNukeDesignerDetonateMessage(payload));
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WGNukeDesignerEuiState designer)
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
