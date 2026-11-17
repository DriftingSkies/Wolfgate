using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.Bench;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Bench;

/// <summary>The Bench terminal's window. Its state comes as a message to this viewer alone.</summary>
public sealed class WGBenchTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WGBenchTerminalWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGBenchTerminalWindow>();
        _window.Panel.OnAction += action => SendMessage(new WGBenchTerminalMessage(action));
        SendMessage(new WGBenchTerminalMessage(new WGBenchRefreshAction()));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WGBenchTerminalStateMessage state)
            _window?.Panel.UpdateState(state.State);
    }
}

/// <summary>The terminal's window around the Bench panel.</summary>
public sealed class WGBenchTerminalWindow : FancyWindow
{
    public readonly WGBenchPanel Panel = new();

    public WGBenchTerminalWindow()
    {
        Title = Loc.GetString("wg-bench-terminal-title");
        SetSize = new Vector2(640, 680);
        MinSize = new Vector2(480, 420);
        ContentsContainer.AddChild(Panel);
    }
}
