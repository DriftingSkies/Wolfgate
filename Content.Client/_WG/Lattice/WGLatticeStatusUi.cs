using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Fragments;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Lattice;

/// <summary>The Lattice status PDA app.</summary>
public sealed partial class WGLatticeStatusUi : UIFragment
{
    private WGLatticeStatusPanel? _panel;

    public override Control GetUIFragmentRoot()
    {
        return _panel!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _panel = new WGLatticeStatusPanel { Viewer = fragmentOwner };
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
    }
}

/// <summary>The Lattice status console's window.</summary>
public sealed class WGLatticeStatusBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WGLatticeStatusWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGLatticeStatusWindow>();
        _window.Panel.Viewer = Owner;
    }
}

/// <summary>A window round the status display.</summary>
public sealed class WGLatticeStatusWindow : FancyWindow
{
    public readonly WGLatticeStatusPanel Panel = new();

    public WGLatticeStatusWindow()
    {
        Title = Loc.GetString("wg-lattice-status-window");
        SetSize = new Vector2(720, 520);
        MinSize = new Vector2(420, 460);
        ContentsContainer.AddChild(Panel);
    }
}
