using System.Numerics;
using Content.Client._WF.MappingMode.Modes;
using Content.Shared._WF.MappingMode;
using Robust.Client.Input;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;

namespace Content.Client._WF.MappingMode.Template;

/// <summary>
/// Template mode: left drag moves the template, Ctrl+drag scales it about its centre, Shift+drag turns it, the wheel
/// scales it and the arrows nudge it by an image pixel (eight with Shift). Right click or Escape drops a drag.
/// </summary>
public sealed partial class WFMappingTemplateMode : WFMappingModeHandlerBase
{
    [Dependency] private IInputManager _input = default!;

    /// <summary>Scale change per wheel notch.</summary>
    private const float WheelStep = 1.05f;

    /// <summary>A scale drag starting nearer the centre than this, in tiles, does nothing.</summary>
    private const float MinScaleRadius = 0.05f;

    private enum DragKind : byte
    {
        None,
        Move,
        Scale,
        Rotate,
    }

    private readonly WFMappingTemplateSystem _template;
    private readonly WFMappingTemplateSettings _start = new();
    private DragKind _drag;
    private Vector2 _pressMouse;

    public WFMappingTemplateMode(WFMappingBuildModeSystem modes, WFMappingTemplateSystem template) : base(modes)
    {
        _template = template;
    }

    public override WFMappingBuildMode Mode => WFMappingBuildMode.Template;

    public bool Dragging => _drag != DragKind.None;

    public override void Enter()
    {
        var context = _input.Contexts.GetContext("common");
        foreach (var function in WFMappingKeyFunctions.Nudges)
        {
            if (!context.FunctionExists(function))
                context.AddFunction(function);
        }
    }

    public override void Exit()
    {
        EndDrag();
        var context = _input.Contexts.GetContext("common");
        foreach (var function in WFMappingKeyFunctions.Nudges)
        {
            if (context.FunctionExists(function))
                context.RemoveFunction(function);
        }
    }

    public override bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        var position = Modes.ToMap(args.Coordinates);
        if (!IsDown(args))
        {
            if (!Dragging)
                return true;

            if (position.MapId != MapId.Nullspace)
                Apply(position.Position);

            EndDrag();
            return true;
        }

        _drag = DragKind.None;
        if (!CanChange() || position.MapId == MapId.Nullspace)
            return true;

        _start.CopyFrom(_template.Settings);
        _pressMouse = position.Position;
        _drag = _input.IsKeyDown(Keyboard.Key.Control)
            ? DragKind.Scale
            : _input.IsKeyDown(Keyboard.Key.Shift)
                ? DragKind.Rotate
                : DragKind.Move;
        return true;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (!IsDown(args))
            return true;

        return CancelDrag();
    }

    public override bool Wheel(float delta)
    {
        if (!_template.HasTemplate || _template.Settings.Locked)
            return false;

        var factor = MathF.Pow(WheelStep, delta);
        var settings = _template.Settings;
        settings.ScaleX *= factor;
        settings.ScaleY *= factor;
        _template.Modified();
        return true;
    }

    public override bool Cancel()
    {
        return CancelDrag();
    }

    /// <summary>Follows the mouse while a drag is held; called every frame by the template system.</summary>
    public void UpdateDrag()
    {
        var mouse = Modes.MouseMapPosition;
        if (Dragging && mouse.MapId != MapId.Nullspace)
            Apply(mouse.Position);
    }

    /// <summary>Drops a drag and puts the template back where it started; false when nothing was held.</summary>
    public bool CancelDrag()
    {
        if (!Dragging)
            return false;

        _drag = DragKind.None;
        _template.Settings.CopyFrom(_start);
        _template.Modified();
        return true;
    }

    /// <summary>Moves the template by one image pixel along an axis, eight with Shift.</summary>
    public bool Nudge(int x, int y)
    {
        if (!Modes.Active || Modes.Mode != WFMappingBuildMode.Template || !CanChange())
            return false;

        var settings = _template.Settings;
        var step = (_input.IsKeyDown(Keyboard.Key.Shift) ? 8f : 1f) / settings.PixelsPerTile;
        settings.Position += new Vector2(x * settings.ScaleX, y * settings.ScaleY) * step;
        _template.Modified();
        return true;
    }

    private bool CanChange()
    {
        if (!_template.HasTemplate)
        {
            _template.Popup("wf-mapping-template-none");
            return false;
        }

        if (_template.Settings.Locked)
        {
            _template.Popup("wf-mapping-template-is-locked");
            return false;
        }

        return true;
    }

    private void EndDrag()
    {
        if (!Dragging)
            return;

        _drag = DragKind.None;
        _template.Modified();
    }

    private void Apply(Vector2 mouse)
    {
        var settings = _template.Settings;
        var centre = _start.Position;
        switch (_drag)
        {
            case DragKind.Move:
                settings.Position = centre + (mouse - _pressMouse);
                break;
            case DragKind.Scale:
                var from = (_pressMouse - centre).Length();
                if (from < MinScaleRadius)
                    return;

                var factor = (mouse - centre).Length() / from;
                settings.ScaleX = _start.ScaleX * factor;
                settings.ScaleY = _start.ScaleY * factor;
                break;
            case DragKind.Rotate:
                var before = MathF.Atan2(_pressMouse.Y - centre.Y, _pressMouse.X - centre.X);
                var after = MathF.Atan2(mouse.Y - centre.Y, mouse.X - centre.X);
                settings.Rotation = _start.Rotation + (after - before) * 180f / MathF.PI;
                break;
            default:
                return;
        }

        _template.Modified();
    }
}
