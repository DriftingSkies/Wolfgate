using System.Linq;
using Content.Client._NF.LateJoin.Windows;
using Content.Client._WF.RoundSetup;
using Content.Shared._WF.RoundSetup;

namespace Content.Client.Lobby;

public sealed partial class LobbyState
{
    private WFRoundSetupClientSystem? _roundSetup;
    private PickerWindow? _normalJobs;

    private void InitializeRoundSetupHome()
    {
        _roundSetup = _entityManager.System<WFRoundSetupClientSystem>();
        _roundSetup.Received += RoundSetupChanged;
        _roundSetup.Send(new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
    }

    private void ShutdownRoundSetupHome()
    {
        if (_roundSetup != null)
            _roundSetup.Received -= RoundSetupChanged;
        _roundSetup = null;
        _openNormalJobsAfterLeaving = false;
        _normalJobs?.Close();
        _normalJobs = null;
    }

    private void RoundSetupChanged(WFRoundSetupState state)
    {
        UpdateRoundSetupHome();
        if (state.Message != null)
            _openNormalJobsAfterLeaving = false;
        if (!_openNormalJobsAfterLeaving || state.OwnLobby != null)
            return;
        _openNormalJobsAfterLeaving = false;
        OpenNormalJobs();
    }

    /// <summary>Shows the standard join action or a return to the player's current crew.</summary>
    private void UpdateRoundSetupHome()
    {
        if (Lobby == null)
            return;
        var state = _roundSetup?.State;
        var ownLobby = state?.Lobbies.FirstOrDefault(lobby => lobby.Id == state.OwnLobby);
        var own = ownLobby != null;
        var user = IoCManager.Resolve<Robust.Client.Player.IPlayerManager>().LocalSession?.UserId;
        var waiting = _gameTicker.IsGameStarted && ownLobby?.Members.Any(member => member.UserId == user && member.Ready) == true;
        var ready = !_gameTicker.IsGameStarted && _gameTicker.AreWeReady;
        var action = _gameTicker.IsGameStarted ? "jobs" : ready ? "unready" : "ready";
        var help = ready ? "ready-help" : waiting ? "waiting-help" : own ? "member-help" : _gameTicker.IsGameStarted ? "jobs-help" : "normal-help";
        Lobby.ReadyButton.ToggleMode = false;
        Lobby.ReadyButton.Pressed = false;
        Lobby.ReadyButton.Disabled = _gameTicker.IsGameStarted && _gameTicker.DisallowedLateJoin;
        Lobby.ReadyButton.RemoveStyleClass("ButtonColorGreen");
        Lobby.ReadyButton.RemoveStyleClass("ButtonColorRed");
        Lobby.ReadyButton.AddStyleClass(ready || _gameTicker.IsGameStarted ? "ButtonColorGreen" : "ButtonColorRed");
        Lobby.SetRoundSetupJoinText(Loc.GetString($"wf-round-setup-home-{action}"), Loc.GetString($"wf-round-setup-home-{help}"));
        Lobby.RoundSetupButton.Visible = true;
        Lobby.RoundSetupButton.Disabled = !own && _roundSetup?.State is not { CanJoin: true };
        Lobby.RoundSetupButton.Text = Loc.GetString(_gameTicker.IsGameStarted
            ? "wf-round-setup-home-join-crew"
            : "wf-round-setup-home-setup");
    }

    /// <summary>Readies normal players directly and routes crew members to their existing preparation.</summary>
    private void JoinFromRoundSetupHome()
    {
        if (!_gameTicker.IsGameStarted)
        {
            if (_roundSetup?.State?.OwnLobby is { } own)
            {
                if (_gameTicker.AreWeReady)
                    _roundSetup.Send(new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = own, Ready = false });
                else
                    _userInterfaceManager.GetUIController<WFRoundSetupUIController>().OpenLobby(own);
            }
            else
                SetReady(!_gameTicker.AreWeReady);
            return;
        }
        // The normal picker must not inherit a crew's spawn restrictions.
        if (_roundSetup?.State?.OwnLobby is { } crew)
        {
            _openNormalJobsAfterLeaving = true;
            _roundSetup.Send(new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = crew });
            return;
        }
        OpenNormalJobs();
    }

    private bool _openNormalJobsAfterLeaving;

    private void OpenNormalJobs()
    {
        if (_gameTicker.DisallowedLateJoin)
            return;
        if (_normalJobs is { IsOpen: true })
        {
            _normalJobs.MoveToFront();
            return;
        }
        _normalJobs = new PickerWindow();
        _normalJobs.OpenNormalJobs();
    }
}
