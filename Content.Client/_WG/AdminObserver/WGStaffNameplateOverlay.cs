using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._WG.AdminObserver;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;

namespace Content.Client._WG.AdminObserver;

/// <summary>Draws each staff marker's wording over its entity, for every viewer.</summary>
public sealed class WGStaffNameplateOverlay : Overlay
{
    private readonly IEntityManager _entities;
    private readonly IEyeManager _eye;
    private readonly EntityLookupSystem _lookup;
    private readonly IUserInterfaceManager _ui;
    private readonly Font _font;

    /// <summary>How far above an entity's middle the plate sits, in tiles.</summary>
    private const float Height = 0.75f;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public WGStaffNameplateOverlay(
        IEntityManager entities,
        IEyeManager eye,
        EntityLookupSystem lookup,
        IResourceCache resources,
        IUserInterfaceManager ui)
    {
        _entities = entities;
        _eye = eye;
        _lookup = lookup;
        _ui = ui;
        _font = resources.NotoStack(variation: "Bold", size: 10);
        ZIndex = 190;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var scale = _ui.RootControl.UIScale;
        var query = _entities.EntityQueryEnumerator<WGStaffMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            if ((marker.Visuals & WGMarkerVisuals.Nameplate) == 0 || xform.MapID != args.MapId || marker.Label.Length == 0)
                continue;

            var centre = _lookup.GetWorldAABB(uid, xform).Center;
            if (!args.WorldAABB.Contains(centre))
                continue;

            // A tile's length on screen, whatever the zoom and however the eye is turned.
            var middle = _eye.WorldToScreen(centre);
            var tile = (_eye.WorldToScreen(centre + Vector2.UnitX) - middle).Length();
            var size = args.ScreenHandle.GetDimensions(_font, marker.Label, scale);
            var position = (middle - new Vector2(size.X / 2f, tile * Height + size.Y)).Rounded();

            args.ScreenHandle.DrawString(_font, position + Vector2.One, marker.Label, scale, Color.Black);
            args.ScreenHandle.DrawString(_font, position, marker.Label, scale, marker.Color);
        }
    }
}
