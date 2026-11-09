using System.Numerics;
using Content.Server._WF.Defcon;
using Content.Server._WF.Warheads;
using Content.Server._WF.Warheads.Components;
using Content.Server.Chat.Systems;
using Content.Server.Nuke;
using Content.Shared._WF.VehicleShop;
using Content.Shared._WF.Warheads;
using Content.Shared.Maps;
using Content.Shared.Nuke;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Ordnance;

/// <summary>
/// Faction nuclear ordnance. Each faction station gets an ordnance terminal, open to its command, that sells a nuclear
/// delivery beacon; a purchase is announced to the whole sector. Any warhead armed sends DEFCON to 1 at once and holds it
/// there until it goes off or is made safe; bought, undelivered devices also weigh on the score.
/// </summary>
public sealed partial class WFOrdnanceSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WFDefconSystem _defcon = default!;

    private static readonly TimeSpan PlaceInterval = TimeSpan.FromSeconds(5);
    private static readonly Color AnnounceColor = Color.FromHex("#e0a020");
    private static readonly SoundSpecifier PurchaseSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    private TimeSpan _nextPlace;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFOrdnanceBeaconComponent, WFVehicleShopPurchasedEvent>(OnPurchased);
        SubscribeLocalEvent<WFWarheadArmedEvent>(OnWarheadArmed);
        SubscribeLocalEvent<WFDefconGatherEvent>(OnDefconGather);
        InitializeBeacon();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateBeacons();

        var now = _timing.CurTime;
        if (now < _nextPlace)
            return;

        _nextPlace = now + PlaceInterval;
        PlaceTerminals();
    }

    /// <summary>The whole sector hears that a faction has bought a nuclear device.</summary>
    private void OnPurchased(Entity<WFOrdnanceBeaconComponent> ent, ref WFVehicleShopPurchasedEvent args)
    {
        var faction = TryComp<WFOrdnanceTerminalComponent>(args.Terminal, out var terminal)
            ? Loc.GetString(terminal.Faction)
            : Loc.GetString("wf-ordnance-faction-unknown");
        ent.Comp.Faction = faction;
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("wf-ordnance-purchase-announcement", ("faction", faction), ("device", Name(ent))),
            Loc.GetString("wf-ordnance-announcement-sender"),
            true,
            PurchaseSound,
            AnnounceColor);

        // Scored at once, so the rise starts with the purchase rather than at the next tick.
        _defcon.Evaluate(_timing.CurTime);
    }

    /// <summary>A warhead armed sends DEFCON straight to 1, unless an admin holds it.</summary>
    private void OnWarheadArmed(ref WFWarheadArmedEvent args)
    {
        if (_defcon.Locked == null && _defcon.Level > 1)
            _defcon.SetLevel(1, false);
    }

    private void OnDefconGather(WFDefconGatherEvent ev)
    {
        var armed = 0;
        var stock = 0;
        var warheads = EntityQueryEnumerator<WFWarheadComponent>();
        while (warheads.MoveNext(out var uid, out var warhead))
        {
            if (warhead.State is WFWarheadState.Armed or WFWarheadState.Countdown or WFWarheadState.Final)
                armed++;
            else if (HasComp<WFWarheadKeyLockComponent>(uid))
                stock++;
        }

        var nukes = EntityQueryEnumerator<NukeComponent>();
        while (nukes.MoveNext(out _, out var nuke))
        {
            if (nuke.Status == NukeStatus.ARMED)
                armed++;
        }

        var beacons = EntityQueryEnumerator<WFOrdnanceBeaconComponent>();
        while (beacons.MoveNext(out _, out _))
        {
            stock++;
        }

        if (armed > 0)
        {
            ev.Add(armed * ev.Profile.NuclearArmed, Loc.GetString("wf-ordnance-defcon-armed", ("count", armed)));
            ev.Floor = 1;
        }

        if (stock > 0)
        {
            ev.Add(stock * ev.Profile.NuclearHeld, Loc.GetString("wf-ordnance-defcon-stock", ("count", stock)));
            ev.Floor = Math.Min(ev.Floor, ev.Profile.NuclearHeldFloor);
        }
    }

    /// <summary>Puts each faction station's terminal down once its grid has floor, unless one is there already.</summary>
    private void PlaceTerminals()
    {
        var stations = EntityQueryEnumerator<WFOrdnanceStationComponent, MapGridComponent>();
        while (stations.MoveNext(out var uid, out var station, out var grid))
        {
            if (station.Placed)
                continue;

            var existing = EntityQueryEnumerator<WFOrdnanceTerminalComponent, TransformComponent>();
            var found = false;
            while (existing.MoveNext(out _, out _, out var xform))
            {
                found |= xform.GridUid == uid;
            }

            if (found)
            {
                station.Placed = true;
                continue;
            }

            if (FreeTile(uid, grid) is not { } tile)
                continue;

            station.Placed = true;
            Spawn(station.Terminal, _maps.GridTileToLocal(uid, grid, tile));
            Log.Info($"Placed the ordnance terminal {station.Terminal} on {ToPrettyString(uid)}.");
        }
    }

    /// <summary>
    /// A free floor tile beside the computer nearest the middle of the grid, where the station's consoles are; else the
    /// free tile nearest the middle.
    /// </summary>
    private Vector2i? FreeTile(EntityUid grid, MapGridComponent gridComp)
    {
        var centre = gridComp.LocalAABB.Center;
        Vector2i? beside = null;
        Vector2i? anywhere = null;
        var besideDistance = float.MaxValue;
        var anywhereDistance = float.MaxValue;
        foreach (var tile in _maps.GetAllTiles(grid, gridComp))
        {
            if (_turf.IsSpace(tile) || _turf.IsTileBlocked(tile, Content.Shared.Physics.CollisionGroup.MachineMask)
                || Anchored(grid, gridComp, tile.GridIndices))
                continue;

            var distance = (new Vector2(tile.GridIndices.X + 0.5f, tile.GridIndices.Y + 0.5f) - centre).LengthSquared();
            if (distance < anywhereDistance)
            {
                anywhereDistance = distance;
                anywhere = tile.GridIndices;
            }

            if (distance < besideDistance && NextToComputer(grid, gridComp, tile.GridIndices))
            {
                besideDistance = distance;
                beside = tile.GridIndices;
            }
        }

        return beside ?? anywhere;
    }

    private bool Anchored(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        return anchored.MoveNext(out _);
    }

    private bool NextToComputer(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        foreach (var offset in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
        {
            var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, tile + offset);
            while (anchored.MoveNext(out var uid))
            {
                if (HasComp<Content.Server.Construction.Components.ComputerComponent>(uid))
                    return true;
            }
        }

        return false;
    }
}
