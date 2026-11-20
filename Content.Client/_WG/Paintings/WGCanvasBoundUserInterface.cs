using Content.Client.Crayon;
using Content.Shared._WG.Paintings;
using Content.Shared.Hands.EntitySystems;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Paintings;

public sealed partial class WGCanvasBoundUserInterface : BoundUserInterface
{
    [Dependency] private IPlayerManager _player = default!;

    [ViewVariables]
    private WGCanvasWindow? _window;

    public WGCanvasBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGCanvasWindow>();
        _window.GetTool = GetTool;
        _window.OnPaint += (pixels, erase) => SendMessage(new WGCanvasPaintMessage(pixels, erase));
        _window.OnPickColor += colour => SendMessage(new WGCanvasPickColorMessage(colour));
        _window.OnSwatch += (colour, remove) => SendMessage(new WGCanvasSwatchMessage(colour, remove));
        _window.OnFinish += (title, anonymous) => SendMessage(new WGCanvasFinishMessage(title, anonymous));
    }

    /// <summary>The held paint tool, the active hand first, as the server picks it.</summary>
    private WGPaintTool? GetTool()
    {
        if (_player.LocalEntity is not { } user)
            return null;

        var hands = EntMan.System<SharedHandsSystem>();
        if (hands.GetActiveItem(user) is { } active && Tool(active) is { } tool)
            return tool;

        foreach (var held in hands.EnumerateHeld(user))
        {
            if (Tool(held) is { } found)
                return found;
        }

        return null;
    }

    private WGPaintTool? Tool(EntityUid uid)
    {
        if (EntMan.TryGetComponent<WGPaintPaletteComponent>(uid, out var palette))
            return new WGPaintTool(palette.Selected, true, palette.Swatches, palette.Recent);
        if (EntMan.TryGetComponent<CrayonComponent>(uid, out var crayon))
            return new WGPaintTool(crayon.Color, false, Array.Empty<Color>(), Array.Empty<Color>());
        return null;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WGCanvasUiState canvas)
            _window?.UpdateState(canvas);
    }
}
