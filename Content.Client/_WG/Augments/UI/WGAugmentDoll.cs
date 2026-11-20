using System.Numerics;
using Content.Shared._WG.Augments;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Input;

namespace Content.Client._WG.Augments.UI;

/// <summary>A part of the body the creator's doll picks: a limb, a hand or foot, the head or the torso.</summary>
public enum WGAugmentRegion : byte
{
    Head,
    Torso,
    LeftArm,
    LeftHand,
    RightArm,
    RightHand,
    LeftLeg,
    LeftFoot,
    RightLeg,
    RightFoot,
}

/// <summary>What each region of the doll holds and where it is drawn.</summary>
public static class WGAugmentRegions
{
    /// <summary>Regions in hit-test order: small parts before the torso they overlap.</summary>
    public static readonly WGAugmentRegion[] HitOrder =
    [
        WGAugmentRegion.LeftHand, WGAugmentRegion.RightHand, WGAugmentRegion.LeftFoot, WGAugmentRegion.RightFoot,
        WGAugmentRegion.LeftArm, WGAugmentRegion.RightArm, WGAugmentRegion.LeftLeg, WGAugmentRegion.RightLeg,
        WGAugmentRegion.Head, WGAugmentRegion.Torso,
    ];

    /// <summary>The slot whose part holds the region's implants.</summary>
    public static WGAugmentSlot Host(WGAugmentRegion region) => region switch
    {
        WGAugmentRegion.Head => WGAugmentSlot.Head,
        WGAugmentRegion.Torso => WGAugmentSlot.Chest,
        WGAugmentRegion.LeftArm => WGAugmentSlot.LeftArm,
        WGAugmentRegion.LeftHand => WGAugmentSlot.LeftHand,
        WGAugmentRegion.RightArm => WGAugmentSlot.RightArm,
        WGAugmentRegion.RightHand => WGAugmentSlot.RightHand,
        WGAugmentRegion.LeftLeg => WGAugmentSlot.LeftLeg,
        WGAugmentRegion.LeftFoot => WGAugmentSlot.LeftFoot,
        WGAugmentRegion.RightLeg => WGAugmentSlot.RightLeg,
        _ => WGAugmentSlot.RightFoot,
    };

    /// <summary>The slots picked in the region: its part, and the organs in the head or torso.</summary>
    public static WGAugmentSlot[] Slots(WGAugmentRegion region) => region switch
    {
        WGAugmentRegion.Head => [WGAugmentSlot.Head, WGAugmentSlot.Eyes],
        WGAugmentRegion.Torso =>
            [WGAugmentSlot.Chest, WGAugmentSlot.Heart, WGAugmentSlot.Lungs, WGAugmentSlot.Liver, WGAugmentSlot.Stomach],
        _ => [Host(region)],
    };

    /// <summary>The region holding a slot.</summary>
    public static WGAugmentRegion Of(WGAugmentSlot slot) => slot switch
    {
        WGAugmentSlot.Head or WGAugmentSlot.Eyes => WGAugmentRegion.Head,
        WGAugmentSlot.LeftArm => WGAugmentRegion.LeftArm,
        WGAugmentSlot.LeftHand => WGAugmentRegion.LeftHand,
        WGAugmentSlot.RightArm => WGAugmentRegion.RightArm,
        WGAugmentSlot.RightHand => WGAugmentRegion.RightHand,
        WGAugmentSlot.LeftLeg => WGAugmentRegion.LeftLeg,
        WGAugmentSlot.LeftFoot => WGAugmentRegion.LeftFoot,
        WGAugmentSlot.RightLeg => WGAugmentRegion.RightLeg,
        WGAugmentSlot.RightFoot => WGAugmentRegion.RightFoot,
        _ => WGAugmentRegion.Torso,
    };

    /// <summary>The humanoid layer whose sprite is the region's shape.</summary>
    public static HumanoidVisualLayers Layer(WGAugmentRegion region) => region switch
    {
        WGAugmentRegion.Head => HumanoidVisualLayers.Head,
        WGAugmentRegion.Torso => HumanoidVisualLayers.Chest,
        WGAugmentRegion.LeftArm => HumanoidVisualLayers.LArm,
        WGAugmentRegion.LeftHand => HumanoidVisualLayers.LHand,
        WGAugmentRegion.RightArm => HumanoidVisualLayers.RArm,
        WGAugmentRegion.RightHand => HumanoidVisualLayers.RHand,
        WGAugmentRegion.LeftLeg => HumanoidVisualLayers.LLeg,
        WGAugmentRegion.LeftFoot => HumanoidVisualLayers.LFoot,
        WGAugmentRegion.RightLeg => HumanoidVisualLayers.RLeg,
        _ => HumanoidVisualLayers.RFoot,
    };

    /// <summary>
    /// The region on a 32-pixel humanoid sprite facing south, measured from the human parts, for a part with no sprite
    /// to trace (a missing limb). The character's left is the viewer's right.
    /// </summary>
    public static UIBox2 Bounds(WGAugmentRegion region) => region switch
    {
        WGAugmentRegion.Head => new UIBox2(10, 2, 21, 10),
        WGAugmentRegion.Torso => new UIBox2(11, 10, 20, 23),
        WGAugmentRegion.LeftArm => new UIBox2(20, 11, 24, 18),
        WGAugmentRegion.LeftHand => new UIBox2(20, 18, 24, 22),
        WGAugmentRegion.RightArm => new UIBox2(7, 11, 11, 18),
        WGAugmentRegion.RightHand => new UIBox2(7, 18, 11, 22),
        WGAugmentRegion.LeftLeg => new UIBox2(16, 23, 21, 29),
        WGAugmentRegion.LeftFoot => new UIBox2(16, 29, 22, 32),
        WGAugmentRegion.RightLeg => new UIBox2(11, 23, 16, 29),
        _ => new UIBox2(10, 29, 16, 32),
    };
}

/// <summary>
/// The character drawn large, facing south. Hovering a part outlines its sprite's contour; clicking it selects it, and
/// the selection stays outlined. A part with no sprite (a missing limb) falls back to a box where it would be.
/// </summary>
public sealed class WGAugmentDoll : Control
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IResourceManager _resources = default!;

    /// <summary>Screen pixels per sprite pixel.</summary>
    public const float SpriteScale = 10f;

    private const float SpriteSize = 32f;
    private const float BorderWidth = 2f;

    /// <summary>The outline of the part under the mouse, and of the picked one.</summary>
    public Color HoverColor = Color.FromHex("#EDE6D2");
    public Color SelectedColor = Color.FromHex("#F2A54A");

    private readonly SpriteView _view;
    private EntityUid? _entity;

    public WGAugmentRegion? Hovered { get; private set; }
    public WGAugmentRegion? Selected { get; set; }

    /// <summary>Raised when a region is clicked.</summary>
    public event Action<WGAugmentRegion>? OnRegionPressed;

    /// <summary>Raised when the hovered region changes, with null when the mouse leaves the body.</summary>
    public event Action<WGAugmentRegion?>? OnRegionHovered;

    public WGAugmentDoll()
    {
        IoCManager.InjectDependencies(this);
        var size = new Vector2(SpriteSize * SpriteScale);
        SetSize = size;
        MouseFilter = MouseFilterMode.Stop;

        _view = new SpriteView
        {
            SetSize = size,
            Stretch = SpriteView.StretchMode.None,
            Scale = new Vector2(SpriteScale),
            OverrideDirection = Direction.South,
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(_view);
        AddChild(new Outlines(this) { SetSize = size, MouseFilter = MouseFilterMode.Ignore });
    }

    /// <summary>Shows an entity on the doll.</summary>
    public void SetEntity(EntityUid? uid)
    {
        _entity = uid;
        _view.SetEntity(uid);
    }

    /// <summary>Where a region's sprite sits: its mask, its top-left corner in doll pixels, and its scale.</summary>
    private readonly record struct Placed(WGAugmentMask Mask, Vector2 Origin, Vector2 Scale);

    /// <summary>The region's sprite as the doll draws it now, or null if the part has none showing.</summary>
    private Placed? Shape(WGAugmentRegion region)
    {
        if (_entity is not { } uid
            || !_entities.TryGetComponent(uid, out SpriteComponent? sprite)
            || !_entities.System<SpriteSystem>().LayerMapTryGet((uid, sprite), WGAugmentRegions.Layer(region), out var index, false)
            || sprite[index] is not SpriteComponent.Layer { Visible: true, ActualRsi: { } rsi } layer
            || WGAugmentMask.Of(_resources, rsi.Path, layer.State.Name, rsi.Size) is not { } mask)
            return null;

        // The sprite is drawn centered on the doll, the layer shifted by its offset (in meters, y up).
        var offset = new Vector2(layer.Offset.X, -layer.Offset.Y) * EyeManager.PixelsPerMeter;
        var scale = sprite.Scale;
        var origin = new Vector2(SpriteSize / 2) + (offset - new Vector2(mask.Width, mask.Height) / 2) * scale;
        return new Placed(mask, origin, scale);
    }

    private WGAugmentRegion? RegionAt(Vector2 position)
    {
        var pixel = position / SpriteScale;
        foreach (var region in WGAugmentRegions.HitOrder)
        {
            if (Shape(region) is { } shape)
            {
                var local = (pixel - shape.Origin) / shape.Scale;
                if (shape.Mask.Contains((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y)))
                    return region;
            }
            else if (WGAugmentRegions.Bounds(region).Contains(pixel))
            {
                return region;
            }
        }

        return null;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        SetHovered(RegionAt(args.RelativePosition));
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        SetHovered(null);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick || RegionAt(args.RelativePosition) is not { } region)
            return;

        Selected = region;
        OnRegionPressed?.Invoke(region);
        args.Handle();
    }

    private void SetHovered(WGAugmentRegion? region)
    {
        if (Hovered == region)
            return;

        Hovered = region;
        OnRegionHovered?.Invoke(region);
    }

    /// <summary>Draws the outlines over the sprite: a solid border along the part's contour and a see-through fill.</summary>
    private sealed class Outlines(WGAugmentDoll doll) : Control
    {
        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            if (doll.Selected is { } selected)
                Outline(handle, selected, doll.SelectedColor);
            if (doll.Hovered is { } hovered && hovered != doll.Selected)
                Outline(handle, hovered, doll.HoverColor);
        }

        private void Outline(DrawingHandleScreen handle, WGAugmentRegion region, Color color)
        {
            var toScreen = SpriteScale * UIScale;
            var fill = color.WithAlpha(0.25f);
            var border = BorderWidth * UIScale;

            if (doll.Shape(region) is not { } shape)
            {
                var bounds = WGAugmentRegions.Bounds(region);
                var box = new UIBox2(bounds.TopLeft * toScreen, bounds.BottomRight * toScreen);
                handle.DrawRect(box, fill);
                handle.DrawRect(box, color, filled: false);
                handle.DrawRect(new UIBox2(box.TopLeft + Vector2.One, box.BottomRight - Vector2.One), color, filled: false);
                return;
            }

            Vector2 Screen(float x, float y) => (shape.Origin + new Vector2(x, y) * shape.Scale) * toScreen;

            foreach (var (x, y) in shape.Mask.Pixels)
                handle.DrawRect(new UIBox2(Screen(x, y), Screen(x + 1, y + 1)), fill);

            // Each edge is a line along a pixel side; widen it inward-and-outward into a bar.
            foreach (var edge in shape.Mask.Edges)
            {
                var from = Screen(edge.Left, edge.Top);
                var to = Screen(edge.Right, edge.Bottom);
                var half = new Vector2(border / 2);
                handle.DrawRect(new UIBox2(Vector2.Min(from, to) - half, Vector2.Max(from, to) + half), color);
            }
        }
    }
}
