using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._WG.Announcements;
using Content.Shared.Administration;
using Content.Shared.Eui;

namespace Content.Server._WG.Announcements;

/// <summary>Server half of the roleplay announcement window.</summary>
public sealed partial class WGAnnounceEui : BaseEui
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    private string? _status;
    private bool _error;

    public WGAnnounceEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new WGAnnounceEuiState(_status, _error);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not WGAnnounceSendMessage send)
            return;

        if (!_admin.HasAdminFlag(Player, AdminFlags.Admin))
        {
            Close();
            return;
        }

        var error = _systems.GetEntitySystem<WGAnnounceSystem>().TrySend(Player, send.Request);
        _error = error != null;
        _status = Loc.GetString(error ?? "wg-announce-status-sent");
        StateDirty();

        if (error == null && send.CloseAfter)
            Close();
    }
}
