using System.Numerics;
using Content.Client._WG.Encounters;
using Content.Shared._Mono.Company;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.AsteroidMining;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.Tiles;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using SpaceDebrisComponent = Content.Server._NF.Worldgen.Components.Debris.SpaceDebrisComponent;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// Radar contacts on the scope drawn as what they are: a ship as a chevron pointing its heading, a station as a
/// square, an asteroid as a rough hexagon, a wreck as a cross. A contact at the rim keeps the triangle pointing its
/// way, and one the scope can't make out keeps the plain triangle.
/// </summary>
public partial class ShuttleNavControl
{
    private enum WgContactKind : byte
    {
        Ship,
        Station,
        Asteroid,
        Wreck,
    }

    private readonly record struct WgContactIcon(WgContactKind Kind, Vector2 Position, Vector2 Heading, Color Color);

    private readonly List<WgContactIcon> _wgContactIcons = new();
    private readonly HashSet<NetEntity> _wgEncounterGrids = new();
    private readonly Vector2[] _wgIconVertices = new Vector2[7];

    /// <summary>
    /// Queues an icon for a grid on the scope, at <paramref name="uiPosition"/> in virtual pixels. False when it gets
    /// the usual triangle instead: out at the rim, or not made out.
    /// </summary>
    private bool WgQueueContactIcon(EntityUid grid, Vector2 uiPosition, Matrix3x2 gridToView, Color color, bool unknown)
    {
        if (unknown)
            return false;

        var kind = WgContactKindOf(grid);
        // The grid's own up, turned into the view, is the way a ship points.
        var heading = Vector2.TransformNormal(new Vector2(0f, 1f), gridToView);
        heading = heading.LengthSquared() > 0f ? Vector2.Normalize(heading) : new Vector2(0f, -1f);

        if (EntManager.TryGetComponent(grid, out CompanyComponent? company) && !string.IsNullOrEmpty(company.CompanyName)
            && IoCManager.Resolve<IPrototypeManager>().TryIndex<CompanyPrototype>(company.CompanyName, out var prototype))
            color = prototype.Color;

        _wgContactIcons.Add(new WgContactIcon(kind, uiPosition * UIScale, heading, color));
        return true;
    }

    /// <summary>What a grid is, from what the client knows of it.</summary>
    private WgContactKind WgContactKindOf(EntityUid grid)
    {
        if (EntManager.HasComponent<WGAsteroidComponent>(grid))
            return WgContactKind.Asteroid;
        if (EntManager.HasComponent<SpaceDebrisComponent>(grid))
            return WgContactKind.Wreck;

        var playerShip = EntManager.HasComponent<ShuttleDeedComponent>(grid)
                         || EntManager.TryGetComponent(grid, out IFFComponent? iff) && (iff.Flags & IFFFlags.IsPlayerShuttle) != 0;
        if (playerShip || _wgEncounterGrids.Contains(EntManager.GetNetEntity(grid)))
            return WgContactKind.Ship;
        if (EntManager.HasComponent<StationMemberComponent>(grid) || EntManager.HasComponent<ProtectedGridComponent>(grid))
            return WgContactKind.Station;

        // Anything else that can move is a ship of some sort; a fixed grid is a structure.
        return EntManager.TryGetComponent(grid, out PhysicsComponent? body) && body.BodyType == BodyType.Static
            ? WgContactKind.Station
            : WgContactKind.Ship;
    }

    /// <summary>Notes which grids are encounter ships, before the contacts are drawn.</summary>
    private void WgGatherEncounterGrids()
    {
        _wgEncounterGrids.Clear();
        foreach (var marker in EntManager.System<WGEncounterMarkerClientSystem>().Markers)
            _wgEncounterGrids.Add(marker.Grid);
    }

    /// <summary>Draws the icons queued this frame, unless the scope is filling grids, then clears them.</summary>
    private void WgDrawContactIcons(DrawingHandleScreen handle, bool draw)
    {
        if (draw)
        {
            var size = RadarBlipSize * 0.45f * UIScale;
            foreach (var icon in _wgContactIcons)
                WgDrawContactIcon(handle, icon, size);
        }

        _wgContactIcons.Clear();
    }

    private void WgDrawContactIcon(DrawingHandleScreen handle, WgContactIcon icon, float size)
    {
        var p = icon.Position;
        switch (icon.Kind)
        {
            case WgContactKind.Ship:
            {
                // A chevron: its point is the bow.
                var forward = icon.Heading * size;
                var side = new Vector2(-icon.Heading.Y, icon.Heading.X) * size * 0.75f;
                _wgIconVertices[0] = p + forward;
                _wgIconVertices[1] = p - forward * 0.8f + side;
                _wgIconVertices[2] = p - forward * 0.35f;
                _wgIconVertices[3] = p - forward * 0.8f - side;
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _wgIconVertices.AsSpan(0, 4), icon.Color);
                break;
            }
            case WgContactKind.Station:
            {
                // A hollow square with a dot in it.
                var half = size * 0.75f;
                handle.DrawRect(new UIBox2(p.X - half, p.Y - half, p.X + half, p.Y + half), icon.Color, false);
                handle.DrawRect(new UIBox2(p.X - half * 0.3f, p.Y - half * 0.3f, p.X + half * 0.3f, p.Y + half * 0.3f), icon.Color);
                break;
            }
            case WgContactKind.Asteroid:
            {
                // A rough hexagon outline.
                for (var i = 0; i < 6; i++)
                {
                    var angle = i * MathF.Tau / 6f;
                    var reach = size * (i % 2 == 0 ? 0.8f : 0.6f);
                    _wgIconVertices[i] = p + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * reach;
                }

                _wgIconVertices[6] = _wgIconVertices[0];
                handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, _wgIconVertices.AsSpan(0, 7), icon.Color);
                break;
            }
            case WgContactKind.Wreck:
            {
                // A cross, as broken as it looks.
                var half = size * 0.65f;
                handle.DrawLine(p + new Vector2(-half, -half), p + new Vector2(half, half), icon.Color);
                handle.DrawLine(p + new Vector2(-half, half), p + new Vector2(half, -half), icon.Color);
                break;
            }
        }
    }

    private RadarModeButton? _wgAllNamesButton;
    private bool _wgAllNames;

    /// <summary>Adds the toggle that names every contact on the scope, among the radar's mode buttons.</summary>
    private void AddWgAllNamesButton(BoxContainer buttons)
    {
        _wgAllNames = _wgSettingsCfg.GetCVar(WolfgateCVars.RadarAllNames);
        var button = _wgAllNamesButton = CreateRadarModeButton(RadarModeButtonIcon.WgAllNames);
        button.ToolTip = Loc.GetString("wg-radar-all-names-tooltip");
        button.ToggleMode = true;
        button.Pressed = _wgAllNames;
        button.OnToggled += args =>
        {
            _wgAllNames = args.Pressed;
            _wgSettingsCfg.SetCVar(WolfgateCVars.RadarAllNames, args.Pressed);
            _wgSettingsCfg.SaveToFile();
        };
        buttons.AddChild(button);
    }

    public sealed partial class RadarModeButton
    {
        /// <summary>Draws the every-contact-named icon: three blips, each with a line of text.</summary>
        private void WgDrawAllNames(DrawingHandleScreen handle, Vector2 center, float radius, Color ink)
        {
            for (var row = -1; row <= 1; row++)
            {
                var y = center.Y + row * radius * 0.55f;
                handle.DrawRect(new UIBox2(center.X - radius * 0.8f, y - radius * 0.12f, center.X - radius * 0.56f, y + radius * 0.12f), ink);
                handle.DrawLine(new Vector2(center.X - radius * 0.35f, y), new Vector2(center.X + radius * 0.8f, y), ink);
            }
        }
    }
}
