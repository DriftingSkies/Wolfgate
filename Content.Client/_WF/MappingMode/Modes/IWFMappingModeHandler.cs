using Robust.Shared.Input.Binding;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// One build mode's input. The pointer methods get both the press and the release; a press that returns false is
/// not claimed and falls through to the reference placement, the mapping tools and the placement manager, and its
/// release is not passed on.
/// </summary>
public interface IWFMappingModeHandler
{
    WFMappingBuildMode Mode { get; }

    /// <summary>Called when the mode becomes current.</summary>
    void Enter();

    /// <summary>Called when another mode takes over or the mapping screen is left.</summary>
    void Exit();

    bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args);

    bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args);

    bool Middle(in PointerInputCmdHandler.PointerInputCmdArgs args);

    /// <summary>The wheel over the viewport, plain or with Ctrl; true when the mode used it, false to zoom.</summary>
    bool Wheel(float delta);

    /// <summary>Escape; true when the mode dropped something of its own.</summary>
    bool Cancel();
}

/// <summary>A mode that follows the mouse every frame while it is current.</summary>
public interface IWFMappingFrameHandler
{
    void FrameUpdate();
}

/// <summary>A mode that shows cells it is about to change, drawn by <see cref="WFMappingShapeOverlay"/>.</summary>
public interface IWFMappingPreviewSource
{
    /// <summary>
    /// Fills <paramref name="cells"/> with grid-local cells to highlight on <paramref name="grid"/>, and gives a box
    /// of cells (right and top exclusive) to shade when the preview is a whole rectangle.
    /// </summary>
    bool TryGetPreview(List<Vector2i> cells, out EntityUid grid, out Box2i? box, out Color color);
}
