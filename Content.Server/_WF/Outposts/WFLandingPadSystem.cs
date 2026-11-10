using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Flight;
using Content.Server._WF.Planets.Hop;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.PlanetMap;
using Content.Shared._WF.Planets;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Landing pads: a pilot with access to an outpost who asks to descend within <see cref="CaptureRange"/> of its powered
/// pad is set down on the pad, squared to the outpost, whatever the hull's lift. The hull parks static over the outpost
/// floor, untouched by it, until the pad lifts it back to orbit. A public pad, such as a Tradepost's, takes any pilot.
/// The world's pads are listed on its orbit radar map.
/// </summary>
public sealed class WFLandingPadSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private WFPlanetHopSystem _hop = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;
    [Dependency] private WFShipAccessSystem _access = default!;

    /// <summary>How far, in metres, a hull in orbit may be from a pad for it to take the hull down.</summary>
    public const float CaptureRange = 32f;

    /// <summary>How often the orbit radar maps' pad lists are brought up to date.</summary>
    private static readonly TimeSpan RadarInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _nextRadar;

    private readonly HashSet<EntityUid> _docked = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private readonly List<EntityUid> _lost = new();

    /// <summary>Where a hull would rest on a pad.</summary>
    public readonly record struct PadPlacement(Entity<WFLandingPadComponent> Pad, EntityUid Outpost, Vector2 Position, Angle Rotation);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFOrbitDescentAttemptEvent>(OnDescentAttempt);
        SubscribeLocalEvent<WFLiftoffDivertEvent>(OnLiftoffDivert);
        SubscribeLocalEvent<WFPlanetHopCompletedEvent>(OnHopCompleted);
        SubscribeLocalEvent<WFLandingPadComponent, ComponentShutdown>(OnPadShutdown);
        SubscribeLocalEvent<WFLandingPadComponent, ExaminedEvent>(OnPadExamined);
        SubscribeLocalEvent<WFLandingPadComponent, GetVerbsEvent<AlternativeVerb>>(OnPadVerbs);
        SubscribeLocalEvent<WFPadDockedComponent, ComponentShutdown>(OnDockedShutdown);
        SubscribeLocalEvent<WFPadGuidedComponent, ComponentShutdown>(OnGuidedShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // A hop called off or lost leaves its pad free again.
        _lost.Clear();
        var guided = EntityQueryEnumerator<WFPadGuidedComponent>();
        while (guided.MoveNext(out var uid, out _))
        {
            if (!_hop.IsHopping(uid))
                _lost.Add(uid);
        }

        foreach (var uid in _lost)
        {
            RemComp<WFPadGuidedComponent>(uid);
        }

        if (_timing.CurTime >= _nextRadar)
        {
            _nextRadar = _timing.CurTime + RadarInterval;
            UpdateRadar();
        }
    }

    /// <summary>Lists every world's anchored pads on its orbit radar map, sending only the lists that changed.</summary>
    public void UpdateRadar()
    {
        var byGround = new Dictionary<EntityUid, List<WFPlanetMapPad>>();
        var pads = EntityQueryEnumerator<WFLandingPadComponent, TransformComponent>();
        while (pads.MoveNext(out _, out var pad, out var xform))
        {
            if (!xform.Anchored || xform.MapUid is not { } map || xform.GridUid is not { } grid)
                continue;

            if (!byGround.TryGetValue(map, out var list))
                byGround[map] = list = new List<WFPlanetMapPad>();
            list.Add(new WFPlanetMapPad(_transform.GetWorldPosition(xform), pad.Size, _transform.GetWorldRotation(grid), pad.Public));
        }

        var networks = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (networks.MoveNext(out var network))
        {
            if (!TryComp<WFPlanetMapComponent>(network.OrbitMap, out var radar))
                continue;

            var list = byGround.GetValueOrDefault(network.GroundMap) ?? new List<WFPlanetMapPad>();
            if (list.SequenceEqual(radar.Pads))
                continue;

            radar.Pads = list;
            DirtyField(network.OrbitMap, radar, nameof(WFPlanetMapComponent.Pads));
        }
    }

    /// <summary>Whether the pilot may land on the outpost's pads: its owner, or anyone on its Access tab's allow list.</summary>
    public bool CanLand(EntityUid pilot, Entity<WFOutpostComponent> outpost)
    {
        if (_outposts.IsOwnerOf(pilot, outpost.Comp))
            return true;

        return TryComp<WFShipAccessComponent>(outpost, out var access)
               && _access.TryGetCard(pilot, out var card)
               && _access.TryGetKey(card, out var key)
               && _access.TryGetEntry(access, key, out _);
    }

    /// <summary>
    /// The nearest pad below the hull, in range and on an outpost the pilot may use, that takes the hull, with where it
    /// would rest. <paramref name="reason"/> says why the nearest pad can't take it, and is null when none is in range.
    /// </summary>
    public bool TryFindPad(EntityUid hull, EntityUid ground, EntityUid pilot, [NotNullWhen(true)] out PadPlacement? placement,
        out string? reason)
    {
        placement = null;
        reason = null;
        if (!TryComp<MapGridComponent>(hull, out var hullGrid))
            return false;

        var centre = Vector2.Transform(hullGrid.LocalAABB.Center, _transform.GetWorldMatrix(hull));
        var candidates = new List<(float Distance, Entity<WFLandingPadComponent> Pad, EntityUid Outpost)>();
        var pads = EntityQueryEnumerator<WFLandingPadComponent, TransformComponent>();
        while (pads.MoveNext(out var uid, out var pad, out var xform))
        {
            if (xform.MapUid != ground || !xform.Anchored || xform.GridUid is not { } grid)
                continue;

            if (!pad.Public && (!TryComp<WFOutpostComponent>(grid, out var outpost) || !CanLand(pilot, (grid, outpost))))
                continue;

            var distance = (_transform.GetWorldPosition(xform) - centre).Length();
            if (distance <= CaptureRange)
                candidates.Add((distance, (uid, pad), grid));
        }

        foreach (var (_, pad, padGrid) in candidates.OrderBy(c => c.Distance))
        {
            if (TryPlace(hull, hullGrid, pad, padGrid, out placement, out var refusal))
            {
                reason = null;
                return true;
            }

            reason ??= refusal;
        }

        return false;
    }

    /// <summary>
    /// Where the hull would rest on the pad, squared to the grid it stands on (an outpost, or the ground), if it fits and
    /// the pad is free.
    /// </summary>
    public bool TryPlace(EntityUid hull, MapGridComponent hullGrid, Entity<WFLandingPadComponent> pad, EntityUid outpost,
        [NotNullWhen(true)] out PadPlacement? placement, out string reason, bool needPower = true)
    {
        placement = null;
        if (pad.Comp.Guiding || pad.Comp.Parked is { } parked && !TerminatingOrDeleted(parked))
        {
            reason = Loc.GetString("wf-pad-occupied");
            return false;
        }

        if (needPower && !_power.IsPowered(pad))
        {
            reason = Loc.GetString("wf-pad-unpowered");
            return false;
        }

        _docked.Clear();
        _shuttle.GetAllDockedShuttles(hull, _docked);
        _docked.Remove(hull);
        if (_docked.Count > 0)
        {
            reason = Loc.GetString("wf-pad-docked");
            return false;
        }

        var outpostGrid = Comp<MapGridComponent>(outpost);
        var outpostRotation = _transform.GetWorldRotation(outpost);
        var turns = (int) Math.Round((_transform.GetWorldRotation(hull) - outpostRotation).Theta / (Math.PI / 2));
        var relative = new Angle((turns % 4 + 4) % 4 * Math.PI / 2);

        // Squared to the outpost and snapped to its tiles, the hull's middle as near the pad's as it goes.
        var turned = new Box2Rotated(hullGrid.LocalAABB, relative, Vector2.Zero).CalcBoundingBox();
        var tile = _maps.TileIndicesFor(outpost, outpostGrid, Transform(pad).Coordinates);
        var origin = (Vector2) tile + new Vector2(0.5f) - turned.Center;
        origin = new Vector2(MathF.Round(origin.X), MathF.Round(origin.Y));
        var footprint = turned.Translated(origin);

        var half = pad.Comp.Size / 2;
        var square = new Box2(tile.X - half, tile.Y - half, tile.X + half + 1, tile.Y + half + 1);
        if (!square.Enlarged(0.01f).Encloses(footprint))
        {
            reason = Loc.GetString("wf-pad-too-small", ("size", pad.Comp.Size),
                ("width", (int) MathF.Round(footprint.Width)), ("height", (int) MathF.Round(footprint.Height)));
            return false;
        }

        for (var x = (int) MathF.Floor(footprint.Left); x < (int) MathF.Ceiling(footprint.Right); x++)
        {
            for (var y = (int) MathF.Floor(footprint.Bottom); y < (int) MathF.Ceiling(footprint.Top); y++)
            {
                var anchored = _maps.GetAnchoredEntitiesEnumerator(outpost, outpostGrid, new Vector2i(x, y));
                while (anchored.MoveNext(out var ent))
                {
                    if (ent.Value != pad.Owner && Blocks(ent.Value))
                    {
                        reason = Loc.GetString("wf-pad-blocked");
                        return false;
                    }
                }
            }
        }

        var matrix = _transform.GetWorldMatrix(outpost);
        var ground = Transform(outpost).MapUid;
        _grids.Clear();
        if (ground is { } map)
            _maps.FindGridsIntersecting(map, matrix.TransformBox(footprint.Enlarged(-0.1f)), ref _grids);
        if (_grids.Any(grid => grid.Owner != outpost && grid.Owner != hull && grid.Owner != ground))
        {
            reason = Loc.GetString("wf-pad-occupied");
            return false;
        }

        placement = new PadPlacement(pad, outpost, Vector2.Transform(origin, matrix), outpostRotation + relative);
        reason = string.Empty;
        return true;
    }

    /// <summary>Starts the hull's guided descent onto the pad from orbit.</summary>
    public bool TryDescend(EntityUid hull, PadPlacement placement, [NotNullWhen(false)] out string? reason)
    {
        if (!TryComp<ShuttleComponent>(hull, out var shuttle) || Transform(placement.Pad).MapUid is not { } ground)
        {
            reason = Loc.GetString("wf-orbit-no-hull");
            return false;
        }

        if (!_hop.TryStart((hull, shuttle), ground, placement.Position, null, false, out reason,
                new WFHopExact(placement.Rotation, placement.Outpost)))
            return false;

        Guide(hull, placement.Pad);
        return true;
    }

    /// <summary>Starts lifting a hull parked on a pad back to orbit over it.</summary>
    public bool TryLaunch(Entity<WFPadDockedComponent> hull, [NotNullWhen(false)] out string? reason)
    {
        if (HasComp<WFPadGuidedComponent>(hull))
        {
            reason = Loc.GetString("wf-pad-guiding");
            return false;
        }

        if (!TryComp<WFLandingPadComponent>(hull.Comp.Pad, out var pad) || !TryComp<ShuttleComponent>(hull, out var shuttle))
        {
            reason = Loc.GetString("wf-pad-lost");
            return false;
        }

        if (!_power.IsPowered(hull.Comp.Pad))
        {
            reason = Loc.GetString("wf-pad-unpowered");
            return false;
        }

        if (OrbitAbove(Transform(hull).MapUid) is not { } orbit)
        {
            reason = Loc.GetString("wf-pad-lost");
            return false;
        }

        if (!_hop.TryStart((hull, shuttle), orbit, _transform.GetWorldPosition(hull), null, true, out reason))
            return false;

        Guide(hull, (hull.Comp.Pad, pad));
        return true;
    }

    private void OnDescentAttempt(ref WFOrbitDescentAttemptEvent args)
    {
        if (args.Handled || args.Pilot is not { } pilot)
            return;

        if (HasComp<WFPadGuidedComponent>(args.Grid))
        {
            args.Handled = true;
            args.Reason = Loc.GetString("wf-pad-guiding");
            return;
        }

        if (!TryFindPad(args.Grid, args.Ground, pilot, out var placement, out var reason))
        {
            if (reason == null)
                return;

            args.Handled = true;
            args.Reason = reason;
            return;
        }

        args.Handled = true;
        args.Reason = TryDescend(args.Grid, placement.Value, out reason) ? null : reason;
    }

    private void OnLiftoffDivert(ref WFLiftoffDivertEvent args)
    {
        if (args.Handled || !TryComp<WFPadDockedComponent>(args.Grid, out var docked))
            return;

        args.Handled = true;
        args.Reason = TryLaunch((args.Grid, docked), out var reason) ? null : reason;
    }

    private void OnHopCompleted(ref WFPlanetHopCompletedEvent args)
    {
        if (!TryComp<WFPadGuidedComponent>(args.Hull, out var guided))
            return;

        var padUid = guided.Pad;
        RemComp<WFPadGuidedComponent>(args.Hull);
        if (TryComp<WFLandingPadComponent>(padUid, out var pad) && Transform(padUid).MapUid == args.MapUid)
        {
            if (!HasComp<WFPadDockedComponent>(args.Hull))
                Dock(args.Hull, (padUid, pad));
            return;
        }

        RemComp<WFPadDockedComponent>(args.Hull);
    }

    private void Guide(EntityUid hull, Entity<WFLandingPadComponent> pad)
    {
        EnsureComp<WFPadGuidedComponent>(hull).Pad = pad;
        SetGuiding(pad, true);
    }

    /// <summary>Rests a hull that already stands on the pad there, as if the pad had guided it down.</summary>
    public void Park(EntityUid hull, Entity<WFLandingPadComponent> pad)
    {
        Dock(hull, pad);
    }

    private void Dock(EntityUid hull, Entity<WFLandingPadComponent> pad)
    {
        EnsureComp<WFPadDockedComponent>(hull).Pad = pad;
        pad.Comp.Parked = hull;
        // Static, it rests over the outpost's static floor without the two pushing each other apart.
        _shuttle.Disable(hull);
        var docked = new WFPadDockedEvent(hull);
        RaiseLocalEvent(pad, ref docked);
    }

    private void SetGuiding(Entity<WFLandingPadComponent> pad, bool guiding)
    {
        pad.Comp.Guiding = guiding;
        Dirty(pad);
        if (TryComp<ApcPowerReceiverComponent>(pad, out var receiver))
            receiver.Load = guiding ? pad.Comp.GuidingLoad : pad.Comp.IdleLoad;
    }

    private void OnGuidedShutdown(Entity<WFPadGuidedComponent> hull, ref ComponentShutdown args)
    {
        if (TryComp<WFLandingPadComponent>(hull.Comp.Pad, out var pad) && !TerminatingOrDeleted(hull.Comp.Pad))
            SetGuiding((hull.Comp.Pad, pad), false);
    }

    private void OnDockedShutdown(Entity<WFPadDockedComponent> hull, ref ComponentShutdown args)
    {
        if (TryComp<WFLandingPadComponent>(hull.Comp.Pad, out var pad) && pad.Parked == hull.Owner)
            pad.Parked = null;

        if (!TerminatingOrDeleted(hull) && !_hop.IsHopping(hull))
            _shuttle.Enable(hull);
    }

    private void OnPadShutdown(Entity<WFLandingPadComponent> pad, ref ComponentShutdown args)
    {
        if (pad.Comp.Parked is { } parked && TryComp<WFPadDockedComponent>(parked, out var docked) && docked.Pad == pad.Owner)
            RemComp<WFPadDockedComponent>(parked);
    }

    private void OnPadExamined(Entity<WFLandingPadComponent> pad, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(WFLandingPadComponent)))
        {
            args.PushMarkup(Loc.GetString("wf-pad-examine", ("size", pad.Comp.Size)));
            args.PushMarkup(Loc.GetString(pad.Comp.Public ? "wf-pad-examine-public" : "wf-pad-examine-private"));
            if (pad.Comp.Guiding)
                args.PushMarkup(Loc.GetString("wf-pad-examine-guiding"));
            else if (pad.Comp.Parked != null)
                args.PushMarkup(Loc.GetString("wf-pad-examine-parked"));
            if (!_power.IsPowered(pad))
                args.PushMarkup(Loc.GetString("wf-pad-examine-unpowered"));
        }
    }

    private void OnPadVerbs(Entity<WFLandingPadComponent> pad, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || Transform(pad).GridUid is not { } grid
            || !TryComp<WFOutpostComponent>(grid, out var outpost) || !_outposts.CanBuild(args.User, (grid, outpost)))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(pad.Comp.Public ? "wf-pad-verb-private" : "wf-pad-verb-public"),
            Priority = -2,
            Act = () =>
            {
                pad.Comp.Public = !pad.Comp.Public;
                Dirty(pad);
                _popup.PopupEntity(Loc.GetString(pad.Comp.Public ? "wf-pad-now-public" : "wf-pad-now-private"), pad, user);
            },
        });

        foreach (var step in new[] { 2, -2 })
        {
            var size = pad.Comp.Size + step;
            if (size < WFLandingPadComponent.MinSize || size > pad.Comp.Widest)
                continue;

            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString(step > 0 ? "wf-pad-verb-widen" : "wf-pad-verb-narrow", ("size", size)),
                Priority = -1,
                Act = () => Resize(pad, size, user),
            });
        }
    }

    private void Resize(Entity<WFLandingPadComponent> pad, int size, EntityUid user)
    {
        if (pad.Comp.Guiding || pad.Comp.Parked != null)
        {
            _popup.PopupEntity(Loc.GetString("wf-pad-in-use"), pad, user);
            return;
        }

        pad.Comp.Size = size;
        Dirty(pad);
        _popup.PopupEntity(Loc.GetString("wf-pad-resized", ("size", size)), pad, user);
    }

    /// <summary>Whether something anchored stands in a hull's way: anything with a hard fixture.</summary>
    private bool Blocks(EntityUid ent)
    {
        return TryComp<PhysicsComponent>(ent, out var body) && body.CanCollide && body.Hard;
    }

    /// <summary>The orbit layer of the world whose layer this map is.</summary>
    private EntityUid? OrbitAbove(EntityUid? map)
    {
        return TryComp<WFPlanetLayerComponent>(map, out var layer)
               && layer.Network is { } net
               && TryGetEntity(net, out var network)
               && TryComp<WFPlanetNetworkComponent>(network, out var comp)
            ? comp.OrbitMap
            : null;
    }
}
