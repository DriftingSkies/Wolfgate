using Content.Client.Gameplay;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._WF.SectorControl;

/// <summary>Opens and closes the sector map window, from the game menu and wf_sectormap; it closes when the player leaves the game.</summary>
public sealed class WFSectorMapUIController : UIController, IOnStateExited<GameplayState>
{
    private WFSectorMapWindow? _window;

    /// <summary>The open window, if any.</summary>
    public WFSectorMapWindow? Window => _window;

    /// <summary>Opens the sector map, or closes it if it is open.</summary>
    public void ToggleWindow()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        OpenWindow();
    }

    /// <summary>Opens the sector map, or brings it to the front if it is open.</summary>
    public void OpenWindow()
    {
        if (_window != null)
        {
            _window.MoveToFront();
            return;
        }

        _window = new WFSectorMapWindow();
        _window.OnClose += () => _window = null;
        _window.FitTo(UIManager.WindowRoot.Size);
        _window.OpenCentered();
    }

    public void OnStateExited(GameplayState state)
    {
        _window?.Close();
    }
}
