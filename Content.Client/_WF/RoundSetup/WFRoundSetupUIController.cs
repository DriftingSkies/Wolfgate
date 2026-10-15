using Content.Client.Gameplay;
using Content.Client.Lobby;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._WF.RoundSetup;

/// <summary>Opens round setup from the lobby and the in-round game menu.</summary>
public sealed class WFRoundSetupUIController : UIController, IOnStateExited<LobbyState>, IOnStateExited<GameplayState>
{
    private WFRoundSetupWindow? _window;

    /// <summary>Opens the browser or brings the existing setup window forward.</summary>
    public void OpenWindow()
    {
        if (_window is { IsOpen: true })
        {
            _window.MoveToFront();
            return;
        }

        var window = new WFRoundSetupWindow();
        _window = window;
        window.OnClose += () =>
        {
            if (_window == window)
                _window = null;
        };
        window.FitTo(UIManager.WindowRoot.Size);
        window.OpenCentered();
    }

    /// <summary>Starts with normal joining or player lobbies from the primary Join action.</summary>
    public void OpenJoin()
    {
        OpenWindow();
        _window?.ShowJoin();
    }

    /// <summary>Opens crew browsing or the player's existing crew only when requested.</summary>
    public void OpenCrews()
    {
        OpenWindow();
        _window?.ShowCrews();
    }

    /// <summary>Opens the scenario chooser from the main lobby's setup action.</summary>
    public void OpenCreate()
    {
        OpenWindow();
        _window?.OpenCreate();
    }

    /// <summary>Opens a selected lobby's details while preserving explicit admission choices.</summary>
    public void OpenLobby(int lobbyId)
    {
        OpenWindow();
        _window?.ShowLobby(lobbyId);
    }

    public void OnStateExited(LobbyState state) => _window?.Close();

    public void OnStateExited(GameplayState state) => _window?.Close();
}
