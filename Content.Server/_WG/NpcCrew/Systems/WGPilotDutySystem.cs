using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.HTN;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Mobs.Systems;
using Content.Shared.Interaction;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// Puts a crew pilot at the helm and flies its orders with Mono's ship steering. The HTN only walks the pilot to the
/// helm and holds it; orders advance here, so a ship keeps flying while its pilot's HTN sleeps. Docking by hand is in
/// <c>WGPilotDutySystem.Docking.cs</c>.
/// </summary>
public sealed partial class WGPilotDutySystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipSteeringSystem _steering = default!;
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How often the helm's reach is checked again with a raycast.</summary>
    private static readonly TimeSpan ReachCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>How often a pilot's docked state is checked again without a dock event.</summary>
    private static readonly TimeSpan DockCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>How often a leader with no console on it is searched again.</summary>
    private static readonly TimeSpan LeaderConsoleRetry = TimeSpan.FromSeconds(5);

    /// <summary>Blackboard key holding the helm the pilot works.</summary>
    public const string HelmKey = "WGCrewHelm";

    /// <summary>Blackboard key holding the helm's coordinates, walked to before taking it.</summary>
    public const string HelmCoordinatesKey = "WGCrewHelmCoords";

    private readonly List<EntityUid> _toRelease = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGPilotDutyComponent, PilotedShuttleRelayedEvent<StartCollideEvent>>(OnShuttleCollide);
        SubscribeLocalEvent<WGPilotDutyComponent, GetShuttleInputsEvent>(OnCrewGetInputs,
            after: new[] { typeof(ShipSteeringSystem), typeof(MoverController) });
        SubscribeLocalEvent<DockEvent>(OnDockChanged);
        SubscribeLocalEvent<UndockEvent>(OnUndockChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        PruneStances(now);
        var refreshDocks = _docksChanged;
        _docksChanged = false;
        _toRelease.Clear();
        var query = EntityQueryEnumerator<WGPilotDutyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var duty, out var xform))
        {
            // Mid-jump the grid passes through hyperspace; only the map it arrives on counts.
            if (xform.MapID != duty.LastMap && !InTransit(xform.GridUid))
            {
                var previous = duty.LastMap;
                duty.LastMap = xform.MapID;
                if (previous != MapId.Nullspace)
                    OnMapChanged((uid, duty), xform.MapID);
            }

            if (!duty.AtHelm)
                continue;

            if (!HoldsHelm((uid, duty), out var console) || !HelmInReach((uid, duty), console, now) || !HelmInPlan(uid))
            {
                _toRelease.Add(uid);
                continue;
            }

            if (refreshDocks || now >= duty.NextDockCheck)
                RefreshDocked((uid, duty));

            if (duty.YieldedUntil is { } yielded)
            {
                // While a person flies the ship its orders stand still: no dock timer runs and no waypoint is passed.
                if (now < yielded)
                    continue;

                // Someone flew the ship while this crewman stood by: its orders start again from where the ship is now,
                // and a bump while they flew is not the creep's.
                duty.YieldedUntil = null;
                duty.HoldPosition = null;
                duty.DockCollision = null;
                if (Steer((uid, duty)) == null)
                    _toRelease.Add(uid);

                continue;
            }

            if (!TryComp<ShipSteererComponent>(uid, out var steerer)
                || TargetElsewhere(duty, xform.MapID) != duty.AwaitingTarget)
            {
                // Something else stopped the steering, or the target left or reached our map; steer afresh.
                if (Steer((uid, duty)) == null)
                    _toRelease.Add(uid);

                continue;
            }

            Advance((uid, duty), steerer, frameTime);
        }

        foreach (var uid in _toRelease)
        {
            ReleaseHelm(uid);
        }
    }

    /// <summary>Stop the ship and keep station.</summary>
    public void Hold(Entity<WGPilotDutyComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        SetOrders((ent, ent.Comp), WGPilotOrder.Hold);
    }

    /// <summary>Fly the waypoints in turn, then hold. No waypoints is Hold.</summary>
    public void GoTo(Entity<WGPilotDutyComponent?> ent, List<EntityCoordinates> waypoints)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        if (waypoints.Count == 0)
        {
            Hold(ent);
            return;
        }

        ent.Comp.Waypoints = new List<EntityCoordinates>(waypoints);
        ent.Comp.WaypointIndex = 0;
        SetOrders((ent, ent.Comp), WGPilotOrder.GoTo);
    }

    /// <summary>Circle a point at a radius.</summary>
    public void Loiter(Entity<WGPilotDutyComponent?> ent, EntityCoordinates center, float radius, float? speed = null,
        WGCrewObjectiveKind objective = WGCrewObjectiveKind.Loiter)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.LoiterCenter = center;
        ent.Comp.RequestedLoiterRadius = radius;
        ent.Comp.LoiterSpeedOverride = speed;
        ent.Comp.OrbitKind = objective;
        UpdateOrbitLimits(ent.Comp);
        SetOrders((ent, ent.Comp), WGPilotOrder.Loiter);
    }

    /// <summary>Paces a circling pilot without restarting his orbit: a speed, or null for his profile's own.</summary>
    public void SetCircleSpeed(Entity<WGPilotDutyComponent?> ent, float? speed)
    {
        if (!Resolve(ent, ref ent.Comp) || ent.Comp.Orders != WGPilotOrder.Loiter || ent.Comp.OrbitKind != WGCrewObjectiveKind.Circle
            || ent.Comp.LoiterSpeedOverride == speed)
            return;

        ent.Comp.LoiterSpeedOverride = speed;
        UpdateOrbitLimits(ent.Comp);
        if (ent.Comp.AtHelm)
            Steer((ent, ent.Comp));
    }

    /// <summary>Keep within range of another grid.</summary>
    public void Follow(Entity<WGPilotDutyComponent?> ent, EntityUid grid, float range)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.FollowTarget = grid;
        ent.Comp.FollowRange = range;
        ent.Comp.EscortOffset = null;
        SetOrders((ent, ent.Comp), WGPilotOrder.Follow);
    }

    /// <summary>Assigns an unoccupied trailing formation slot relative to the escorted ship.</summary>
    public void Escort(EntityUid pilot, EntityUid target, float spacing)
    {
        if (!TryComp<WGPilotDutyComponent>(pilot, out var duty) || Transform(pilot).GridUid is not { } grid)
            return;
        var occupied = new HashSet<int>();
        var captains = EntityManager.System<WGCaptainSystem>();
        var query = EntityQueryEnumerator<WGPilotDutyComponent>();
        while (query.MoveNext(out var other, out var escort))
        {
            if (other == pilot || TerminatingOrDeleted(other) || !_mobState.IsAlive(other) || HasComp<ActorComponent>(other))
                continue;
            if (escort.FollowTarget == target && escort.EscortOffset != null
                && (escort.Orders == WGPilotOrder.Follow || escort.ResumeOrder == WGPilotOrder.Follow))
                occupied.Add(escort.EscortSlot);
            // An escort evading under its captain returns to its slot afterwards.
            else if (captains.TryGetSavedEscort(other, out var saved, out var savedSlot) && saved == target)
                occupied.Add(savedSlot);
        }
        var slot = 0;
        while (occupied.Contains(slot))
            slot++;
        duty.FollowTarget = target;
        duty.FollowRange = duty.Navigation.EscortRange;
        duty.EscortSlot = slot;
        duty.EscortSpacing = spacing;
        UpdateEscortOffset(duty, grid, target);
        SetOrders((pilot, duty), WGPilotOrder.Follow);
        EntityManager.System<WGCrewEscortSystem>().SetEscort(pilot, target);
    }

    private float GridRadius(EntityUid grid) => TryComp<Robust.Shared.Map.Components.MapGridComponent>(grid, out var map)
        ? map.LocalAABB.Size.Length() / 2f + map.LocalAABB.Center.Length() : 0f;

    /// <summary>How far from a grid's centre the pilot's ship can get without entering the clearance around it.</summary>
    public float HullClearance(EntityUid pilot, EntityUid target)
    {
        if (!TryComp<WGPilotDutyComponent>(pilot, out var duty) || Transform(pilot).GridUid is not { } grid)
            return 0f;
        return GridRadius(grid) + GridRadius(target) + duty.Navigation.NavigationClearance;
    }

    /// <summary>The escorted grid's forward, from its helm relative to the grid; the helm is cached on the escort.</summary>
    private Angle GridForwardAngle(WGPilotDutyComponent duty, EntityUid grid)
    {
        var cached = duty.LeaderConsoleOf == grid
                     && (duty.LeaderConsole is { } held
                         ? !TerminatingOrDeleted(held) && Transform(held) is { Anchored: true } heldXform && heldXform.GridUid == grid
                         : _timing.CurTime < duty.NextLeaderConsoleCheck);
        if (!cached)
        {
            duty.LeaderConsoleOf = grid;
            duty.LeaderConsole = FindLeaderConsole(grid);
            duty.NextLeaderConsoleCheck = _timing.CurTime + LeaderConsoleRetry;
        }
        return duty.LeaderConsole is { } console
            ? _transform.GetWorldRotation(console) - _transform.GetWorldRotation(grid)
            : Angle.Zero;
    }

    /// <summary>The anchored console a grid is flown from, else a powered one, else any.</summary>
    private EntityUid? FindLeaderConsole(EntityUid grid)
    {
        EntityUid? powered = null;
        EntityUid? anchored = null;
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out var console, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;
            if (console.SubscribedPilots.Count > 0)
                return uid;
            anchored ??= uid;
            if (powered == null && _power.IsPowered(uid))
                powered = uid;
        }
        return powered ?? anchored;
    }

    /// <summary>Fly to another grid, dock with it by hand, then hold.</summary>
    public void Dock(Entity<WGPilotDutyComponent?> ent, EntityUid targetGrid)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.DockTarget = targetGrid;
        SetOrders((ent, ent.Comp), WGPilotOrder.Dock);
    }

    /// <summary>Undock from everything, back off away from it, then hold. Done once the pilot is at the helm.</summary>
    public void Undock(Entity<WGPilotDutyComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        // The back-off point is filled in when the docks are released.
        ent.Comp.Waypoints = new List<EntityCoordinates>();
        ent.Comp.WaypointIndex = 0;
        SetOrders((ent, ent.Comp), WGPilotOrder.Undock);
    }

    private void SetOrders(Entity<WGPilotDutyComponent> ent, WGPilotOrder orders, bool continuation = false)
    {
        ent.Comp.ResumeOrder = null;
        ent.Comp.HeadingOverride = null;
        if (orders is not (WGPilotOrder.Hold or WGPilotOrder.Undock)
            && Transform(ent).GridUid is { } grid && RefreshDocked(ent)
            && !(orders == WGPilotOrder.Dock && ent.Comp.DockTarget is { } destination && _docking.AreGridsDocked(grid, destination)))
        {
            var waypoints = ent.Comp.Waypoints;
            ent.Comp.Waypoints = new List<EntityCoordinates>();
            ent.Comp.WaypointIndex = 0;
            SetOrders(ent, WGPilotOrder.Undock, continuation);
            ent.Comp.ResumeOrder = orders;
            ent.Comp.ResumeWaypoints = waypoints;
            return;
        }
        ent.Comp.Orders = orders;
        ent.Comp.OrdersCompleted = false;
        if (orders == WGPilotOrder.Hold)
            CaptureHold(ent);
        ResetDock(ent.Comp);

        var ev = new WGPilotOrdersChangedEvent(ent, orders, continuation);
        RaiseLocalEvent(ent, ref ev, true);

        if (ent.Comp.AtHelm)
            Steer(ent);
    }

    /// <summary>
    /// The assigned helm if it can be flown from, else the nearest anchored, powered shuttle console on the
    /// crewman's grid, one turned square to the hull before any that is not: a ship steered from a console turned
    /// away from its thrusters would fly stern first. A helm the crewman already holds wins. With
    /// <paramref name="squareFirst"/> off the nearest console wins however it is turned, for a pilot who cannot reach
    /// a square one.
    /// </summary>
    public bool TryFindHelm(EntityUid mob, out EntityUid console, bool squareFirst = true)
    {
        console = default;
        if (Transform(mob).GridUid is not { } grid)
            return false;

        if (TryComp<PilotComponent>(mob, out var pilot) && pilot.Console is { } held && IsUsableHelm(held, grid))
        {
            console = held;
            return true;
        }

        if (squareFirst
            && TryComp<WGPilotDutyComponent>(mob, out var duty)
            && duty.Console is { } assigned
            && IsUsableHelm(assigned, grid))
        {
            console = assigned;
            return true;
        }

        var position = _transform.GetWorldPosition(mob);
        var best = float.MaxValue;
        var bestSquare = false;
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid || !IsUsableHelm(uid, grid))
                continue;

            var square = squareFirst && IsSquareHelm(xform.LocalRotation);
            var distance = (_transform.GetWorldPosition(xform) - position).LengthSquared();
            if (best < float.MaxValue && (bestSquare && !square || bestSquare == square && distance >= best))
                continue;

            best = distance;
            bestSquare = square;
            console = uid;
        }

        return best < float.MaxValue;
    }

    /// <summary>
    /// Attaches the crewman to the helm and steers toward its current orders. True when it holds the helm
    /// afterwards, including when it already did.
    /// </summary>
    public bool TryTakeHelm(EntityUid mob, EntityUid console)
    {
        if (!TryComp<WGPilotDutyComponent>(mob, out var duty)
            || Transform(mob).GridUid is not { } grid
            || !HasComp<ShuttleComponent>(grid)
            || !IsUsableHelm(console, grid)
            || !_interaction.InRangeUnobstructed(mob, console)
            || !TryComp<ShuttleConsoleComponent>(console, out var consoleComp))
        {
            return false;
        }

        // Upstream's AddPilot doesn't let go of another console first.
        if (TryComp<PilotComponent>(mob, out var existing) && existing.Console is { } current && current != console)
            _console.RemovePilot(mob, existing);

        var pilot = EnsureComp<PilotComponent>(mob);
        _console.AddPilot(console, mob, consoleComp);
        if (pilot.Console != console)
        {
            // A pilot component with no console still blocks movement.
            RemComp<PilotComponent>(mob);
            return false;
        }

        // Before steering, so a ship that is docked waits where it is.
        RefreshDocked((mob, duty));
        if (Steer((mob, duty)) == null)
        {
            _console.RemovePilot(mob);
            return false;
        }

        // Just checked in range; the update loop checks again after the interval.
        duty.HelmInReach = true;
        duty.NextReachCheck = _timing.CurTime + ReachCheckInterval;
        if (duty.AtHelm)
            return true;

        duty.Console = console;
        duty.AtHelm = true;
        EntityManager.System<WGCrewSpeechSystem>().Say(mob, "helm");
        var facing = _transform.GetWorldPosition(console) - _transform.GetWorldPosition(mob);
        if (facing.LengthSquared() > 0.001f)
            _transform.SetWorldRotation(mob, facing.ToWorldAngle());
        var ev = new WGHelmTakenEvent(mob, console, grid);
        RaiseLocalEvent(mob, ref ev, true);
        return true;
    }

    /// <summary>Stops steering and detaches the crewman from the helm. Safe to call at any time.</summary>
    public void ReleaseHelm(EntityUid mob)
    {
        var ship = (TryComp<PilotComponent>(mob, out var helm) && helm.Console is { } held
                       ? CompOrNull<TransformComponent>(held)?.GridUid
                       : null)
                   ?? CompOrNull<TransformComponent>(mob)?.GridUid;
        _steering.Stop(mob);
        _console.RemovePilot(mob);

        if (!TryComp<WGPilotDutyComponent>(mob, out var duty) || !duty.AtHelm)
            return;

        duty.AtHelm = false;
        // The steering leaves dampening off, and an unflown hull would coast on for minutes.
        if (ship is { } grid && TryComp<ShuttleComponent>(grid, out var shuttle) && TryComp<PhysicsComponent>(grid, out var body))
        {
            _shuttle.SetInertiaDampening(grid, body, shuttle, Transform(grid),
                duty.Docked ? InertiaDampeningMode.Anchor : InertiaDampeningMode.Dampen);
        }
        var ev = new WGHelmReleasedEvent(mob);
        RaiseLocalEvent(mob, ref ev, true);
    }

    public bool IsAtHelm(EntityUid mob)
    {
        return TryComp<WGPilotDutyComponent>(mob, out var duty) && duty.AtHelm;
    }

    /// <summary>
    /// Whether the crewman holds a helm it can keep flying from: attached to a usable console, up, and not taken over
    /// by a player.
    /// </summary>
    public bool CanHoldHelm(Entity<WGPilotDutyComponent?> ent)
    {
        return Resolve(ent, ref ent.Comp, false)
               && HoldsHelm((ent, ent.Comp), out var console)
               && _interaction.InRangeUnobstructed(ent.Owner, console);
    }

    /// <summary><see cref="CanHoldHelm"/> without the reach raycast.</summary>
    private bool HoldsHelm(Entity<WGPilotDutyComponent> ent, out EntityUid console)
    {
        console = default;
        if (!ent.Comp.AtHelm
            || !TryComp<PilotComponent>(ent, out var pilot)
            || pilot.Console is not { } held
            || Transform(ent).GridUid is not { } grid
            || !IsUsableHelm(held, grid)
            || _mobState.IsIncapacitated(ent)
            || HasComp<ActorComponent>(ent))
            return false;

        console = held;
        return true;
    }

    /// <summary>Whether the helm is in reach and unobstructed, raycast at most once per interval.</summary>
    private bool HelmInReach(Entity<WGPilotDutyComponent> ent, EntityUid console, TimeSpan now)
    {
        if (now >= ent.Comp.NextReachCheck)
        {
            ent.Comp.NextReachCheck = now + ReachCheckInterval;
            ent.Comp.HelmInReach = _interaction.InRangeUnobstructed(ent.Owner, console);
        }
        return ent.Comp.HelmInReach;
    }

    /// <summary>
    /// False when the crewman's HTN is running a plan that doesn't hold the helm, such as a fight it woke into. No
    /// plan (asleep, or between plans) keeps the helm.
    /// </summary>
    private bool HelmInPlan(EntityUid mob)
    {
        if (!TryComp<HTNComponent>(mob, out var htn) || htn.Plan is not { } plan)
            return true;
        foreach (var task in plan.Tasks)
        {
            if (task.Operator is WGTakeHelmOperator)
                return true;
        }
        return false;
    }

    private bool IsUsableHelm(EntityUid console, EntityUid grid)
    {
        return !TerminatingOrDeleted(console)
               && HasComp<ShuttleConsoleComponent>(console)
               && Transform(console) is { Anchored: true } xform
               && xform.GridUid == grid
               && _power.IsPowered(console);
    }

    /// <summary>
    /// GoTo: on to the next waypoint once in range, and hold after the last. Follow: hold once the grid is gone.
    /// Undock: release the docks, then back off like a GoTo. Dock: fly the docking phases.
    /// </summary>
    private void Advance(Entity<WGPilotDutyComponent> ent, ShipSteererComponent steerer, float frameTime)
    {
        var duty = ent.Comp;
        duty.HeadingOverride = null;
        // A visitor docked to us mid-flight: brake in place and carry on once it casts off.
        if (duty.Docked && duty.Orders is WGPilotOrder.GoTo or WGPilotOrder.Follow or WGPilotOrder.Loiter)
            return;
        if (duty.Orders == WGPilotOrder.Follow && duty.EscortOffset != null && duty.FollowTarget is { } leader
            && !TerminatingOrDeleted(leader) && !duty.AwaitingTarget && Transform(ent).GridUid is { } grid)
        {
            steerer.Coordinates = new EntityCoordinates(leader, duty.EscortOffset.Value);
            var heading = _transform.GetWorldRotation(leader) + GridForwardAngle(duty, leader) + new Angle(Math.PI) + Angle.FromDegrees(TravelHeadingOffset(ent));
            steerer.InRangeRotation = heading;
            steerer.AlwaysFaceTarget = true;
            var distance = (_transform.ToMapCoordinates(steerer.Coordinates).Position - _transform.GetWorldPosition(grid)).Length();
            // Near the slot its bearing swings every tick; hold the leader's heading outright.
            var cruising = distance > MathF.Max(duty.Navigation.EscortHeadingRange, GridRadius(grid) * 2f);
            duty.HeadingOverride = cruising ? null : heading;
            steerer.TargetRotation = cruising ? TravelHeadingOffset(ent) : 0f;
        }
        PresentGuns(ent, steerer);
        FaceThreatsHolding(ent, steerer);
        switch (duty.Orders)
        {
            case WGPilotOrder.Dock:
                // A target on another map is waited for in place.
                if (!duty.AwaitingTarget)
                    AdvanceDock(ent, steerer, frameTime);
                break;
            case WGPilotOrder.Undock when duty.Waypoints.Count == 0:
                ReleaseDocks(ent, frameTime);
                break;
            case WGPilotOrder.GoTo or WGPilotOrder.Undock when steerer.Status == ShipSteeringStatus.InRange:
                duty.WaypointIndex++;
                if (duty.WaypointIndex < duty.Waypoints.Count)
                {
                    Steer(ent);
                    return;
                }

                CompleteOrders(ent);
                break;
            case WGPilotOrder.Follow when duty.FollowTarget is not { } target || TerminatingOrDeleted(target):
                SetOrders(ent, WGPilotOrder.Hold);
                break;
        }
    }

    /// <summary>Switches to Hold and reports the orders flown to their end.</summary>
    private void CompleteOrders(Entity<WGPilotDutyComponent> ent)
    {
        if (ent.Comp.ResumeOrder is { } resume)
        {
            ent.Comp.Waypoints = ent.Comp.ResumeWaypoints;
            ent.Comp.WaypointIndex = 0;
            SetOrders(ent, resume, continuation: true);
            return;
        }
        SetOrders(ent, WGPilotOrder.Hold);
        ent.Comp.OrdersCompleted = true;
        var ev = new WGPilotOrdersCompletedEvent(ent);
        RaiseLocalEvent(ent, ref ev, true);
    }

    /// <summary>Points the steering at the current orders. Null when the crewman's grid can't be steered.</summary>
    private ShipSteererComponent? Steer(Entity<WGPilotDutyComponent> ent)
    {
        var duty = ent.Comp;
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || xform.MapUid is not { } map)
            return null;

        // Hold where the ship is now; also the fallback for orders missing their target.
        var target = new EntityCoordinates(map, _transform.GetWorldPosition(grid));
        var mode = ShipSteeringMode.GoToRange;
        var range = duty.Navigation.HoldRange;
        var speed = duty.Navigation.ArrivalSpeed;
        var avoid = true;
        var finishOnCollide = false;
        var faceTarget = false;
        Angle? heading = null;
        float? maxTurnRate = null;
        // A target on another map is waited for where the ship is.
        var elsewhere = duty.AwaitingTarget = TargetElsewhere(duty, xform.MapID);

        switch (duty.Orders)
        {
            case WGPilotOrder.Hold:
                if (duty.HoldPosition is not { } heldPosition || !heldPosition.IsValid(EntityManager)
                    || _transform.ToMapCoordinates(heldPosition).MapId != xform.MapID)
                    CaptureHold(ent);
                target = duty.HoldPosition ?? target;
                heading = duty.HoldHeading;
                avoid = false;
                faceTarget = true;
                break;
            case WGPilotOrder.GoTo when duty.WaypointIndex >= 0 && duty.WaypointIndex < duty.Waypoints.Count:
                target = duty.Waypoints[duty.WaypointIndex];
                range = duty.ArrivalRange;
                speed = duty.Navigation.ArrivalSpeed;
                faceTarget = true;
                break;
            case WGPilotOrder.Loiter when duty.LoiterCenter is { } center && !elsewhere:
                target = center;
                mode = ShipSteeringMode.Orbit;
                range = SafeOrbitRange(grid, center, duty.LoiterRadius, duty.Navigation.NavigationClearance);
                speed = SpeedLimit(duty.LoiterSpeed);
                break;
            case WGPilotOrder.Follow when duty.FollowTarget is { } followed && !TerminatingOrDeleted(followed) && !elsewhere:
                // Grid-relative coordinates move with the grid.
                target = new EntityCoordinates(followed, duty.EscortOffset ?? Vector2.Zero);
                range = duty.EscortOffset != null ? duty.FollowRange : MathF.Max(duty.FollowRange, GridRadius(grid) + GridRadius(followed) + duty.Navigation.NavigationClearance);
                speed = duty.Navigation.ArrivalSpeed;
                faceTarget = true;
                break;
            case WGPilotOrder.Undock when duty.Waypoints.Count > 0:
                target = duty.Waypoints[0];
                speed = duty.Navigation.UndockSpeed;
                break;
            case WGPilotOrder.Dock when duty.DockPlan is { } plan
                                        && duty.DockTarget is { } dockTarget
                                        && !TerminatingOrDeleted(dockTarget)
                                        && !elsewhere:
                heading = DockHeading(plan, dockTarget);
                if (duty.DockPhase == WGDockPhase.Creep)
                {
                    // On the target grid, so the final pose moves with it; avoidance would refuse to touch it.
                    target = plan.Final;
                    range = duty.Navigation.DockCreepRange;
                    speed = duty.DockCreepSpeed;
                    avoid = false;
                    finishOnCollide = false;
                    faceTarget = true;
                    break;
                }

                target = StandoffOnMap(plan, map);
                range = duty.Navigation.DockStandoffRange;
                speed = duty.DockPhase == WGDockPhase.Settle ? duty.Navigation.DockSettleSpeed : duty.DockApproachSpeed;
                maxTurnRate = duty.DockPhase == WGDockPhase.Settle ? duty.Navigation.DockSettleTurnRate : null;
                break;
        }

        // Docked and waiting: aimed at our own grid the steering stays idle, and OnCrewGetInputs brakes the pair.
        if (DockedWait(duty))
        {
            target = new EntityCoordinates(grid, Vector2.Zero);
            mode = ShipSteeringMode.GoToRange;
            range = MathF.Max(duty.Navigation.HoldRange, 1f);
            speed = duty.Navigation.ArrivalSpeed;
            avoid = false;
            faceTarget = false;
            heading = null;
            maxTurnRate = null;
        }

        if (_steering.Steer(ent.Owner, target) is not { } steerer)
            return null;

        steerer.Mode = mode;
        steerer.OrbitOffset = Angle.FromDegrees(duty.Navigation.OrbitLookaheadAngle);
        steerer.Range = range;
        // Orbit uses the midpoint of the range band; a null tolerance otherwise halves its radius.
        steerer.RangeTolerance = mode == ShipSteeringMode.Orbit ? 0f : null;
        steerer.InRangeMaxSpeed = speed;
        steerer.AvoidCollisions = avoid;
        var skill = TryComp<WGCrewComponent>(ent, out var pilotCrew) ? WGCrewSkills.Of(pilotCrew) : WGCrewSkills.Of(WGCrewSkill.Veteran);
        steerer.AvoidProjectiles = skill.DodgesFire && duty.Orders is not (WGPilotOrder.Dock or WGPilotOrder.Hold);
        steerer.EvasionBuffer = duty.Orders == WGPilotOrder.Dock ? duty.Navigation.DockEvasionBuffer : duty.Navigation.EvasionBuffer;
        steerer.BaseEvasionTime = duty.Orders == WGPilotOrder.Dock ? duty.Navigation.DockEvasionLookahead : duty.Navigation.EvasionLookahead * skill.Evasion;
        steerer.RotationCompensation = 0f;
        steerer.RotationCompensationGain = 0f;
        steerer.FinishOnCollide = finishOnCollide;
        steerer.InRangeRotation = heading;
        steerer.MaxRotateRate = maxTurnRate;
        steerer.AlwaysFaceTarget = faceTarget;
        steerer.TargetRotation = faceTarget && heading is { } held ? CreepHeadingOffset(steerer, held, grid) : TravelHeadingOffset(ent);
        // Status is only refreshed when the ship next asks for input; don't let the last target's arrival count.
        steerer.Status = ShipSteeringStatus.Moving;
        return steerer;
    }

    /// <summary>How far, in radians, a console may be turned from the hull and still count as square to it.</summary>
    private const double SquareHelm = 0.1;

    /// <summary>Whether a console turned by this much from the hull counts as square to it.</summary>
    public static bool IsSquareHelm(Angle rotation)
    {
        return Math.Abs(Angle.ShortestDistance(rotation, Angle.Zero).Theta) < SquareHelm;
    }

    /// <summary>Uses the helm's north direction for cruise flight.</summary>
    private float TravelHeadingOffset(EntityUid pilot)
    {
        if (TryComp<PilotComponent>(pilot, out var helm) && helm.Console is { } console
            && Transform(pilot).GridUid is { } grid)
            return (float) (_transform.GetWorldRotation(grid) - _transform.GetWorldRotation(console)).Degrees;
        return 0f;
    }
}
