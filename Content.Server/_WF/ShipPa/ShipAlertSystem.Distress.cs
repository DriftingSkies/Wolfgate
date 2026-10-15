using System.Numerics;
using Content.Server._WF.Encounters;
using Content.Shared.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ShipPa;
using Content.Shared.Database;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.ShipPa;

/// <summary>
/// The ship's distress call: the SOS button on the PA panel calls for help on the radio and marks the ship on every
/// radar and the sector map until it is cancelled or lapses.
/// </summary>
public sealed partial class ShipAlertSystem
{
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private void InitializeDistress()
    {
        SubscribeLocalEvent<WFEncounterCollectMarkersEvent>(OnCollectMarkers);
    }

    private void OnDistressRequest(Entity<ShuttleConsoleComponent> ent, ref ShipDistressRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        var alert = EnsureComp<ShipAlertComponent>(grid);
        if (args.Active && !alert.Distress && _timing.CurTime < alert.NextDistress)
        {
            var seconds = (int) Math.Ceiling((alert.NextDistress - _timing.CurTime).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("ship-distress-cooldown", ("seconds", seconds)), ent, args.Actor);
            return;
        }

        SetDistress(grid, args.Active, args.Actor);
    }

    /// <summary>Sends or cancels the ship's distress call. A call goes out on the radio and over the ship's own PA.</summary>
    public void SetDistress(EntityUid grid, bool active, EntityUid? user = null)
    {
        if (!Exists(grid))
            return;

        var alert = EnsureComp<ShipAlertComponent>(grid);
        if (alert.Distress == active)
            return;

        alert.Distress = active;
        Dirty(grid, alert);

        var ship = _pa.GetShipName(grid);
        if (active)
        {
            alert.DistressEnds = _timing.CurTime + alert.DistressDuration;
            alert.NextDistress = _timing.CurTime + alert.DistressCooldown;
            var position = GetShipPosition(grid).Position;
            Broadcast(grid, alert, Loc.GetString("ship-distress-radio",
                ("ship", ship),
                ("x", (int) MathF.Round(position.X)),
                ("y", (int) MathF.Round(position.Y))));
            _pa.Announce(grid, Loc.GetString("ship-distress-announcement"), alert.AnnouncementChime, color: alert.DistressColor);
        }
        else
        {
            Broadcast(grid, alert, Loc.GetString("ship-distress-radio-cancel", ("ship", ship)));
            _pa.Announce(grid, Loc.GetString("ship-distress-announcement-cancel"), alert.AnnouncementChime);
        }

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{(user is { } actor ? ToPrettyString(actor).ToString() : "Server")} {(active ? "sent" : "cancelled")} a distress call from {ToPrettyString(grid)}");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShipAlertComponent>();
        while (query.MoveNext(out var uid, out var alert))
        {
            if (!alert.Distress || _timing.CurTime < alert.DistressEnds)
                continue;

            // A lapsed call goes quietly: the radio heard it once, the crew is told it is down.
            alert.Distress = false;
            Dirty(uid, alert);
            _pa.Announce(uid, Loc.GetString("ship-distress-announcement-lapsed"), alert.AnnouncementChime);
            _adminLogger.Add(LogType.Action, LogImpact.Low, $"The distress call of {ToPrettyString(uid)} lapsed");
        }
    }

    /// <summary>Speaks a line on the distress channel in the ship's name, heard sector-wide without a telecom server.</summary>
    private void Broadcast(EntityUid grid, ShipAlertComponent alert, string text)
    {
        var voice = Spawn(null, GetShipPosition(grid));
        _meta.SetEntityName(voice, _pa.GetShipName(grid));
        EnsureComp<TelecomExemptComponent>(voice);
        _radio.SendRadioMessage(voice, text, alert.DistressChannel, voice);
        QueueDel(voice);
    }

    /// <summary>The ship's centre of mass in map space.</summary>
    private MapCoordinates GetShipPosition(EntityUid grid)
    {
        var centre = CompOrNull<PhysicsComponent>(grid)?.LocalCenter ?? Vector2.Zero;
        return _transform.ToMapCoordinates(new EntityCoordinates(grid, centre));
    }

    /// <summary>Puts every ship with a standing call on the sector markers, so radars and the sector map show it.</summary>
    private void OnCollectMarkers(ref WFEncounterCollectMarkersEvent args)
    {
        var query = EntityQueryEnumerator<ShipAlertComponent>();
        while (query.MoveNext(out var uid, out var alert))
        {
            if (!alert.Distress || TerminatingOrDeleted(uid))
                continue;

            var ship = _pa.GetShipName(uid);
            var position = GetShipPosition(uid);
            var net = GetNetEntity(uid);
            args.Markers.Add(new WFEncounterMarker
            {
                Encounter = net,
                Name = Loc.GetString("ship-distress-marker", ("ship", ship)),
                Ship = ship,
                Category = WFEncounterCategory.Distress,
                Icon = WFEncounterIcon.Category,
                Grid = net,
                Color = alert.DistressColor,
                Map = position.MapId,
                Position = position.Position,
                Velocity = CompOrNull<PhysicsComponent>(uid)?.LinearVelocity ?? Vector2.Zero,
            });
        }
    }
}
