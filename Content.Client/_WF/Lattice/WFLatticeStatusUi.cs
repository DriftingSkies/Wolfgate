using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Fragments;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Lattice;

/// <summary>The Lattice status PDA app.</summary>
public sealed partial class WFLatticeStatusUi : UIFragment
{
    private WFLatticeStatusPanel? _panel;

    public override Control GetUIFragmentRoot()
    {
        return _panel!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _panel = new WFLatticeStatusPanel { Viewer = fragmentOwner };
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
    }
}

/// <summary>The Lattice status console's window.</summary>
public sealed class WFLatticeStatusBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WFLatticeStatusWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFLatticeStatusWindow>();
        _window.Panel.Viewer = Owner;
    }
}

/// <summary>A window round the status display.</summary>
public sealed class WFLatticeStatusWindow : FancyWindow
{
    public readonly WFLatticeStatusPanel Panel = new();

    public WFLatticeStatusWindow()
    {
        Title = Loc.GetString("wf-lattice-status-window");
        SetSize = new Vector2(720, 520);
        MinSize = new Vector2(420, 460);
        ContentsContainer.AddChild(Panel);
    }
}
