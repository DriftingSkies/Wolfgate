using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Colonies;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Colonies;

/// <summary>Opens the charter office: the world's development, and for the governing company its charter.</summary>
public sealed class WFColonyOfficeBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFColonyOfficeWindow? _window;

    public WFColonyOfficeBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFColonyOfficeWindow>();
        var panel = _window.Panel;
        panel.OnDefence += engage => SendMessage(new WFColonyDefenceMessage(engage));
        panel.OnOpen += open => SendMessage(new WFColonyOpenMessage(open));
        panel.OnPermit += (holder, granted) => SendMessage(new WFColonyPermitMessage(holder, granted));
        panel.OnBerth += (holder, open) => SendMessage(new WFColonyBerthMessage(holder, open));
        panel.OnYardKit += () => SendMessage(new WFColonyYardKitMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WFColonyOfficeState state)
            _window?.Panel.UpdateState(state.View);
    }
}

/// <summary>The charter office's window: the Development panel of an outpost console, standing on its own.</summary>
public sealed class WFColonyOfficeWindow : FancyWindow
{
    public readonly WFColonyDevelopmentPanel Panel = new() { VerticalExpand = true, ShowOutput = false };

    public WFColonyOfficeWindow()
    {
        Title = Loc.GetString("wf-colony-office-title");
        MinSize = new Vector2i(520, 420);
        SetSize = new Vector2i(640, 600);
        ContentsContainer.AddChild(Panel);
    }
}
