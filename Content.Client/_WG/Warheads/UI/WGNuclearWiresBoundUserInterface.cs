using Content.Client.Wires.UI;
using Content.Shared.Wires;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Warheads.UI;

/// <summary>The stock wire panel, themed as a warhead's fuzing assembly.</summary>
public sealed class WGNuclearWiresBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WiresMenu? _menu;

    public WGNuclearWiresBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<WiresMenu>();
        _menu.WgApplyNuclearTheme();
        _menu.OnAction += (id, action) => SendMessage(new WiresActionMessage(id, action));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WiresBoundUserInterfaceState wires)
            _menu?.Populate(wires);
    }
}
