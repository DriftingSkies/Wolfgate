using Content.Client.Eui;
using Content.Shared._WF.Announcements;
using Content.Shared.Eui;

namespace Content.Client._WF.Announcements;

/// <summary>Client half of the roleplay announcement window.</summary>
public sealed class WFAnnounceEui : BaseEui
{
    private readonly WFAnnounceWindow _window;

    public WFAnnounceEui()
    {
        _window = new WFAnnounceWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSend += (request, closeAfter) => SendMessage(new WFAnnounceSendMessage(request, closeAfter));
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WFAnnounceEuiState announce)
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
