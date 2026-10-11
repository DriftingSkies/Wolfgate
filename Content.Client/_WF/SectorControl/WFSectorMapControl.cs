using System.Numerics;
using Content.Client._WF.Encounters;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Content.Shared.Ghost;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.SectorControl;

/// <summary>The sector from above, zoomable out to 60 km either side of centre: territory, stations, encounter ships and the player's own ship.</summary>
public sealed class WFSectorMapControl : Control
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    /// <summary>The narrowest half-width the view zooms in to, in metres.</summary>
    public const float MinRange = 1500f;

    /// <summary>The widest half-width the view zooms out to, in metres.</summary>
    public const float MaxRange = 60000f;

    /// <summary>The space left around the stations and territory when the view is fitted to them.</summary>
    public const float FitMargin = 3000f;

    private const float RingSpacing = 5000f;
    private const float ZoomStep = 1.25f;

    /// <summary>How far from the origin the centre can be dragged.</summary>
    private const float CentreLimit = MaxRange * 1.5f;

    private static readonly Color BackingColor = Color.FromHex("#0b0e12");
    private static readonly Color RingColor = Color.FromHex("#4a5866").WithAlpha(0.35f);
    private static readonly Color RingLabelColor = Color.FromHex("#6f8090");
    private static readonly Color LandmarkColor = Color.FromHex("#c8d4e0");
    private static readonly Color DimColor = Color.FromHex("#8a96a3");
    private static readonly ResPath MarkerGlyphs = new("/Textures/_WF/Encounters/markers.rsi");

    private readonly WFSectorOverlay _overlay = new();
    private readonly Font _font;
    private readonly List<UIBox2> _placedLabels = new();
    private readonly Dictionary<WFEncounterIcon, Texture> _glyphs = new();
    private readonly Vector2[] _triangle = new Vector2[3];
    private Vector2[] _ring = new Vector2[129];

    private Vector2 _centre;
    private float _range = 20000f;

    /// <summary>The map the view was last fitted to; a new map, or Nullspace, fits it again.</summary>
    private MapId _fitted = MapId.Nullspace;

    private bool _dragging;
    private Vector2 _lastPointer;
    private Vector2? _hover;
    private TimeSpan? _lastClick;
    private Vector2 _lastClickPosition;

    /// <summary>The entity whose map is shown, such as a shuttle console; null shows the player's map, or else any map with territory.</summary>
    public EntityUid? Tracked { get; set; }

    /// <summary>The world point at the middle of the control.</summary>
    public Vector2 Centre => _centre;

    /// <summary>The half-width of the view, in metres.</summary>
    public float Range => _range;

    /// <summary>Pixels per metre at the current zoom.</summary>
    public float PixelsPerMetre => PixelSize.X > 0 ? PixelSize.X / 2f / _range : 0f;

    public WFSectorMapControl()
    {
        IoCManager.InjectDependencies(this);
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
        var cache = IoCManager.Resolve<IResourceCache>();
        _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 12);
    }

    /// <summary>The territory shown: the tracked entity's map, or with nothing tracked the player's map, else any map the client has.</summary>
    public bool TryGetView(out WFSectorMapView view)
    {
        var system = _entManager.System<WFSectorClientSystem>();
        if (Tracked is { } tracked)
        {
            var map = _entManager.TryGetComponent(tracked, out TransformComponent? xform) ? xform.MapID : MapId.Nullspace;
            return system.TryGetMap(map, out view);
        }

        if (_player.LocalEntity is { } player
            && _entManager.TryGetComponent(player, out TransformComponent? own)
            && system.TryGetMap(own.MapID, out view))
        {
            return true;
        }

        return system.TryGetAnyMap(out view);
    }

    /// <summary>Map space to the control's pixels: <c>(world - centre) * scale</c>, Y flipped, about the midpoint.</summary>
    public Matrix3x2 WorldToView()
    {
        var scale = PixelsPerMetre;
        return Matrix3x2.CreateTranslation(-_centre)
               * Matrix3x2.CreateScale(scale, -scale)
               * Matrix3x2.CreateTranslation((Vector2) PixelSize / 2f);
    }

    /// <summary>The map position under a pixel of the control.</summary>
    public Vector2 ViewToWorld(Vector2 pixel)
    {
        var scale = PixelsPerMetre;
        if (scale <= 0f)
            return _centre;

        var offset = (pixel - (Vector2) PixelSize / 2f) / scale;
        return _centre + new Vector2(offset.X, -offset.Y);
    }

    /// <summary>Fits the view to the stations and territory again.</summary>
    public void ShowWholeSector()
    {
        if (PixelSize.X > 0 && PixelSize.Y > 0 && TryGetView(out var view))
            Fit(view);
        else
            _fitted = MapId.Nullspace;
    }

    /// <summary>Centres the view on the player's own grid, or on the player when they are not aboard one (a ghost always); false if neither is on the map shown.</summary>
    public bool CentreOnPlayer()
    {
        if (!TryGetView(out var view))
            return false;

        if (TryGetOwnShip(view.Map, out var position, out _))
        {
            SetCentre(position);
            return true;
        }

        if (_player.LocalEntity is not { } player
            || !_entManager.TryGetComponent(player, out TransformComponent? xform)
            || xform.MapID != view.Map)
        {
            return false;
        }

        SetCentre(_entManager.System<SharedTransformSystem>().GetWorldPosition(xform));
        return true;
    }

    /// <summary>Zooms by a factor, above one to zoom out, keeping the map point under a pixel where it is.</summary>
    public void ZoomAt(Vector2 pixel, float factor)
    {
        if (PixelSize.X <= 0)
            return;

        var anchor = ViewToWorld(pixel);
        _range = Math.Clamp(_range * factor, MinRange, MaxRange);
        var offset = (pixel - (Vector2) PixelSize / 2f) / PixelsPerMetre;
        SetCentre(anchor - new Vector2(offset.X, -offset.Y));
    }

    /// <summary>The hover line for a map position: the cell's callsign and who holds or contests it.</summary>
    public string Readout(WFSectorMapView view, Vector2 world)
    {
        var cell = WFSectorHex.CellOf(world, view.CellSize);
        var callsign = WFSectorHex.Callsign(cell);
        foreach (var contest in view.Contests)
        {
            if (contest.Cell != cell)
                continue;

            var remaining = WFSectorOverlay.FormatRemaining(contest.Deadline - _timing.CurTime);
            return Loc.GetString("wf-sector-map-hover-contested",
                ("callsign", callsign), ("faction", view.Names[contest.Faction]), ("time", remaining));
        }

        if (view.Owners.TryGetValue(cell, out var owner))
            return Loc.GetString("wf-sector-map-hover-held", ("callsign", callsign), ("faction", view.Names[owner]));

        return Loc.GetString("wf-sector-map-hover-open", ("callsign", callsign));
    }

    /// <summary>Where the player's own grid is on a map and which way it faces, when the player is aboard one there; a ghost is never aboard, even over a grid.</summary>
    public bool TryGetOwnShip(MapId map, out Vector2 position, out Angle heading)
    {
        position = default;
        heading = default;
        if (_player.LocalEntity is not { } player
            || _entManager.HasComponent<GhostComponent>(player)
            || !_entManager.TryGetComponent(player, out TransformComponent? xform)
            || xform.GridUid is not { } grid
            || !_entManager.TryGetComponent(grid, out TransformComponent? gridXform)
            || gridXform.MapID != map
            || grid == gridXform.MapUid)
        {
            return false;
        }

        var transform = _entManager.System<SharedTransformSystem>();
        var (worldPosition, worldRotation) = transform.GetWorldPositionRotation(gridXform);
        position = _entManager.TryGetComponent(grid, out PhysicsComponent? body)
            ? transform.ToMapCoordinates(new EntityCoordinates(grid, body.LocalCenter)).Position
            : worldPosition;
        heading = worldRotation;
        return true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (TryGetView(out var view))
            EnsureFitted(view);
    }

    private void EnsureFitted(WFSectorMapView view)
    {
        if (_fitted != view.Map && PixelSize.X > 0 && PixelSize.Y > 0)
            Fit(view);
    }

    /// <summary>Centres on the stations and territory and zooms to show them all, with a margin.</summary>
    private void Fit(WFSectorMapView view)
    {
        var box = view.Extent ?? new Box2(Vector2.Zero, Vector2.Zero);
        var aspect = (float) PixelSize.X / PixelSize.Y;
        var halfWidth = box.Width / 2f + FitMargin;
        var halfHeight = box.Height / 2f + FitMargin;
        _range = Math.Clamp(MathF.Max(halfWidth, halfHeight * aspect), MinRange, MaxRange);
        SetCentre(box.Center);
        _fitted = view.Map;
    }

    private void SetCentre(Vector2 centre)
    {
        _centre = Vector2.Clamp(centre, new Vector2(-CentreLimit), new Vector2(CentreLimit));
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        var now = _timing.RealTime;
        var position = args.RelativePixelPosition;
        if (_lastClick is { } last
            && now - last <= TimeSpan.FromMilliseconds(_cfg.GetCVar(CVars.DoubleClickDelay))
            && (position - _lastClickPosition).Length() < _cfg.GetCVar(CVars.DoubleClickRange))
        {
            _lastClick = null;
            _dragging = false;
            CentreOnPlayer();
            args.Handle();
            return;
        }

        _lastClick = now;
        _lastClickPosition = position;
        _lastPointer = position;
        _dragging = true;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _dragging = false;
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        var position = args.RelativePixelPosition;
        _hover = position;
        if (!_dragging)
            return;

        var delta = position - _lastPointer;
        _lastPointer = position;
        var scale = PixelsPerMetre;
        if (scale > 0f)
            SetCentre(_centre - new Vector2(delta.X, -delta.Y) / scale);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hover = null;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        if (args.Delta.Y == 0f)
            return;

        ZoomAt(args.RelativePixelPosition, MathF.Pow(ZoomStep, -args.Delta.Y));
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = (Vector2) PixelSize;
        var bounds = new UIBox2(Vector2.Zero, size);
        handle.DrawRect(bounds, BackingColor);
        if (size.X <= 0f || size.Y <= 0f)
            return;

        if (!TryGetView(out var view))
        {
            var text = Loc.GetString("wf-sector-map-no-data");
            var dimensions = handle.GetDimensions(_font, text, UIScale);
            handle.DrawString(_font, (size - dimensions) / 2f, text, UIScale, DimColor);
            return;
        }

        EnsureFitted(view);
        var system = _entManager.System<WFSectorClientSystem>();
        var worldToView = WorldToView();
        var scale = PixelsPerMetre;
        DrawRings(handle, worldToView, scale, bounds);
        _overlay.DrawFills(handle, view, worldToView);
        _overlay.DrawBorders(handle, view, worldToView);
        _overlay.DrawContests(handle, system, view, worldToView, _font, UIScale, bounds);
        _overlay.DrawCallsigns(handle, view, worldToView, _font, UIScale, scale, bounds);
        _placedLabels.Clear();
        DrawLandmarks(handle, view, worldToView, bounds);
        DrawEncounters(handle, view.Map, worldToView, bounds);
        DrawOwnShip(handle, view.Map, worldToView);
        // The legend goes over the stations, so a label never runs across it.
        _overlay.DrawLegend(handle, system, _font, UIScale);
        DrawReadout(handle, view, bounds);
    }

    /// <summary>Faint rings every 5 km about the origin, each with its distance on the right.</summary>
    private void DrawRings(DrawingHandleScreen handle, in Matrix3x2 worldToView, float scale, UIBox2 bounds)
    {
        if (RingSpacing * scale < 8f * UIScale)
            return;

        var origin = Vector2.Transform(Vector2.Zero, worldToView);
        var nearest = Vector2.Clamp(origin, bounds.TopLeft, bounds.BottomRight);
        var near = (nearest - origin).Length();
        var far = MathF.Max(
            MathF.Max((bounds.TopLeft - origin).Length(), (bounds.BottomRight - origin).Length()),
            MathF.Max((bounds.TopRight - origin).Length(), (bounds.BottomLeft - origin).Length()));
        var spacing = RingSpacing * scale;
        var first = Math.Max(1, (int) MathF.Ceiling(near / spacing));
        var last = Math.Min(first + 64, (int) MathF.Floor(far / spacing));
        var labelScale = UIScale * 0.8f;
        for (var ring = first; ring <= last; ring++)
        {
            var radius = ring * spacing;
            DrawRing(handle, origin, radius);
            var label = Loc.GetString("wf-sector-map-ring", ("km", (int) (ring * RingSpacing / 1000f)));
            var dimensions = handle.GetDimensions(_font, label, labelScale);
            var at = origin + new Vector2(radius - dimensions.X - 3f * UIScale, -dimensions.Y - 1f * UIScale);
            if (bounds.Contains(at) && bounds.Contains(at + dimensions))
                handle.DrawString(_font, at, label, labelScale, RingLabelColor);
        }
    }

    /// <summary>A ring with enough segments that it stays round however far it is zoomed in.</summary>
    private void DrawRing(DrawingHandleScreen handle, Vector2 centre, float radius)
    {
        var segments = Math.Clamp((int) (radius / 4f), 64, 2048);
        if (_ring.Length < segments + 1)
            _ring = new Vector2[segments + 1];

        for (var i = 0; i <= segments; i++)
        {
            var angle = i / (float) segments * MathF.Tau;
            _ring[i] = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, new ReadOnlySpan<Vector2>(_ring, 0, segments + 1), RingColor);
    }

    /// <summary>A dot and the name of each station; a name that would overlap one already printed is left out.</summary>
    private void DrawLandmarks(DrawingHandleScreen handle, WFSectorMapView view, in Matrix3x2 worldToView, UIBox2 bounds)
    {
        var labelScale = UIScale * 0.85f;
        var gap = 5f * UIScale;
        foreach (var landmark in view.Landmarks)
        {
            var point = Vector2.Transform(landmark.Position, worldToView);
            if (!bounds.Contains(point))
                continue;

            handle.DrawCircle(point, 2.5f * UIScale, LandmarkColor);
            var dimensions = handle.GetDimensions(_font, landmark.Name, labelScale);
            var right = UIBox2.FromDimensions(point + new Vector2(gap, -dimensions.Y / 2f), dimensions);
            var left = UIBox2.FromDimensions(point - new Vector2(gap + dimensions.X, dimensions.Y / 2f), dimensions);
            UIBox2 box;
            if (LabelFits(right, bounds))
                box = right;
            else if (LabelFits(left, bounds))
                box = left;
            else
                continue;

            _placedLabels.Add(box);
            handle.DrawString(_font, box.TopLeft + new Vector2(1f, 1f), landmark.Name, labelScale, Color.Black.WithAlpha(0.8f));
            handle.DrawString(_font, box.TopLeft, landmark.Name, labelScale, LandmarkColor);
        }
    }

    private bool LabelFits(UIBox2 box, UIBox2 bounds)
    {
        if (!bounds.Contains(box.TopLeft) || !bounds.Contains(box.BottomRight))
            return false;

        foreach (var placed in _placedLabels)
        {
            if (placed.Intersects(box))
                return false;
        }

        return true;
    }

    /// <summary>Each encounter ship on the map as its glyph, coloured as the radar colours it.</summary>
    private void DrawEncounters(DrawingHandleScreen handle, MapId map, in Matrix3x2 worldToView, UIBox2 bounds)
    {
        var markers = _entManager.System<WFEncounterMarkerClientSystem>();
        if (markers.Markers.Count == 0)
            return;

        var size = new Vector2(14f * UIScale);
        foreach (var marker in markers.Markers)
        {
            if (marker.Map != map)
                continue;

            var point = Vector2.Transform(markers.GetPosition(marker), worldToView);
            if (!bounds.Contains(point))
                continue;

            var color = marker.Color ?? WFEncounterColors.Category(marker.Category);
            var icon = marker.Icon != WFEncounterIcon.Category ? marker.Icon : GlyphFor(marker.Category);
            handle.DrawTextureRect(Glyph(icon), UIBox2.FromDimensions(point - size / 2f, size), color);
        }
    }

    /// <summary>A category's own glyph, as the radar draws it.</summary>
    private static WFEncounterIcon GlyphFor(WFEncounterCategory category)
    {
        return category switch
        {
            WFEncounterCategory.Patrol => WFEncounterIcon.Shield,
            WFEncounterCategory.Threat => WFEncounterIcon.Skull,
            WFEncounterCategory.Distress => WFEncounterIcon.Cross,
            _ => WFEncounterIcon.Crate,
        };
    }

    /// <summary>The texture for a glyph, loaded once.</summary>
    private Texture Glyph(WFEncounterIcon icon)
    {
        if (!_glyphs.TryGetValue(icon, out var texture))
        {
            texture = _entManager.System<SpriteSystem>().Frame0(new SpriteSpecifier.Rsi(MarkerGlyphs, icon.ToString().ToLowerInvariant()));
            _glyphs[icon] = texture;
        }

        return texture;
    }

    /// <summary>A small white triangle on the player's own grid, pointing the way it faces.</summary>
    private void DrawOwnShip(DrawingHandleScreen handle, MapId map, in Matrix3x2 worldToView)
    {
        if (!TryGetOwnShip(map, out var position, out var heading))
        {
            DrawGhost(handle, map, worldToView);
            return;
        }

        var point = Vector2.Transform(position, worldToView);
        var forward = heading.RotateVec(new Vector2(0f, 1f));
        var direction = Vector2.Normalize(new Vector2(forward.X, -forward.Y));
        var side = new Vector2(-direction.Y, direction.X);
        var size = 7f * UIScale;
        _triangle[0] = point + direction * size;
        _triangle[1] = point - direction * size * 0.6f + side * size * 0.6f;
        _triangle[2] = point - direction * size * 0.6f - side * size * 0.6f;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _triangle, Color.White);
        var outline = Color.Black.WithAlpha(0.8f);
        handle.DrawLine(_triangle[0], _triangle[1], outline);
        handle.DrawLine(_triangle[1], _triangle[2], outline);
        handle.DrawLine(_triangle[2], _triangle[0], outline);
    }

    /// <summary>A small white ring where the player's ghost is on a map.</summary>
    private void DrawGhost(DrawingHandleScreen handle, MapId map, in Matrix3x2 worldToView)
    {
        if (_player.LocalEntity is not { } player
            || !_entManager.HasComponent<GhostComponent>(player)
            || !_entManager.TryGetComponent(player, out TransformComponent? xform)
            || xform.MapID != map)
        {
            return;
        }

        var point = Vector2.Transform(_entManager.System<SharedTransformSystem>().GetWorldPosition(xform), worldToView);
        handle.DrawCircle(point, 5f * UIScale, Color.Black.WithAlpha(0.8f), false);
        handle.DrawCircle(point, 4f * UIScale, Color.White, false);
    }

    /// <summary>The cell under the cursor, its callsign and who holds it, beside the cursor.</summary>
    private void DrawReadout(DrawingHandleScreen handle, WFSectorMapView view, UIBox2 bounds)
    {
        if (_hover is not { } hover || !bounds.Contains(hover))
            return;

        var text = Readout(view, ViewToWorld(hover));
        var dimensions = handle.GetDimensions(_font, text, UIScale);
        var pad = new Vector2(4f * UIScale);
        var offset = 16f * UIScale;
        var at = hover + new Vector2(offset);
        if (at.X + dimensions.X + pad.X * 2f > bounds.Right)
            at.X = hover.X - offset - dimensions.X - pad.X * 2f;

        if (at.Y + dimensions.Y + pad.Y * 2f > bounds.Bottom)
            at.Y = hover.Y - offset - dimensions.Y - pad.Y * 2f;

        handle.DrawRect(UIBox2.FromDimensions(at, dimensions + pad * 2f), Color.Black.WithAlpha(0.75f));
        handle.DrawString(_font, at + pad, text, UIScale, Color.White);
    }
}
