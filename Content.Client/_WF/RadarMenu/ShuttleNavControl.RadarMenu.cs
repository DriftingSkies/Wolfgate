using System.Numerics;
using Content.Client._WF.Planets;
using Content.Client._WF.RadarMenu;
using Content.Shared._Mono.Detection;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Hailing;
using Content.Shared._WF.RadarMenu;
using Content.Shared._WF.Waypoints;
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
    private const float WfMenuPickRadius = 18f;

    /// <summary>How far short of a contact's hull, in metres, the autopilot stops.</summary>
    private const float WfApproachStandoff = 30f;

    private WFRadarContextMenu? _wfMenu;

    /// <summary>
    /// Opens the menu for the point a left click landed on, given relative to this control in UI units. False on a
    /// radar with no console or position.
    /// </summary>
    public bool WfOpenRadarMenu(Vector2 relative)
    {
        // Only a helm or mass scanner built into a ship or station has the menu: not the admin radar, an admin ghost's
        // scanner or a handheld one, whose clicks are for looking around.
        if (_wfWaypointKey == null
            || WfCursorWorldPosition(relative) is not { } point
            || !EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console)
            || !console.Anchored
            || console.MapID == MapId.Nullspace)
            return false;

        _wfMenu?.Close();
        var pick = WfMenuPickRadius * UIScale / MathF.Max(MinimapScale, 0.0001f);
        var from = WfScannerWorldPosition() ?? point;
        var canFly = _wfWaypointKey != null && _wfWaypointCanFly;
        var canMark = _wfWaypointKey != null && console.GridUid != null;

        WFRadarContextMenu menu;
        if (WfWaypointAt(console, point, pick) is { } waypoint)
        {
            menu = new WFRadarContextMenu(waypoint.Name, WfReadout(from, waypoint.Position));
            menu.AddKey(Loc.GetString("wf-radar-menu-fly-waypoint"), canFly ? () => WfFlyTo(waypoint.Position) : null);
            menu.AddKey(Loc.GetString("wf-radar-menu-delete-waypoint"),
                canMark ? () => WfSendWaypointMessage(new WFWaypointRemoveMessage(waypoint.Id)) : null);
            WfCommonKeys(menu, waypoint.Position, canFly);
        }
        else if (WfContactAt(console, point, pick, out var name) is { } contact)
        {
            var centre = WfContactCentre(contact);
            menu = new WFRadarContextMenu(name, WfReadout(from, centre));
            var net = EntManager.GetNetEntity(contact);
            // The side to stop on is read when the key is pressed: the ship may have moved round since the menu opened.
            menu.AddKey(Loc.GetString("wf-radar-menu-approach"),
                canFly ? () => WfFlyTo(WfApproachPoint(contact, WfScannerWorldPosition() ?? from)) : null);
            menu.AddKey(Loc.GetString("wf-radar-menu-dock-at"), canFly ? () => WfShowDocks(menu, net) : null, closes: false);
            menu.AddKey(Loc.GetString("wf-radar-menu-hail"), canFly ? () => WfSendWaypointMessage(new HailRequestMessage(net)) : null);
            menu.AddKey(Loc.GetString("wf-radar-menu-mark-contact"),
                canMark ? () => WfSendWaypointMessage(new WFWaypointAddMessage(name, centre)) : null);
            WfCommonKeys(menu, centre, canFly);
        }
        else
        {
            menu = new WFRadarContextMenu(Loc.GetString("wf-radar-menu-position"), WfReadout(from, point));
            menu.AddKey(Loc.GetString("wf-radar-menu-fly-here"), canFly ? () => WfFlyTo(point) : null);
            menu.AddKey(Loc.GetString("wf-radar-menu-mark-here"),
                canMark ? () => WfSendWaypointMessage(new WFWaypointAddMessage(string.Empty, point)) : null);
            WfCommonKeys(menu, point, canFly);
        }

        _wfMenu = menu;
        menu.OnClosed(() =>
        {
            if (_wfMenu == menu)
                _wfMenu = null;
        });
        menu.OpenAtMouse();
        return true;
    }

    /// <summary>
    /// Opens the menu for a world clicked on the radar: its signals, autopilot into its orbit, and a waypoint on it.
    /// False on a radar without the menu, which lists the signals at once instead.
    /// </summary>
    private bool WfOpenWorldMenu(EntityUid body)
    {
        if (_wfWaypointKey == null
            || !EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console)
            || !console.Anchored
            || console.MapID == MapId.Nullspace
            || !EntManager.TryGetComponent<TransformComponent>(body, out var bodyXform)
            || bodyXform.MapID != console.MapID)
            return false;

        _wfMenu?.Close();
        var centre = _transform.GetWorldPosition(bodyXform);
        var from = WfScannerWorldPosition() ?? centre;
        var canFly = _wfWaypointCanFly;
        var canMark = console.GridUid != null;
        var name = EntManager.GetComponent<MetaDataComponent>(body).EntityName;
        var net = EntManager.GetNetEntity(body);

        var menu = new WFRadarContextMenu(name, WfReadout(from, centre));
        menu.AddKey(Loc.GetString("wf-radar-menu-world-signals"), () => WfShowPlanetSignals(body));
        menu.AddKey(Loc.GetString("wf-radar-menu-orbit"), canFly && EntManager.HasComponent<WFSectorPlanetComponent>(body)
            ? () => WfRequestOrbit(body, net, name)
            : null);
        menu.AddKey(Loc.GetString("wf-radar-menu-mark-contact"),
            canMark ? () => WfSendWaypointMessage(new WFWaypointAddMessage(name, centre)) : null);
        WfCommonKeys(menu, centre, canFly);

        _wfMenu = menu;
        menu.OnClosed(() =>
        {
            if (_wfMenu == menu)
                _wfMenu = null;
        });
        menu.OpenAtMouse();
        return true;
    }

    /// <summary>Sends the autopilot into a world's orbit, asking first for a world nobody is cleared to visit.</summary>
    private void WfRequestOrbit(EntityUid body, NetEntity net, string name)
    {
        if (EntManager.TryGetComponent<WFSectorPlanetComponent>(body, out var sector) && !sector.Sanctioned)
        {
            _wfOrbitConfirm ??= new WFUnsanctionedOrbitConfirmWindow();
            _wfOrbitConfirm.Ask(name, () => WfSendWaypointMessage(new WFAutoOrbitRequestMessage(net, true)));
            return;
        }

        WfSendWaypointMessage(new WFAutoOrbitRequestMessage(net, false));
    }

    private WFUnsanctionedOrbitConfirmWindow? _wfOrbitConfirm;

    /// <summary>Keys every menu has: copy the coordinates, recentre a panned radar, cancel the autopilot.</summary>
    private void WfCommonKeys(WFRadarContextMenu menu, Vector2 position, bool canFly)
    {
        menu.AddKey(Loc.GetString("wf-radar-menu-copy"), () =>
            IoCManager.Resolve<IClipboardManager>().SetText(
                Loc.GetString("wf-radar-menu-copy-text", ("x", $"{position.X:0.0}"), ("y", $"{position.Y:0.0}"))));
        if (_wasPanned)
            menu.AddKey(Loc.GetString("wf-radar-menu-recentre"), WfRecentre);
        if (canFly)
            menu.AddKey(Loc.GetString("wf-radar-menu-cancel-autopilot"), () => WfSendWaypointMessage(new WFAutopilotCancelMessage()));
    }

    /// <summary>Asks the server which docks on a grid this ship fits, and lists them in the menu when they come.</summary>
    private void WfShowDocks(WFRadarContextMenu menu, NetEntity grid)
    {
        menu.NewPage(Loc.GetString("wf-radar-menu-docks-checking"));
        menu.AddKey(Loc.GetString("wf-radar-menu-dock-any"), () => WfSendWaypointMessage(new WFAutoDockRequestMessage(grid, null)));
        // The server answers each console at most twice a second; a request it passed over can be sent again.
        menu.AddKey(Loc.GetString("wf-radar-menu-docks-retry"),
            () => WfSendWaypointMessage(new WFAutoDockOptionsRequestMessage(grid)), closes: false);

        var system = EntManager.System<WFRadarMenuSystem>();
        var console = _consoleEntity is { } uid ? EntManager.GetNetEntity(uid) : NetEntity.Invalid;

        void Received(WFAutoDockOptionsEvent ev)
        {
            if (ev.Console != console || ev.Grid != grid)
                return;

            system.DockOptionsReceived -= Received;
            menu.NewPage(ev.Docks.Count == 0
                ? Loc.GetString("wf-radar-menu-docks-none")
                : Loc.GetString("wf-radar-menu-docks-title", ("count", ev.Docks.Count)));
            if (ev.Docks.Count > 0)
                menu.AddKey(Loc.GetString("wf-radar-menu-dock-any"), () => WfSendWaypointMessage(new WFAutoDockRequestMessage(grid, null)));
            foreach (var dock in ev.Docks)
            {
                var target = dock.Dock;
                menu.AddKey(Loc.GetString("wf-radar-menu-dock-option", ("name", dock.Name), ("distance", WfDistance(dock.Distance))),
                    () => WfSendWaypointMessage(new WFAutoDockRequestMessage(grid, target)));
            }
        }

        system.DockOptionsReceived += Received;
        menu.OnClosed(() => system.DockOptionsReceived -= Received);
        WfSendWaypointMessage(new WFAutoDockOptionsRequestMessage(grid));
    }

    /// <summary>The nearest waypoint of the console's grid within reach of a world point on this map.</summary>
    private WFWaypoint? WfWaypointAt(TransformComponent console, Vector2 point, float reach)
    {
        if (console.MapUid is not { } map || !EntManager.TryGetComponent<WFWaypointsComponent>(console.GridUid, out var stored))
            return null;

        var mapNet = EntManager.GetNetEntity(map);
        WFWaypoint? best = null;
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
    private EntityUid? WfContactAt(TransformComponent console, Vector2 point, float reach, out string name)
    {
        name = string.Empty;
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var grid in _grids)
        {
            var uid = grid.Owner;
            if (uid == console.GridUid || EntManager.HasComponent<MapComponent>(uid) || !WfContactShown(uid))
                continue;

            var onHull = _transform.GetWorldMatrix(uid) is var matrix
                         && Matrix3x2.Invert(matrix, out var inverse)
                         && grid.Comp.LocalAABB.Contains(Vector2.Transform(point, inverse));
            var distance = onHull ? 0f : Vector2.Distance(WfContactCentre(uid), point);
            if (distance > reach || distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        if (best is { } found)
            name = WfContactName(found);
        return best;
    }

    /// <summary>Whether the radar shows a grid at all: drawable, and not a hidden contact it has not detected.</summary>
    private bool WfContactShown(EntityUid uid)
    {
        if (!EntManager.TryGetComponent<PhysicsComponent>(uid, out var body))
            return false;

        EntManager.TryGetComponent<IFFComponent>(uid, out var iff);
        return _shuttles.CanDraw(uid, body, iff)
               && !(GetGridDetected(uid) == DetectionLevel.Undetected && iff != null && (iff.Flags & IFFFlags.HideLabel) != 0);
    }

    /// <summary>A contact's name as the radar labels it: hidden ones by their signature, garbled past the Lattice.</summary>
    private string WfContactName(EntityUid uid)
    {
        EntManager.TryGetComponent<IFFComponent>(uid, out var iff);
        var hideLabel = iff != null && (iff.Flags & IFFFlags.HideLabel) != 0;
        var noLabel = iff != null && (iff.Flags & IFFFlags.HideLabelAlways) != 0;
        var label = noLabel ? null
            : hideLabel ? GetGridDetected(uid) == DetectionLevel.PartialDetected
                ? Loc.GetString("shuttle-console-signature-infrared")
                : _detection.HandleUnknownMassLabel(uid)
            : _shuttles.GetIFFLabel(uid, self: false, component: iff);
        return WfLatticeGarble(label, uid) ?? Loc.GetString("wf-radar-menu-contact-unknown");
    }

    private Vector2 WfContactCentre(EntityUid uid)
    {
        var centre = EntManager.TryGetComponent<PhysicsComponent>(uid, out var body) ? body.LocalCenter : Vector2.Zero;
        return Vector2.Transform(centre, _transform.GetWorldMatrix(uid));
    }

    /// <summary>A point short of a contact on the side facing us, clear of its hull.</summary>
    private Vector2 WfApproachPoint(EntityUid contact, Vector2 from)
    {
        var centre = WfContactCentre(contact);
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
        return centre + direction * (radius + WfApproachStandoff);
    }

    /// <summary>"1.2 km · 045°" from the scanner to a point.</summary>
    private static string WfReadout(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        var bearing = (MathHelper.RadiansToDegrees(MathF.Atan2(delta.X, delta.Y)) + 360f) % 360f;
        return Loc.GetString("wf-radar-menu-readout", ("distance", WfDistance(delta.Length())), ("bearing", $"{bearing:000}"));
    }

    private static string WfDistance(float metres)
    {
        return metres < 1000f
            ? Loc.GetString("wf-radar-menu-metres", ("value", $"{metres:0}"))
            : Loc.GetString("wf-radar-menu-kilometres", ("value", $"{metres / 1000f:0.0}"));
    }

    /// <summary>Puts a panned radar back on its ship.</summary>
    private void WfRecentre()
    {
        _wasPanned = false;
        Offset = TargetOffset = Vector2.Zero;
        if (_wfRadarOrigin is { } origin)
            _coordinates = origin;
    }
}
