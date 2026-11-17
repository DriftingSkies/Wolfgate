using System.Linq;
using Content.Shared._WG.Drydock;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Drydock;

/// <summary>
/// The drydock terminal's window, and a berth console window for each berth opened from it. The berth windows send the
/// berth console's own messages, stamped with their berth, through the terminal; they close with the terminal.
/// </summary>
public sealed class WGDrydockTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WGDrydockTerminalWindow? _window;

    private readonly Dictionary<NetEntity, WGDrydockConsoleWindow> _berths = new();
    private WGDrydockTerminalState? _state;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGDrydockTerminalWindow>();
        _window.OnSend += SendMessage;
        _window.OnOpenBerth += OpenBerth;
        SendMessage(new WGDrydockTerminalRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is not WGDrydockTerminalStateMessage state)
            return;

        _state = state.State;
        _window?.UpdateState(_state);

        foreach (var (net, window) in _berths.ToArray())
        {
            var berth = _state.Berths.Find(b => b.Console == net);
            if (berth == null)
            {
                window.Close();
                continue;
            }

            Show(window, berth);
        }
    }

    private void OpenBerth(NetEntity console)
    {
        if (_berths.TryGetValue(console, out var open))
        {
            open.MoveToFront();
            return;
        }

        if (_state?.Berths.Find(b => b.Console == console) is not { } berth)
            return;

        var window = new WGDrydockConsoleWindow();
        window.OnRepairPressed += () => SendMessage(new WGDrydockRepairMessage { Berth = console });
        window.OnBreakPressed += () => SendMessage(new WGDrydockBreakMessage { Berth = console });
        window.OnCleanDriftPressed += () => SendMessage(new WGDrydockCleanDriftMessage { Berth = console });
        window.OnUpgradePressed += upgrade => SendMessage(new WGDrydockUpgradeMessage(upgrade) { Berth = console });
        window.OnBerthLightsPressed += () => SendMessage(new WGDrydockToggleBerthLightsMessage { Berth = console });
        window.OnClose += () => _berths.Remove(console);
        _berths[console] = window;

        Show(window, berth);
        window.OpenCentered();
    }

    private static void Show(WGDrydockConsoleWindow window, WGDrydockTerminalBerth berth)
    {
        window.UpdateState(berth.State);
        window.Title = Loc.GetString("drydock-terminal-berth-title", ("berth", berth.Label));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        foreach (var window in _berths.Values.ToArray())
            window.Close();
        _berths.Clear();
    }
}
