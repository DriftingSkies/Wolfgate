using System.Globalization;
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

/// <summary>The sector from above, zoomable out to 60 km either side of centre: the cell grid, territory, the grids and encounters a shuttle console's MAP tab lists, and the player's own ship.</summary>
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

    /// <summary>The space left around the grids and territory when the view is fitted to them.</summary>
    public const float FitMargin = 3000f;

    private const float ZoomStep = 1.25f;

    /// <summary>How narrow a cell may get on screen, in pixels, before the grid is no longer outlined.</summary>
    private const float GridPixels = 6f;

    /// <summary>How far from the origin the centre can be dragged.</summary>
    private const float CentreLimit = MaxRange * 1.5f;

    private static readonly Color BackingColor = Color.FromHex("#0b0e12");
    private static readonly Color GridColor = Color.White.WithAlpha(0.08f);
    private static readonly Color LandmarkColor = Color.FromHex("#c8d4e0");
    private static readonly Color DimColor = Color.FromHex("#8a96a3");
    private static readonly ResPath MarkerGlyphs = new("/Textures/_WF/Encounters/markers.rsi");

    private readonly WFSectorOverlay _overlay = new();
    private readonly Font _font;
    private readonly List<UIBox2> _placedLabels = new();
    private readonly Dictionary<WFEncounterIcon, Texture> _glyphs = new();
    private readonly List<WFSectorLandmarkView> _landmarks = new();
    private readonly List<WFSectorEncounterView> _encounters = new();
    private readonly HashSet<NetEntity> _listed = new();
    private readonly Vector2[] _triangle = new Vector2[3];

    private Vector2 _centre;
    private float _range = 20000f;

    /// <summary>The map the view was last fitted to; a new map, or Nullspace, fits it again.</summary>
    private MapId _fitted = MapId.Nullspace;

    private bool _dragging;
    private Vector2 _lastPointer;
    private Vector2? _hover;
    private TimeSpan? _lastClick;
    private Vector2 _lastClickPosition;

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

    /// <summary>The territory shown: the player's map, else any map the client has, the sector map first.</summary>
    public bool TryGetView(out WFSectorMapView view)
    {
        var system = _entManager.System<WFSectorClientSystem>();
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

    /// <summary>Fits the view to the grids and territory again.</summary>
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

    /// <summary>The hover line for a map position: the cell's callsign, its distance from the origin and who holds or contests it.</summary>
    public string Readout(WFSectorMapView view, Vector2 world)
    {
        var cell = WFSectorHex.CellOf(world, view.CellSize);
        var callsign = WFSectorHex.Callsign(cell);
        var distance = (world.Length() / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
        foreach (var contest in view.Contests)
        {
            if (contest.Cell != cell)
                continue;

            var remaining = WFSectorOverlay.FormatRemaining(contest.Deadline - _timing.CurTime);
            return Loc.GetString("wf-sector-map-hover-contested",
                ("callsign", callsign), ("distance", distance), ("faction", view.Names[contest.Faction]), ("time", remaining));
        }

        if (view.Owners.TryGetValue(cell, out var owner))
        {
            return Loc.GetString("wf-sector-map-hover-held",
                ("callsign", callsign), ("distance", distance), ("faction", view.Names[owner]));
        }

        return Loc.GetString("wf-sector-map-hover-open", ("callsign", callsign), ("distance", distance));
    }

    /// <summary>Where the player's own grid is on a map and which way it faces, when the player is aboard one there; a ghost is never aboard, even over a grid.</summary>
    public bool TryGetOwnShip(MapId map, out Vector2 position, out Angle heading)
    {
        position = default;
        heading = default;
        if (OwnGrid(map) is not { } grid)
            return false;

        var transform = _entManager.System<SharedTransformSystem>();
        var (worldPosition, worldRotation) = transform.GetWorldPositionRotation(grid);
        position = _entManager.TryGetComponent(grid, out PhysicsComponent? body)
            ? transform.ToMapCoordinates(new EntityCoordinates(grid, body.LocalCenter)).Position
            : worldPosition;
        heading = worldRotation;
        return true;
    }

    /// <summary>Fills a list with the grids on a map that the sector map names, as the MAP tab lists them without a scan, leaving out the player's own ship.</summary>
    public void CollectLandmarks(MapId map, List<WFSectorLandmarkView> into)
    {
        _entManager.System<WFSectorClientSystem>().CollectLandmarks(map, OwnGrid(map), into);
    }

    /// <summary>Fills a list with one marker per encounter on a map, at its first ship, as the MAP tab lists them.</summary>
    public void CollectEncounters(MapId map, IReadOnlyList<WFEncounterMarker> markers, List<WFSectorEncounterView> into)
    {
        into.Clear();
        _listed.Clear();
        var system = _entManager.System<WFEncounterMarkerClientSystem>();
        foreach (var marker in markers)
        {
            if (marker.Map != map || !_listed.Add(marker.Encounter))
                continue;

            into.Add(new WFSectorEncounterView(marker.Name, marker.Category, system.GetPosition(marker)));
        }
    }

    /// <summary>The grid the player is aboard on a map; none for a ghost, even over a grid.</summary>
    private EntityUid? OwnGrid(MapId map)
    {
        if (_player.LocalEntity is not { } player
            || _entManager.HasComponent<GhostComponent>(player)
            || !_entManager.TryGetComponent(player, out TransformComponent? xform)
            || xform.GridUid is not { } grid
            || !_entManager.TryGetComponent(grid, out TransformComponent? gridXform)
            || gridXform.MapID != map
            || grid == gridXform.MapUid)
        {
            return null;
        }

        return grid;
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

    /// <summary>Centres on the named grids within reach, the player's ship and the territory, and zooms to show them all with a margin.</summary>
    private void Fit(WFSectorMapView view)
    {
        var box = view.Extent;
        CollectLandmarks(view.Map, _landmarks);
        foreach (var landmark in _landmarks)
        {
            // A grid past the widest zoom could never be shown with the rest.
            if (landmark.Position.Length() <= MaxRange)
                box = Grow(box, landmark.Position);
        }

        if (TryGetOwnShip(view.Map, out var own, out _))
            box = Grow(box, own);

        var bounds = box ?? new Box2(Vector2.Zero, Vector2.Zero);
        var aspect = (float) PixelSize.X / PixelSize.Y;
        var halfWidth = bounds.Width / 2f + FitMargin;
        var halfHeight = bounds.Height / 2f + FitMargin;
        _range = Math.Clamp(MathF.Max(halfWidth, halfHeight * aspect), MinRange, MaxRange);
        SetCentre(bounds.Center);
        _fitted = view.Map;
    }

    private static Box2 Grow(Box2? box, Vector2 point)
    {
        return box is { } current ? current.ExtendToContain(point) : new Box2(point, point);
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
        DrawGrid(handle, view, worldToView, scale);
        _overlay.DrawFills(handle, view, worldToView);
        _overlay.DrawBorders(handle, view, worldToView);
        _overlay.DrawContests(handle, system, view, worldToView, _font, UIScale, bounds);
        _overlay.DrawCallsigns(handle, view.CellLabels, view.CellSize, worldToView, _font, UIScale, scale, bounds);
        _placedLabels.Clear();
        DrawLandmarks(handle, view.Map, worldToView, bounds);
        DrawEncounters(handle, view.Map, worldToView, bounds);
        DrawOwnShip(handle, view.Map, worldToView);
        // The legend goes over the grids, so a label never runs across it.
        _overlay.DrawLegend(handle, system, _font, UIScale);
        DrawReadout(handle, view, bounds);
    }

    /// <summary>Faintly outlines every cell of the sector, unless a cell would be too narrow on screen to see.</summary>
    private void DrawGrid(DrawingHandleScreen handle, WFSectorMapView view, in Matrix3x2 worldToView, float scale)
    {
        if (view.Grid.Length == 0 || view.CellSize * MathF.Sqrt(3f) * scale < GridPixels * UIScale)
            return;

        _overlay.Draw(handle, DrawPrimitiveTopology.LineList, view.Grid, worldToView, GridColor);
    }

    /// <summary>A dot and the name of each grid the MAP tab would list; a name that would overlap one already printed is left out.</summary>
    private void DrawLandmarks(DrawingHandleScreen handle, MapId map, in Matrix3x2 worldToView, UIBox2 bounds)
    {
        CollectLandmarks(map, _landmarks);
        foreach (var landmark in _landmarks)
        {
            var point = Vector2.Transform(landmark.Position, worldToView);
            if (!bounds.Contains(point))
                continue;

            handle.DrawCircle(point, 2.5f * UIScale, LandmarkColor);
            DrawLabel(handle, point, 5f * UIScale, landmark.Name, LandmarkColor, bounds);
        }
    }

    /// <summary>Each encounter as its category's glyph at its first ship, with its name beside it where there is room.</summary>
    private void DrawEncounters(DrawingHandleScreen handle, MapId map, in Matrix3x2 worldToView, UIBox2 bounds)
    {
        CollectEncounters(map, _entManager.System<WFEncounterMarkerClientSystem>().Markers, _encounters);
        var size = new Vector2(14f * UIScale);
        foreach (var encounter in _encounters)
        {
            var point = Vector2.Transform(encounter.Position, worldToView);
            if (!bounds.Contains(point))
                continue;

            var color = WFEncounterColors.Category(encounter.Category);
            handle.DrawTextureRect(Glyph(GlyphFor(encounter.Category)), UIBox2.FromDimensions(point - size / 2f, size), color);
            DrawLabel(handle, point, size.X / 2f + 3f * UIScale, encounter.Name, color, bounds);
        }
    }

    /// <summary>Prints a name to the right of a point, or the left if there is no room, unless it would overlap a name already printed.</summary>
    private void DrawLabel(DrawingHandleScreen handle, Vector2 point, float gap, string text, Color color, UIBox2 bounds)
    {
        if (text.Length == 0)
            return;

        var labelScale = UIScale * 0.85f;
        var dimensions = handle.GetDimensions(_font, text, labelScale);
        var right = UIBox2.FromDimensions(point + new Vector2(gap, -dimensions.Y / 2f), dimensions);
        var left = UIBox2.FromDimensions(point - new Vector2(gap + dimensions.X, dimensions.Y / 2f), dimensions);
        UIBox2 box;
        if (LabelFits(right, bounds))
            box = right;
        else if (LabelFits(left, bounds))
            box = left;
        else
            return;

        _placedLabels.Add(box);
        handle.DrawString(_font, box.TopLeft + new Vector2(1f, 1f), text, labelScale, Color.Black.WithAlpha(0.8f));
        handle.DrawString(_font, box.TopLeft, text, labelScale, color);
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

    /// <summary>The cell under the cursor, its callsign, distance out and who holds it, beside the cursor.</summary>
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

/// <summary>An encounter on the sector map: its name, its category and where its first ship is in map space.</summary>
public readonly record struct WFSectorEncounterView(string Name, WFEncounterCategory Category, Vector2 Position);
