using System.Linq;
using Content.Client.Lobby;
using Content.Shared._WF.Census;
using JetBrains.Annotations;
using Robust.Client.State;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;

namespace Content.Client._WF.Census;

/// <summary>
/// Keeps the open censuses the server sent, opens the census window from the Census buttons, and opens it by
/// itself when the server says so, which it only does in the lobby.
/// </summary>
[UsedImplicitly]
public sealed class CensusUIController : UIController, IOnSystemChanged<CensusSystem>
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IStateManager _state = default!;

    private CensusSystem? _system;
    private CensusWindow? _window;
    private List<CensusEntry> _censuses = new();
    private bool _received;

    /// <summary>Raised when the open censuses or the player's answers change, for the buttons' count.</summary>
    public event Action? Changed;

    /// <summary>How many open censuses the player has yet to answer.</summary>
    public int Unanswered => _censuses.Count(c => !c.Answered);

    public bool Enabled => _cfg.GetCVar(CensusCVars.Enabled);

    public void OnSystemLoaded(CensusSystem system)
    {
        _system = system;
        system.List += OnList;
        system.Answered += OnAnswered;
    }

    public void OnSystemUnloaded(CensusSystem system)
    {
        system.List -= OnList;
        system.Answered -= OnAnswered;
        _system = null;
        _censuses = new List<CensusEntry>();
        _received = false;
        _window?.Close();
        Changed?.Invoke();
    }

    /// <summary>Opens the census window, or closes it if it is open.</summary>
    public void ToggleWindow()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        OpenWindow();
    }

    private void OpenWindow()
    {
        if (_window != null)
        {
            _window.MoveToFront();
            return;
        }

        _window = new CensusWindow();
        _window.OnClose += () => _window = null;
        _window.Submitted += answers => _system?.Submit(answers);
        if (_received)
            _window.SetCensuses(_censuses);
        _window.OpenCentered();
        _system?.RequestList();
    }

    private void OnList(CensusListEvent ev)
    {
        _censuses = ev.Censuses;
        _received = true;
        _window?.SetCensuses(_censuses);
        Changed?.Invoke();

        // The server only prompts in the lobby; the state check keeps a late message from opening it in a round.
        if (ev.Prompt && Enabled && _state.CurrentState is LobbyState)
            OpenWindow();
    }

    private void OnAnswered(CensusResultEvent ev)
    {
        _window?.ShowResult(ev);
    }
}
