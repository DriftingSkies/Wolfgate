using System.Linq;
using Content.Client._NF.LateJoin.Windows;
using Content.Client._WG.RoundSetup;
using Content.Shared._WG.RoundSetup;

namespace Content.Client.Lobby;

public sealed partial class LobbyState
{
    private WGRoundSetupClientSystem? _roundSetup;
    private PickerWindow? _stationRoles;
    private WGRoundJoinChoiceWindow? _joinChoice;

    private void InitializeRoundSetupHome()
    {
        _roundSetup = _entityManager.System<WGRoundSetupClientSystem>();
        _roundSetup.Received += RoundSetupChanged;
        _roundSetup.Send(new WGRoundSetupRequest(WGRoundSetupAction.Refresh));
    }

    private void ShutdownRoundSetupHome()
    {
        if (_roundSetup != null)
            _roundSetup.Received -= RoundSetupChanged;
        _roundSetup = null;
        _joinStationAfterLeaving = false;
        _joinChoice?.Close();
        _joinChoice = null;
        _stationRoles?.Close();
        _stationRoles = null;
    }

    private void RoundSetupChanged(WGRoundSetupState state)
    {
        UpdateRoundSetupHome();
        if (state.Message != null)
            _joinStationAfterLeaving = false;
        if (!_joinStationAfterLeaving || state.OwnLobby != null)
            return;
        _joinStationAfterLeaving = false;
        CompleteStationJoin();
    }

    /// <summary>Shows the join action or a return to the player's current crew.</summary>
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
        var help = ready ? "ready-help" : !_gameTicker.IsGameStarted ? "ready-choice-help" : waiting ? "waiting-help" : own ? "member-help" : "jobs-help";
        Lobby.ReadyButton.ToggleMode = false;
        Lobby.ReadyButton.Pressed = false;
        Lobby.ReadyButton.Disabled = _gameTicker.IsGameStarted && _gameTicker.DisallowedLateJoin;
        Lobby.ReadyButton.RemoveStyleClass("ButtonColorGreen");
        Lobby.ReadyButton.RemoveStyleClass("ButtonColorRed");
        Lobby.ReadyButton.AddStyleClass(ready || _gameTicker.IsGameStarted ? "ButtonColorGreen" : "ButtonColorRed");
        Lobby.SetRoundSetupJoinText(Loc.GetString($"wg-round-setup-home-{action}"), Loc.GetString($"wg-round-setup-home-{help}"));
        Lobby.RoundSetupButton.Visible = !_gameTicker.IsGameStarted;
        Lobby.RoundSetupButton.Disabled = !own && _roundSetup?.State is not { CanJoin: true };
        if (Lobby.ReadyButton.Disabled)
            Lobby.ReadyButton.ToolTip = Loc.GetString("wg-round-setup-disabled-join");
        Lobby.RoundSetupButton.ToolTip = Lobby.RoundSetupButton.Disabled ? Loc.GetString("wg-round-setup-disabled-join") :
            Loc.GetString("wg-round-setup-home-member-help");
        Lobby.RoundSetupButton.Text = Loc.GetString(_gameTicker.IsGameStarted
            ? "wg-round-setup-home-join-crew"
            : "wg-round-setup-home-setup");
    }

    /// <summary>Offers crew setup first; cancelling existing readiness remains a single action.</summary>
    private void JoinFromRoundSetupHome()
    {
        if (!_gameTicker.IsGameStarted && _gameTicker.AreWeReady)
        {
            if (_roundSetup?.State?.OwnLobby is { } own)
                _roundSetup.Send(new WGRoundSetupRequest(WGRoundSetupAction.SetReady) { LobbyId = own, Ready = false });
            else
                SetReady(false);
            return;
        }
        if (_joinChoice is { IsOpen: true })
        {
            _joinChoice.MoveToFront();
            return;
        }
        _joinChoice = new WGRoundJoinChoiceWindow(
            () => _userInterfaceManager.GetUIController<WGRoundSetupUIController>().OpenCrews(), JoinStationRoles, pregame: !_gameTicker.IsGameStarted);
        _joinChoice.OpenCentered();
    }

    private void JoinStationRoles()
    {
        if (_gameTicker.IsGameStarted && _gameTicker.DisallowedLateJoin)
            return;
        // Wait for the server to release crew membership before choosing station roles.
        if (_roundSetup?.State?.OwnLobby is { } crew)
        {
            _joinStationAfterLeaving = true;
            _roundSetup.Send(new WGRoundSetupRequest(WGRoundSetupAction.Leave) { LobbyId = crew });
            return;
        }
        CompleteStationJoin();
    }

    private bool _joinStationAfterLeaving;

    private void CompleteStationJoin()
    {
        if (_gameTicker.IsGameStarted)
            OpenStationRoles();
        else
            SetReady(true);
    }

    private void OpenStationRoles()
    {
        if (_gameTicker.DisallowedLateJoin)
            return;
        if (_stationRoles is { IsOpen: true })
        {
            _stationRoles.MoveToFront();
            return;
        }
        _stationRoles = new PickerWindow();
        _stationRoles.OpenStationRoles();
    }
}
