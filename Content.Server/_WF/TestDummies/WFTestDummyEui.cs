using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._WF.TestDummies;
using Content.Shared.Administration;
using Content.Shared.Eui;

namespace Content.Server._WF.TestDummies;

/// <summary>Server half of the test dummy window: sends calls and keeps the admin's list current.</summary>
public sealed partial class WFTestDummyEui : BaseEui
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    private WFTestDummySystem _system = default!;
    private string? _status;
    private bool _error;

    public WFTestDummyEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        _system = _systems.GetEntitySystem<WFTestDummySystem>();
        _system.Changed += StateDirty;
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        _system.Changed -= StateDirty;
    }

    public override EuiStateBase GetNewState()
    {
        var state = _system.Snapshot(_status, _error);
        _status = null;
        _error = false;
        return state;
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_admin.HasAdminFlag(Player, AdminFlags.Admin))
        {
            Close();
            return;
        }

        switch (msg)
        {
            case WFTestDummySendMessage send:
                var error = _system.TrySend(Player, send.Request, out var asked);
                _error = error != null;
                _status = error != null
                    ? Loc.GetString(error)
                    : Loc.GetString("wf-test-dummy-status-sent", ("count", asked));
                StateDirty();
                break;
            case WFTestDummyEndMessage:
                _system.End();
                _status = Loc.GetString("wf-test-dummy-status-ended");
                StateDirty();
                break;
        }
    }
}
