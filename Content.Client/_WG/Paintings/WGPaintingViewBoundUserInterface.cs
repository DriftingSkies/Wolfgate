using Content.Shared._WG.Paintings;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Paintings;

/// <summary>Shows a framed painting up close.</summary>
public sealed class WGPaintingViewBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WGPaintingViewWindow? _window;

    public WGPaintingViewBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGPaintingViewWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WGCanvasUiState painting)
            _window?.UpdateState(painting);
    }
}
