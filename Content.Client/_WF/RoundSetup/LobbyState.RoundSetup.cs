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
    }

    private void ShutdownRoundSetupHome()
    {
        if (_roundSetup != null)
            _roundSetup.Received -= RoundSetupChanged;
        _roundSetup = null;
        _normalJobs?.Close();
        _normalJobs = null;
    }

    private void RoundSetupChanged(WFRoundSetupState state) => UpdateRoundSetupHome();

    /// <summary>Shows the standard join action or a return to the player's current crew.</summary>
    private void UpdateRoundSetupHome()
    {
        if (Lobby == null)
            return;
        var own = _roundSetup?.State?.OwnLobby != null;
        var ready = !_gameTicker.IsGameStarted && _gameTicker.AreWeReady;
        var action = own ? "manage" : _gameTicker.IsGameStarted ? "jobs" : ready ? "unready" : "ready";
        var help = own ? "member-help" : _gameTicker.IsGameStarted ? "jobs-help" : ready ? "ready-help" : "normal-help";
        Lobby.ReadyButton.ToggleMode = false;
        Lobby.ReadyButton.Pressed = false;
        Lobby.ReadyButton.Disabled = !own && _gameTicker.IsGameStarted && _gameTicker.DisallowedLateJoin;
        Lobby.SetRoundSetupJoinText(Loc.GetString($"wf-round-setup-home-{action}"), Loc.GetString($"wf-round-setup-home-{help}"));
        Lobby.RoundSetupButton.Visible = true;
        Lobby.RoundSetupButton.Disabled = own || _roundSetup?.State is not { CanJoin: true };
        Lobby.RoundSetupButton.ToolTip = own ? Loc.GetString("wf-round-setup-home-already-member") : null;
    }

    /// <summary>Readies normal players directly and routes crew members to their existing preparation.</summary>
    private void JoinFromRoundSetupHome()
    {
        if (_roundSetup?.State?.OwnLobby is { } own)
        {
            _userInterfaceManager.GetUIController<WFRoundSetupUIController>().OpenLobby(own);
            return;
        }
        if (!_gameTicker.IsGameStarted)
        {
            SetReady(!_gameTicker.AreWeReady);
            return;
        }
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
