using Content.Client.Eui;
using Content.Shared._WG.Paintings;
using Content.Shared.Eui;

namespace Content.Client._WG.Paintings;

/// <summary>Client half of the admin painting browser.</summary>
public sealed class WGPaintingAdminEui : BaseEui
{
    private readonly WGPaintingAdminWindow _window;

    public WGPaintingAdminEui()
    {
        _window = new WGPaintingAdminWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSearch += (query, filter) => SendMessage(new WGPaintingAdminSearchMessage(query, filter));
        _window.OnSelect += hash => SendMessage(new WGPaintingAdminSelectMessage(hash));
        _window.OnBlock += (hash, blocked) => SendMessage(new WGPaintingAdminBlockMessage(hash, blocked));
        _window.OnDelete += hash => SendMessage(new WGPaintingAdminDeleteMessage(hash));
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WGPaintingAdminEuiState cast)
            _window.UpdateState(cast);
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
        _window.DisposePreview();
    }
}
