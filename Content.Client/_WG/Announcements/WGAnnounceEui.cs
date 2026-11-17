using Content.Client.Eui;
using Content.Shared._WG.Announcements;
using Content.Shared.Eui;

namespace Content.Client._WG.Announcements;

/// <summary>Client half of the roleplay announcement window.</summary>
public sealed class WGAnnounceEui : BaseEui
{
    private readonly WGAnnounceWindow _window;

    public WGAnnounceEui()
    {
        _window = new WGAnnounceWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSend += (request, closeAfter) => SendMessage(new WGAnnounceSendMessage(request, closeAfter));
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WGAnnounceEuiState announce)
            _window.SetStatus(announce.Status, announce.Error);
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
