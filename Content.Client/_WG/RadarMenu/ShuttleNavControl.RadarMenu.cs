using System.Numerics;
using Content.Client._WG.Planets;
using Content.Client._WG.RadarMenu;
using Content.Shared._Mono.Detection;
using Content.Shared._WG.Planets;
using Content.Shared._WG.Hailing;
using Content.Shared._WG.RadarMenu;
using Content.Shared._WG.Waypoints;
using Content.Shared.Shuttles.Components;
using Robust.Client.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// The radar's click menu (opened from the left-click handling in ShuttleNavControl.PlanetSignals.cs): what can be done with the spot, contact or waypoint under the cursor. A helm flies
/// there, docks, hails and cancels its autopilot; a helm or mass scanner marks and deletes waypoints; any radar copies
/// coordinates and recentres.
/// </summary>
public partial class ShuttleNavControl
{
    /// <summary>How near, in pixels at UI scale 1, a click must land to a contact's centre or a waypoint to pick it.</summary>
    private const float WgMenuPickRadius = 18f;

    /// <summary>How far short of a contact's hull, in metres, the autopilot stops.</summary>
    private const float WgApproachStandoff = 30f;

    private WGRadarContextMenu? _wgMenu;

    /// <summary>
    /// Opens the menu for the point a left click landed on, given relative to this control in UI units. False on a
    /// radar with no console or position.
    /// </summary>
    public bool WgOpenRadarMenu(Vector2 relative)
    {
        // Only a helm or mass scanner built into a ship or station has the menu: not the admin radar, an admin ghost's
        // scanner or a handheld one, whose clicks are for looking around.
        if (_wgWaypointKey == null
            || WgCursorWorldPosition(relative) is not { } point
            || !EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console)
            || !console.Anchored
            || console.MapID == MapId.Nullspace)
            return false;

        _wgMenu?.Close();
        var pick = WgMenuPickRadius * UIScale / MathF.Max(MinimapScale, 0.0001f);
        var from = WgScannerWorldPosition() ?? point;
        var canFly = _wgWaypointKey != null && _wgWaypointCanFly;
        var canMark = _wgWaypointKey != null && console.GridUid != null;

        WGRadarContextMenu menu;
        if (WgWaypointAt(console, point, pick) is { } waypoint)
        {
            menu = new WGRadarContextMenu(waypoint.Name, WgReadout(from, waypoint.Position));
            menu.AddKey(Loc.GetString("wg-radar-menu-fly-waypoint"), canFly ? () => WgFlyTo(waypoint.Position) : null);
            menu.AddKey(Loc.GetString("wg-radar-menu-delete-waypoint"),
                canMark ? () => WgSendWaypointMessage(new WGWaypointRemoveMessage(waypoint.Id)) : null);
            WgCommonKeys(menu, waypoint.Position, canFly);
        }
        else if (WgContactAt(console, point, pick, out var name) is { } contact)
        {
            var centre = WgContactCentre(contact);
            menu = new WGRadarContextMenu(name, WgReadout(from, centre));
            var net = EntManager.GetNetEntity(contact);
            // The side to stop on is read when the key is pressed: the ship may have moved round since the menu opened.
            menu.AddKey(Loc.GetString("wg-radar-menu-approach"),
                canFly ? () => WgFlyTo(WgApproachPoint(contact, WgScannerWorldPosition() ?? from)) : null);
            menu.AddKey(Loc.GetString("wg-radar-menu-dock-at"), canFly ? () => WgShowDocks(menu, net) : null, closes: false);
            menu.AddKey(Loc.GetString("wg-radar-menu-hail"), canFly ? () => WgSendWaypointMessage(new HailRequestMessage(net)) : null);
            menu.AddKey(Loc.GetString("wg-radar-menu-mark-contact"),
                canMark ? () => WgSendWaypointMessage(new WGWaypointAddMessage(name, centre)) : null);
            WgCommonKeys(menu, centre, canFly);
        }
        else
        {
            menu = new WGRadarContextMenu(Loc.GetString("wg-radar-menu-position"), WgReadout(from, point));
            menu.AddKey(Loc.GetString("wg-radar-menu-fly-here"), canFly ? () => WgFlyTo(point) : null);
            menu.AddKey(Loc.GetString("wg-radar-menu-mark-here"),
                canMark ? () => WgSendWaypointMessage(new WGWaypointAddMessage(string.Empty, point)) : null);
            WgCommonKeys(menu, point, canFly);
        }

        _wgMenu = menu;
        menu.OnClosed(() =>
        {
            if (_wgMenu == menu)
                _wgMenu = null;
        });
        menu.OpenAtMouse();
        return true;
    }

    /// <summary>
    /// Opens the menu for a world clicked on the radar: its signals, autopilot into its orbit, and a waypoint on it.
    /// False on a radar without the menu, which lists the signals at once instead.
    /// </summary>
    private bool WgOpenWorldMenu(EntityUid body)
    {
        if (_wgWaypointKey == null
            || !EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console)
            || !console.Anchored
            || console.MapID == MapId.Nullspace
            || !EntManager.TryGetComponent<TransformComponent>(body, out var bodyXform)
            || bodyXform.MapID != console.MapID)
            return false;

        _wgMenu?.Close();
        var centre = _transform.GetWorldPosition(bodyXform);
        var from = WgScannerWorldPosition() ?? centre;
        var canFly = _wgWaypointCanFly;
        var canMark = console.GridUid != null;
        var name = EntManager.GetComponent<MetaDataComponent>(body).EntityName;
        var net = EntManager.GetNetEntity(body);

        var menu = new WGRadarContextMenu(name, WgReadout(from, centre));
        menu.AddKey(Loc.GetString("wg-radar-menu-world-signals"), () => WgShowPlanetSignals(body));
        menu.AddKey(Loc.GetString("wg-radar-menu-orbit"), canFly && EntManager.HasComponent<WGSectorPlanetComponent>(body)
            ? () => WgRequestOrbit(body, net, name)
            : null);
        menu.AddKey(Loc.GetString("wg-radar-menu-mark-contact"),
            canMark ? () => WgSendWaypointMessage(new WGWaypointAddMessage(name, centre)) : null);
        WgCommonKeys(menu, centre, canFly);

        _wgMenu = menu;
        menu.OnClosed(() =>
        {
            if (_wgMenu == menu)
                _wgMenu = null;
        });
        menu.OpenAtMouse();
        return true;
    }

    /// <summary>Sends the autopilot into a world's orbit, asking first for a world nobody is cleared to visit.</summary>
    private void WgRequestOrbit(EntityUid body, NetEntity net, string name)
    {
        if (EntManager.TryGetComponent<WGSectorPlanetComponent>(body, out var sector) && !sector.Sanctioned)
        {
            _wgOrbitConfirm ??= new WGUnsanctionedOrbitConfirmWindow();
            _wgOrbitConfirm.Ask(name, () => WgSendWaypointMessage(new WGAutoOrbitRequestMessage(net, true)));
            return;
        }

        WgSendWaypointMessage(new WGAutoOrbitRequestMessage(net, false));
    }

    private WGUnsanctionedOrbitConfirmWindow? _wgOrbitConfirm;

    /// <summary>Keys every menu has: copy the coordinates, recentre a panned radar, cancel the autopilot.</summary>
    private void WgCommonKeys(WGRadarContextMenu menu, Vector2 position, bool canFly)
    {
        menu.AddKey(Loc.GetString("wg-radar-menu-copy"), () =>
            IoCManager.Resolve<IClipboardManager>().SetText(
                Loc.GetString("wg-radar-menu-copy-text", ("x", $"{position.X:0.0}"), ("y", $"{position.Y:0.0}"))));
        if (_wasPanned)
            menu.AddKey(Loc.GetString("wg-radar-menu-recentre"), WgRecentre);
        if (canFly)
            menu.AddKey(Loc.GetString("wg-radar-menu-cancel-autopilot"), () => WgSendWaypointMessage(new WGAutopilotCancelMessage()));
    }

    /// <summary>Asks the server which docks on a grid this ship fits, and lists them in the menu when they come.</summary>
    private void WgShowDocks(WGRadarContextMenu menu, NetEntity grid)
    {
        menu.NewPage(Loc.GetString("wg-radar-menu-docks-checking"));
        menu.AddKey(Loc.GetString("wg-radar-menu-dock-any"), () => WgSendWaypointMessage(new WGAutoDockRequestMessage(grid, null)));
        // The server answers each console at most twice a second; a request it passed over can be sent again.
        menu.AddKey(Loc.GetString("wg-radar-menu-docks-retry"),
            () => WgSendWaypointMessage(new WGAutoDockOptionsRequestMessage(grid)), closes: false);

        var system = EntManager.System<WGRadarMenuSystem>();
        var console = _consoleEntity is { } uid ? EntManager.GetNetEntity(uid) : NetEntity.Invalid;

        void Received(WGAutoDockOptionsEvent ev)
        {
            if (ev.Console != console || ev.Grid != grid)
                return;

            system.DockOptionsReceived -= Received;
            menu.NewPage(ev.Docks.Count == 0
                ? Loc.GetString("wg-radar-menu-docks-none")
                : Loc.GetString("wg-radar-menu-docks-title", ("count", ev.Docks.Count)));
            if (ev.Docks.Count > 0)
                menu.AddKey(Loc.GetString("wg-radar-menu-dock-any"), () => WgSendWaypointMessage(new WGAutoDockRequestMessage(grid, null)));
            foreach (var dock in ev.Docks)
            {
                var target = dock.Dock;
                menu.AddKey(Loc.GetString("wg-radar-menu-dock-option", ("name", dock.Name), ("distance", WgDistance(dock.Distance))),
                    () => WgSendWaypointMessage(new WGAutoDockRequestMessage(grid, target)));
            }
        }

        system.DockOptionsReceived += Received;
        menu.OnClosed(() => system.DockOptionsReceived -= Received);
        WgSendWaypointMessage(new WGAutoDockOptionsRequestMessage(grid));
    }

    /// <summary>The nearest waypoint of the console's grid within reach of a world point on this map.</summary>
    private WGWaypoint? WgWaypointAt(TransformComponent console, Vector2 point, float reach)
    {
        if (console.MapUid is not { } map || !EntManager.TryGetComponent<WGWaypointsComponent>(console.GridUid, out var stored))
            return null;

        var mapNet = EntManager.GetNetEntity(map);
        WGWaypoint? best = null;
        var bestDistance = reach;
        foreach (var waypoint in stored.Waypoints)
        {
            var distance = Vector2.Distance(waypoint.Position, point);
            if (waypoint.Map != mapNet || distance > bestDistance)
                continue;
            best = waypoint;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// The contact under a world point: one whose hull it lies on, else the nearest whose centre is within reach. Only
    /// contacts this radar draws count, named as it labels them.
    /// </summary>
    private EntityUid? WgContactAt(TransformComponent console, Vector2 point, float reach, out string name)
    {
        name = string.Empty;
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var grid in _grids)
        {
            var uid = grid.Owner;
            if (uid == console.GridUid || EntManager.HasComponent<MapComponent>(uid) || !WgContactShown(uid))
                continue;

            var onHull = _transform.GetWorldMatrix(uid) is var matrix
                         && Matrix3x2.Invert(matrix, out var inverse)
                         && grid.Comp.LocalAABB.Contains(Vector2.Transform(point, inverse));
            var distance = onHull ? 0f : Vector2.Distance(WgContactCentre(uid), point);
            if (distance > reach || distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        if (best is { } found)
            name = WgContactName(found);
        return best;
    }

    /// <summary>Whether the radar shows a grid at all: drawable, and not a hidden contact it has not detected.</summary>
    private bool WgContactShown(EntityUid uid)
    {
        if (!EntManager.TryGetComponent<PhysicsComponent>(uid, out var body))
            return false;

        EntManager.TryGetComponent<IFFComponent>(uid, out var iff);
        return _shuttles.CanDraw(uid, body, iff)
               && !(GetGridDetected(uid) == DetectionLevel.Undetected && iff != null && (iff.Flags & IFFFlags.HideLabel) != 0);
    }

    /// <summary>A contact's name as the radar labels it: hidden ones by their signature, garbled past the Lattice.</summary>
    private string WgContactName(EntityUid uid)
    {
        EntManager.TryGetComponent<IFFComponent>(uid, out var iff);
        var hideLabel = iff != null && (iff.Flags & IFFFlags.HideLabel) != 0;
        var noLabel = iff != null && (iff.Flags & IFFFlags.HideLabelAlways) != 0;
        var label = noLabel ? null
            : hideLabel ? GetGridDetected(uid) == DetectionLevel.PartialDetected
                ? Loc.GetString("shuttle-console-signature-infrared")
                : _detection.HandleUnknownMassLabel(uid)
            : _shuttles.GetIFFLabel(uid, self: false, component: iff);
        return WgLatticeGarble(label, uid) ?? Loc.GetString("wg-radar-menu-contact-unknown");
    }

    private Vector2 WgContactCentre(EntityUid uid)
    {
        var centre = EntManager.TryGetComponent<PhysicsComponent>(uid, out var body) ? body.LocalCenter : Vector2.Zero;
        return Vector2.Transform(centre, _transform.GetWorldMatrix(uid));
    }

    /// <summary>A point short of a contact on the side facing us, clear of its hull.</summary>
    private Vector2 WgApproachPoint(EntityUid contact, Vector2 from)
    {
        var centre = WgContactCentre(contact);
        // The farthest corner of the hull from that centre, which need not be the middle of its bounds.
        var radius = 0f;
        if (EntManager.TryGetComponent<MapGridComponent>(contact, out var grid))
        {
            var local = EntManager.TryGetComponent<PhysicsComponent>(contact, out var body) ? body.LocalCenter : Vector2.Zero;
            var box = grid.LocalAABB;
            radius = MathF.Max(MathF.Max((box.BottomLeft - local).Length(), (box.BottomRight - local).Length()),
                MathF.Max((box.TopLeft - local).Length(), (box.TopRight - local).Length()));
        }
        var away = from - centre;
        var direction = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : Vector2.UnitY;
        return centre + direction * (radius + WgApproachStandoff);
    }

    /// <summary>"1.2 km · 045°" from the scanner to a point.</summary>
    private static string WgReadout(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        var bearing = (MathHelper.RadiansToDegrees(MathF.Atan2(delta.X, delta.Y)) + 360f) % 360f;
        return Loc.GetString("wg-radar-menu-readout", ("distance", WgDistance(delta.Length())), ("bearing", $"{bearing:000}"));
    }

    private static string WgDistance(float metres)
    {
        return metres < 1000f
            ? Loc.GetString("wg-radar-menu-metres", ("value", $"{metres:0}"))
            : Loc.GetString("wg-radar-menu-kilometres", ("value", $"{metres / 1000f:0.0}"));
    }

    /// <summary>Puts a panned radar back on its ship.</summary>
    private void WgRecentre()
    {
        _wasPanned = false;
        Offset = TargetOffset = Vector2.Zero;
        if (_wgRadarOrigin is { } origin)
            _coordinates = origin;
    }
}
