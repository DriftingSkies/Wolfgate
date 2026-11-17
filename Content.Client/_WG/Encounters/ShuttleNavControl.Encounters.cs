using Content.Shared._WG.Encounters;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;
using System.Numerics;
using Content.Client._WG.Encounters;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>The ships of one side of one encounter, gathered for a frame.</summary>
    private readonly Dictionary<(NetEntity Encounter, string Side), (Vector2 Sum, int Count, bool Named, bool OnScope, Color Color, WGEncounterIcon Icon, WGEncounterCategory Category, float Warn, float Attack)> _encounterGroups = new();

    /// <summary>Where each side's ships were drawn this frame, for the spread of its zone envelope.</summary>
    private readonly Dictionary<(NetEntity Encounter, string Side), List<Vector2>> _encounterPoints = new();

    /// <summary>The ship each side's name hangs on this frame: its on-scope ship with the lowest grid id, whatever order the markers came in.</summary>
    private readonly Dictionary<(NetEntity Encounter, string Side), NetEntity> _encounterNamers = new();

    private static readonly ResPath MarkerGlyphs = new("/Textures/_WG/Encounters/markers.rsi");
    private readonly Dictionary<WGEncounterIcon, Texture> _encounterGlyphs = new();
    private WGEncounterRanks? _encounterRanks;

    private WGEncounterRanks EncounterRanks => _encounterRanks ??= new WGEncounterRanks(EntManager.System<SpriteSystem>());

    /// <summary>The texture for a glyph, loaded once.</summary>
    private Texture EncounterGlyph(WGEncounterIcon icon)
    {
        if (!_encounterGlyphs.TryGetValue(icon, out var texture))
        {
            texture = EntManager.System<SpriteSystem>().Frame0(new SpriteSpecifier.Rsi(MarkerGlyphs, icon.ToString().ToLowerInvariant()));
            _encounterGlyphs[icon] = texture;
        }

        return texture;
    }

    /// <summary>
    /// Marks the visible encounters in the sector. A ship on the scope gets a diamond; the ships of one side share a
    /// single name and a single pair of zone rings, drawn round the side as a whole: its widest ranges plus the spread
    /// of its ships, so a dozen hulls in a battle make one envelope and not a dozen overlapping circles. A side with
    /// no ship on the scope gets one bare arrow at the rim, however many ships it has, so far contacts don't crowd it.
    /// </summary>
    private void DrawEncounterMarkers(DrawingHandleScreen handle, TransformComponent consoleXform, Matrix3x2 worldToView)
    {
        var system = EntManager.System<WGEncounterMarkerClientSystem>();
        // Encounters are radar contacts like any other: the IFF toggle hides them too.
        if (!ShowIFF || system.Markers.Count == 0)
            return;

        var centre = MidPointVector;
        var rim = MathF.Min(PixelSize.X, PixelSize.Y) / 2f - 20f * UIScale;
        var size = 6f * UIScale;
        _encounterGroups.Clear();
        _encounterPoints.Clear();
        _encounterNamers.Clear();
        var here = _transform.GetWorldPosition(consoleXform);
        foreach (var marker in system.Markers)
        {
            if (marker.Map != consoleXform.MapID
                || (Vector2.Transform(system.GetPosition(marker), worldToView) - centre).Length() > rim)
                continue;

            var key = (marker.Encounter, marker.Side);
            if (!_encounterNamers.TryGetValue(key, out var namer) || marker.Grid.Id < namer.Id)
                _encounterNamers[key] = marker.Grid;
        }

        foreach (var marker in system.Markers)
        {
            if (marker.Map != consoleXform.MapID)
                continue;

            var key = (marker.Encounter, marker.Side);
            var color = marker.Color ?? WGEncounterColors.Category(marker.Category);
            if (!_encounterGroups.TryGetValue(key, out var group))
                group = (Vector2.Zero, 0, false, false, color, WGEncounterIcon.Category, marker.Category, 0f, 0f);

            var point = Vector2.Transform(system.GetPosition(marker), worldToView);
            var onScope = (point - centre).Length() <= rim;
            // Only a ship with zones of its own widens its side's rings: a wreck the server sent without ranges does not.
            if (marker.WarnRange > 0f || marker.AttackRange > 0f)
            {
                if (!_encounterPoints.TryGetValue(key, out var points))
                    _encounterPoints[key] = points = new List<Vector2>();
                points.Add(point);
            }
            if (onScope)
            {
                handle.DrawLine(point + new Vector2(0f, -size), point + new Vector2(size, 0f), color);
                handle.DrawLine(point + new Vector2(size, 0f), point + new Vector2(0f, size), color);
                handle.DrawLine(point + new Vector2(0f, size), point + new Vector2(-size, 0f), color);
                handle.DrawLine(point + new Vector2(-size, 0f), point + new Vector2(0f, -size), color);
                if (!group.Named && _encounterNamers.GetValueOrDefault(key) == marker.Grid)
                {
                    // Above the ship: its own IFF label hangs below and to the right, and the admin crew tag sits between.
                    // The threat rank follows the name on the same line, in a stack too.
                    // Named anywhere only when close enough to matter, like any other ship; else under the cursor.
                    var close = (system.GetPosition(marker) - here).Length() <= WgTagCloseRange;
                    WgBeginTag(point, point + new Vector2(10f, -54f) * UIScale, 2, always: close, key: HashCode.Combine(marker.Encounter, marker.Side));
                    WgTagLine(handle, marker.Name, UIScale * 0.8f, color);
                    var rank = handle.GetDimensions(Font, marker.Name, UIScale * 0.8f).Y;
                    var width = WGEncounterRanks.Width(marker.Threat, rank);
                    if (width > 0f)
                    {
                        var threat = marker.Threat;
                        WgTagHeadExtra(width + 3f * UIScale,
                            (h, at) => EncounterRanks.Draw(h, at + new Vector2(3f * UIScale, 0f), threat, rank));
                    }
                }
            }

            // Any ship of the side with a glyph of its own names the side's; the category's glyph is only a fallback at drawing.
            var icon = group.Icon != WGEncounterIcon.Category ? group.Icon : marker.Icon;
            var named = onScope && _encounterNamers.GetValueOrDefault(key) == marker.Grid;
            _encounterGroups[key] = (group.Sum + point, group.Count + 1, group.Named || named, group.OnScope || onScope, color, icon, group.Category,
                MathF.Max(group.Warn, marker.WarnRange), MathF.Max(group.Attack, marker.AttackRange));
        }

        foreach (var (key, group) in _encounterGroups)
        {
            // The side's zones as one envelope: its widest ranges out from the middle of its armed ships, plus their spread.
            if (!_encounterPoints.TryGetValue(key, out var zoned) || zoned.Count == 0)
                continue;

            var middle = Vector2.Zero;
            foreach (var point in zoned)
            {
                middle += point;
            }

            middle /= zoned.Count;
            var spread = 0f;
            foreach (var point in zoned)
            {
                spread = MathF.Max(spread, (point - middle).Length());
            }

            DrawEncounterZone(handle, middle, group.Warn, spread, Color.FromHex("#ffd23f"), system.WarnLabel);
            DrawEncounterZone(handle, middle, group.Attack, spread, Color.FromHex("#ff4b4b"), system.AttackLabel);
        }

        foreach (var group in _encounterGroups.Values)
        {
            if (group.OnScope)
                continue;

            var offset = group.Sum / group.Count - centre;
            if (offset.LengthSquared() < 1f)
                continue;

            var direction = Vector2.Normalize(offset);
            var side = new Vector2(-direction.Y, direction.X);
            var tip = centre + direction * rim;
            var back = tip - direction * size * 2f;
            handle.DrawLine(tip, back + side * size, group.Color);
            handle.DrawLine(tip, back - side * size, group.Color);
            handle.DrawLine(back + side * size, back - side * size, group.Color);
            // The side's glyph just inside the arrow, so a skull at the rim is a raider and a crate a freighter.
            var glyph = 16f * UIScale;
            var glyphCentre = back - direction * (glyph * 0.5f + 2f * UIScale);
            var icon = group.Icon != WGEncounterIcon.Category ? group.Icon : EncounterGlyphFor(group.Category);
            handle.DrawTextureRect(EncounterGlyph(icon), UIBox2.FromDimensions(glyphCentre - new Vector2(glyph / 2f), new Vector2(glyph)), group.Color);
        }
    }

    /// <summary>A category's own glyph.</summary>
    private static WGEncounterIcon EncounterGlyphFor(WGEncounterCategory category)
    {
        return category switch
        {
            WGEncounterCategory.Patrol => WGEncounterIcon.Shield,
            WGEncounterCategory.Threat => WGEncounterIcon.Skull,
            WGEncounterCategory.Distress => WGEncounterIcon.Cross,
            _ => WGEncounterIcon.Crate,
        };
    }

    /// <summary>
    /// A ring round a side at a zone's range, widened by the spread of its ships so it wraps every one of their zones,
    /// named at its top when any of it is on the scope.
    /// </summary>
    private void DrawEncounterZone(DrawingHandleScreen handle, Vector2 centre, float range, float spread, Color color, string label)
    {
        if (range <= 0f)
            return;

        var radius = range * MinimapScale + spread;
        if (centre.X + radius < 0f || centre.Y + radius < 0f || centre.X - radius > PixelSize.X || centre.Y - radius > PixelSize.Y)
            return;

        handle.DrawCircle(centre, radius, color.WithAlpha(0.8f), false);
        if (label.Length == 0)
            return;

        var dimensions = handle.GetDimensions(Font, label, UIScale * 0.7f);
        handle.DrawString(Font, centre + new Vector2(-dimensions.X / 2f, -radius - dimensions.Y - 2f * UIScale), label, UIScale * 0.7f, color);
    }
}
