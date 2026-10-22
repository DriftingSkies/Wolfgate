using Content.Client._WF.MappingMode.Modes;
using Content.Shared._WF.MappingMode;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WF.MappingMode.Template;

/// <summary>
/// Draws the template image under the grids or over everything, at its opacity, on its own z-level only, and a thin
/// outline over it while Template mode is on.
/// </summary>
public sealed partial class WFMappingTemplateOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Color OutlineColor = Color.FromHex("#4fc3f7");

    private readonly WFMappingTemplateSystem _template;
    private readonly WFMappingBuildModeSystem _modes;
    private readonly SharedMapSystem _map;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld | OverlaySpace.WorldSpace;

    public WFMappingTemplateOverlay(WFMappingTemplateSystem template, WFMappingBuildModeSystem modes)
    {
        IoCManager.InjectDependencies(this);
        _template = template;
        _modes = modes;
        _map = _entities.System<SharedMapSystem>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_template.Texture == null)
            return false;

        var map = _map.GetMapOrInvalid(args.MapId);
        var depth = _entities.TryGetComponent(map, out CEZMapComponent? zMap) ? zMap.Depth : 0;
        return depth == _template.Settings.Depth;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_template.Texture is not { } texture)
            return;

        var settings = _template.Settings;
        var quad = settings.Quad(texture.Size);
        var wanted = settings.Layer == WFMappingTemplateLayer.Over
            ? OverlaySpace.WorldSpace
            : OverlaySpace.WorldSpaceBelowWorld;

        if (settings.Visible && args.Space == wanted && settings.Opacity > 0f)
            args.WorldHandle.DrawTextureRect(texture, quad, Color.White.WithAlpha(settings.Opacity));

        if (args.Space == OverlaySpace.WorldSpace && _modes.Active && _modes.Mode == WFMappingBuildMode.Template)
        {
            var color = settings.Locked ? OutlineColor.WithAlpha(0.35f) : OutlineColor;
            args.WorldHandle.DrawRect(quad, color, filled: false);
        }
    }
}
