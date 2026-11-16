using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Shuttles;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.RadarMenu;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Server._WF.RadarMenu;

/// <summary>
/// The helm side of the radar menu's "Dock at": lists the free docks on a grid the ship can dock with, then flies the
/// ship in and docks it the way the NPC boarding pilots do, with their planner and settings: out to a standoff in front
/// of the port with a clear lane, settle there turned to dock, creep in port to port and dock once the ports line up,
/// as the Dock key does. A bump or a stall goes back out and tries again; after three tries it stops and says why.
/// </summary>
public sealed partial class WFAutoDockSystem : EntitySystem
{
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private ShipSteeringSystem _steering = default!;
    [Dependency] private WFPilotDutySystem _pilots = default!;

    /// <summary>How far, in metres, a grid may be for its docks to be listed or flown to.</summary>
    public const float MaxRange = 6000f;

    /// <summary>The most docks listed for one grid.</summary>
    public const int MaxOptions = 12;

    /// <summary>Tries before giving up, as an NPC pilot.</summary>
    public const int MaxAttempts = 3;

    /// <summary>The NPC pilots' docking settings, flown as a veteran would.</summary>
    private static readonly WFCrewNavigationSettings Navigation = new();

    private static readonly TimeSpan CheckGap = TimeSpan.FromSeconds(0.1);

    /// <summary>The longest a docking waits for a flying autopilot to let go of the controls.</summary>
    private static readonly TimeSpan ReleaseWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OptionsGap = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<EntityUid, TimeSpan> _nextOptions = new();

    public override void Initialize()
    {
        base.Initialize();

        // ShuttleConsoleSystem owns the open and close subscriptions for these consoles; only new message types here.
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<WFAutoDockOptionsRequestMessage>(OnOptionsRequest);
            subs.Event<WFAutoDockRequestMessage>(OnDockRequest);
            subs.Event<WFAutopilotCancelMessage>(OnCancel);
            subs.Event<WFAutoOrbitRequestMessage>(OnOrbitRequest);
        });

        // A new autopilot target from the map or a waypoint replaces the docking.
        SubscribeLocalEvent<WFAutoDockComponent, ShuttleConsoleAutopilotPositionMessage>(OnOtherAutopilot);
        SubscribeLocalEvent<WFAutoDockComponent, PilotedShuttleRelayedEvent<StartCollideEvent>>(OnCollide);
        SubscribeLocalEvent<WFAutoDockComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Content.Shared.GameTicking.RoundRestartCleanupEvent>(_ => _nextOptions.Clear());
        InitializeOrbit();
    }

    private void OnOptionsRequest(Entity<ShuttleConsoleComponent> ent, ref WFAutoDockOptionsRequestMessage args)
    {
        var now = _timing.CurTime;
        if (_nextOptions.TryGetValue(ent, out var next) && now < next)
            return;
        _nextOptions[ent] = now + OptionsGap;

        if (!TryGetEntity(args.Grid, out var grid) || !_players.TryGetSessionByEntity(args.Actor, out var session))
            return;

        var options = Options(ent, grid.Value);
        RaiseNetworkEvent(new WFAutoDockOptionsEvent(GetNetEntity(ent), args.Grid, options), session);
    }

    /// <summary>The free docks on a grid that the helm's ship can dock with and reach by a clear lane, nearest first.</summary>
    public List<WFDockOption> Options(EntityUid console, EntityUid grid)
    {
        var options = new List<WFDockOption>();
        if (!TryGetShip(console, grid, out var ship))
            return options;

        var shipPosition = _xform.GetWorldPosition(ship);
        var unnamed = 0;
        foreach (var dock in _docking.GetDocks(grid))
        {
            if (dock.Comp.Docked)
                continue;
            unnamed++;
            if (!_pilots.TryPlanDock(ship, grid, Navigation.DockStandoff, out _, dock.Owner))
                continue;

            var name = dock.Comp.Name is { } label
                ? Loc.GetString(label)
                : Loc.GetString("wf-radar-menu-dock-unnamed", ("number", unnamed));
            var distance = Vector2.Distance(shipPosition, _xform.GetWorldPosition(dock));
            options.Add(new WFDockOption(GetNetEntity(dock), name, distance));
        }

        options.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (options.Count > MaxOptions)
            options.RemoveRange(MaxOptions, options.Count - MaxOptions);
        return options;
    }

    private void OnDockRequest(Entity<ShuttleConsoleComponent> ent, ref WFAutoDockRequestMessage args)
    {
        if (!TryGetEntity(args.Grid, out var grid))
            return;

        EntityUid? dock = args.Dock is { } netDock && TryGetEntity(netDock, out var found) ? found : null;
        if (args.Dock != null && dock == null)
            return;

        TryStart(ent, grid.Value, dock, args.Actor);
    }

    /// <summary>
    /// Sends the ship in to dock at a grid: at one dock, or at the nearest free one that fits. False, with the pilot
    /// told why, when there is no such dock.
    /// </summary>
    public bool TryStart(Entity<ShuttleConsoleComponent> console, EntityUid grid, EntityUid? dock, EntityUid? pilot)
    {
        if (!TryGetShip(console, grid, out var ship))
        {
            Tell(console, pilot, "wf-radar-menu-dock-out-of-reach");
            return false;
        }

        if (_docking.GetDocks(ship).Exists(own => own.Comp.Docked))
        {
            Tell(console, pilot, "wf-radar-menu-dock-undock-first");
            return false;
        }

        if (!_pilots.TryPlanDock(ship, grid, Navigation.DockStandoff, out var plan, dock))
        {
            Tell(console, pilot, "wf-radar-menu-dock-none");
            return false;
        }

        // The helm's own autopilot lets go, and any orbit trip ends; the docking flies the ship itself from here.
        // A flying autopilot stops its steering as it shuts down on the planner's next update, which would take the
        // docking's with it, so the docking waits for it to let go first.
        var flying = AutopilotEngaged(console) || HasComp<ShipSteererComponent>(console);
        StopAutopilot(console);
        RemCompDeferred<WFAutoOrbitComponent>(console);
        var request = EnsureComp<WFAutoDockComponent>(console);
        request.TargetGrid = grid;
        request.RequestedDock = dock;
        request.Plan = plan;
        request.Attempts = 0;
        request.Pilot = pilot;
        if (flying)
        {
            request.Phase = WFAutoDockPhase.Release;
            request.PhaseStart = _timing.CurTime;
            request.NextCheck = _timing.CurTime;
        }
        else
            SetPhase(console, request, WFAutoDockPhase.Approach);

        Tell(console, pilot, "wf-radar-menu-dock-started", ("grid", Name(grid)));
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateOrbit();
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFAutoDockComponent>();
        while (query.MoveNext(out var uid, out var request))
        {
            if (now < request.NextCheck)
                continue;
            request.NextCheck = now + CheckGap;

            if (Advance(uid, request) is { } reason)
            {
                RemCompDeferred<WFAutoDockComponent>(uid);
                if (reason.Length > 0)
                    Tell(uid, request.Pilot, reason);
            }
        }
    }

    /// <summary>
    /// Moves a docking on one step, as an NPC pilot's Dock order does. Null while it carries on; once it ends, the
    /// message for the pilot, or an empty string when docked.
    /// </summary>
    public string? Advance(EntityUid console, WFAutoDockComponent request)
    {
        var plan = request.Plan;
        if (TerminatingOrDeleted(request.TargetGrid) || !TryGetShip(console, request.TargetGrid, out var ship)
            || Transform(ship).MapUid is not { } map)
            return Fail(console, "wf-radar-menu-dock-failed-gone");

        // The autopilot that was flying lets go of the controls on the planner's next update; then the docking takes
        // them. If it never does, its steering is stopped here.
        if (request.Phase == WFAutoDockPhase.Release)
        {
            var waited = _timing.CurTime - request.PhaseStart;
            if (HasComp<ShipSteererComponent>(console) && waited < ReleaseWait)
                return null;
            _steering.Stop(console);
            SetPhase(console, request, WFAutoDockPhase.Approach);
            return null;
        }

        // Something else took the controls: the steering was stopped, or someone flies the ship by hand.
        if (!TryComp<ShipSteererComponent>(console, out var steerer)
            || TryComp<PilotedShuttleComponent>(ship, out var piloted) && piloted.ActiveSources > 1)
            return Fail(console, "wf-radar-menu-dock-failed-cancelled");

        if (!TryFreeDock(plan.OwnDock, out var ownDock) || !TryFreeDock(plan.TargetDock, out var targetDock))
            return Retry(console, request, ship, "wf-radar-menu-dock-failed-taken");

        var collision = request.Collision;
        request.Collision = null;
        var elapsed = (float) (_timing.CurTime - request.PhaseStart).TotalSeconds;
        var heading = _pilots.DockHeading(plan, request.TargetGrid);
        var shipPosition = _xform.GetWorldPosition(ship);
        var standoff = _xform.ToMapCoordinates(plan.Standoff).Position;

        switch (request.Phase)
        {
            case WFAutoDockPhase.Approach:
            {
                // Far out it cruises with the helm's north forward; near the standoff it turns to dock.
                var cruising = (standoff - shipPosition).Length() > MathF.Max(Navigation.DockAlignmentRange, Navigation.DockStandoff * 2f);
                steerer.Coordinates = _pilots.InDockCorridor(ship, request.TargetGrid, plan, Transform(ship).MapID,
                    shipPosition, standoff, Navigation.DockStandoff)
                    ? plan.Standoff
                    : _pilots.StandoffOnMap(plan, map);
                steerer.InRangeRotation = cruising ? null : heading;
                steerer.AlwaysFaceTarget = true;
                steerer.TargetRotation = cruising ? HelmOffset(console, ship) : _pilots.CreepHeadingOffset(steerer, heading, ship);
                if ((standoff - shipPosition).Length() <= Navigation.DockStandoff)
                    steerer.AvoidCollisions = false;

                if (elapsed >= request.ApproachBudget)
                    return Retry(console, request, ship, "wf-radar-menu-dock-failed-short");
                if (steerer.Status == ShipSteeringStatus.InRange)
                    SetPhase(console, request, WFAutoDockPhase.Settle);
                return null;
            }
            case WFAutoDockPhase.Settle:
            {
                if (_pilots.Settled(ship, request.TargetGrid, Navigation))
                    SetPhase(console, request, WFAutoDockPhase.Creep);
                else if (elapsed >= Navigation.DockSettleTimeout)
                    return Retry(console, request, ship, "wf-radar-menu-dock-failed-align");
                return null;
            }
            case WFAutoDockPhase.Creep:
            {
                steerer.TargetRotation = _pilots.CreepHeadingOffset(steerer, heading, ship);
                if (_docking.CanDock((plan.OwnDock, ownDock), (plan.TargetDock, targetDock)))
                {
                    _docking.Dock((plan.OwnDock, ownDock), (plan.TargetDock, targetDock));
                    _steering.Stop(console);
                    Tell(console, request.Pilot, "wf-radar-menu-dock-done", ("grid", Name(request.TargetGrid)));
                    return string.Empty;
                }

                // Touching the target with the ports this close is the ports mating, not a bump.
                var portGap = Vector2.Distance(_xform.GetWorldPosition(plan.OwnDock), _xform.GetWorldPosition(plan.TargetDock));
                var bumped = collision is { } hit && (hit != request.TargetGrid || portGap > 2.5f);
                if (bumped || elapsed >= Navigation.DockCreepTimeout)
                    return Retry(console, request, ship, "wf-radar-menu-dock-failed-align");
                return null;
            }
        }

        return null;
    }

    /// <summary>Points the steering at a phase, with the NPC pilots' docking settings, and starts its clock.</summary>
    private void SetPhase(EntityUid console, WFAutoDockComponent request, WFAutoDockPhase phase)
    {
        request.Phase = phase;
        request.PhaseStart = _timing.CurTime;
        request.NextCheck = _timing.CurTime;
        if (Transform(console).GridUid is not { } ship || Transform(ship).MapUid is not { } map)
            return;

        var plan = request.Plan;
        // The approach's time is set once from the distance to fly, as an NPC pilot's, so it doesn't shrink en route.
        if (phase == WFAutoDockPhase.Approach)
        {
            var distance = Vector2.Distance(_xform.ToMapCoordinates(plan.Standoff).Position, _xform.GetWorldPosition(ship));
            request.ApproachBudget = Navigation.DockApproachTimeout + distance / MathF.Max(Navigation.CruiseSpeed, 1f) * 1.5f;
        }

        var heading = _pilots.DockHeading(plan, request.TargetGrid);
        var target = phase == WFAutoDockPhase.Creep ? plan.Final : _pilots.StandoffOnMap(plan, map);
        if (_steering.Steer(console, target) is not { } steerer)
            return;

        steerer.Mode = ShipSteeringMode.GoToRange;
        steerer.RangeTolerance = null;
        steerer.AvoidProjectiles = false;
        steerer.EvasionBuffer = Navigation.DockEvasionBuffer;
        steerer.BaseEvasionTime = Navigation.DockEvasionLookahead;
        steerer.RotationCompensation = 0f;
        steerer.RotationCompensationGain = 0f;
        steerer.InRangeRotation = heading;
        steerer.Status = ShipSteeringStatus.Moving;
        switch (phase)
        {
            case WFAutoDockPhase.Approach:
                steerer.Range = Navigation.DockStandoffRange;
                steerer.InRangeMaxSpeed = Navigation.DockApproachSpeed;
                steerer.AvoidCollisions = true;
                steerer.FinishOnCollide = true;
                steerer.AlwaysFaceTarget = true;
                steerer.MaxRotateRate = null;
                break;
            case WFAutoDockPhase.Settle:
                steerer.Range = Navigation.DockStandoffRange;
                steerer.InRangeMaxSpeed = Navigation.DockSettleSpeed;
                steerer.AvoidCollisions = false;
                steerer.FinishOnCollide = true;
                steerer.AlwaysFaceTarget = false;
                steerer.MaxRotateRate = Navigation.DockSettleTurnRate;
                break;
            case WFAutoDockPhase.Creep:
                // On the target grid, so the docked pose moves with it; avoidance would refuse to touch it.
                steerer.Range = Navigation.DockCreepRange;
                steerer.InRangeMaxSpeed = Navigation.DockCreepSpeed;
                steerer.AvoidCollisions = false;
                steerer.FinishOnCollide = false;
                steerer.AlwaysFaceTarget = true;
                steerer.MaxRotateRate = null;
                steerer.TargetRotation = _pilots.CreepHeadingOffset(steerer, heading, ship);
                break;
        }
    }

    /// <summary>A failed try: back out to the standoff with a fresh plan, or stop once the tries are spent.</summary>
    private string? Retry(EntityUid console, WFAutoDockComponent request, EntityUid ship, string reason)
    {
        request.Attempts++;
        if (request.Attempts >= MaxAttempts
            || !_pilots.TryPlanDock(ship, request.TargetGrid, Navigation.DockStandoff, out var plan, request.RequestedDock)
            && !_pilots.TryPlanDock(ship, request.TargetGrid, Navigation.DockStandoff, out plan))
            return Fail(console, reason);

        request.Plan = plan;
        SetPhase(console, request, WFAutoDockPhase.Approach);
        return null;
    }

    /// <summary>Ends a docking that cannot go on: the ship lets go of the controls.</summary>
    private string Fail(EntityUid console, string reason)
    {
        _steering.Stop(console);
        return reason;
    }

    /// <summary>The helm's north against the hull, for cruising nose first as a pilot sat there would.</summary>
    private float HelmOffset(EntityUid console, EntityUid ship)
    {
        return (float) (_xform.GetWorldRotation(ship) - _xform.GetWorldRotation(console)).Degrees;
    }

    private bool TryFreeDock(EntityUid uid, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DockingComponent? dock)
    {
        dock = null;
        return !TerminatingOrDeleted(uid) && Transform(uid).Anchored && TryComp(uid, out dock) && dock.DockedWith == null;
    }

    private void OnCollide(Entity<WFAutoDockComponent> ent, ref PilotedShuttleRelayedEvent<StartCollideEvent> args)
    {
        var other = args.Args.OtherEntity;
        if (ent.Comp.Phase != WFAutoDockPhase.Creep || !HasComp<MapGridComponent>(other))
            return;

        // A hit on anything but the target outweighs touching the target.
        if (ent.Comp.Collision == null || ent.Comp.Collision == ent.Comp.TargetGrid)
            ent.Comp.Collision = other;
    }

    private void OnOtherAutopilot(Entity<WFAutoDockComponent> ent, ref ShuttleConsoleAutopilotPositionMessage args)
    {
        _steering.Stop(ent.Owner);
        RemCompDeferred<WFAutoDockComponent>(ent);
    }

    private void OnShutdown(Entity<WFAutoDockComponent> ent, ref ComponentShutdown args)
    {
        _nextOptions.Remove(ent);
    }

    private void OnCancel(Entity<ShuttleConsoleComponent> ent, ref WFAutopilotCancelMessage args)
    {
        if (HasComp<WFAutoDockComponent>(ent))
            _steering.Stop(ent.Owner);
        RemCompDeferred<WFAutoDockComponent>(ent);
        RemCompDeferred<WFAutoOrbitComponent>(ent);
        StopAutopilot(ent);
        Tell(ent, args.Actor, "wf-radar-menu-autopilot-cancelled");
    }

    /// <summary>Clears the helm's autopilot target, which ends its flight on the planner's next update.</summary>
    public void StopAutopilot(EntityUid console)
    {
        if (!TryComp<HTNComponent>(console, out var htn) || !TryComp<ShuttleConsoleComponent>(console, out var helm))
            return;

        htn.Blackboard.Remove<EntityCoordinates>(helm.AutopilotTargetKey);
        htn.Blackboard.Remove<Angle>(helm.AutopilotRotationKey);
    }

    /// <summary>The ship a helm flies, when it can reach the grid: same map, in range, not in FTL, and not the grid itself.</summary>
    private bool TryGetShip(EntityUid console, EntityUid grid, out EntityUid ship)
    {
        ship = default;
        if (Transform(console).GridUid is not { } own || own == grid || !HasComp<ShuttleComponent>(own)
            || HasComp<FTLComponent>(own) || !HasComp<MapGridComponent>(grid)
            || Transform(own).MapID != Transform(grid).MapID
            || Vector2.Distance(_xform.GetWorldPosition(own), _xform.GetWorldPosition(grid)) > MaxRange)
            return false;

        ship = own;
        return true;
    }

    private string Name(EntityUid grid) => MetaData(grid).EntityName;

    private void Tell(EntityUid console, EntityUid? pilot, string key, params (string, object)[] args)
    {
        var message = Loc.GetString(key, args);
        if (pilot is { } user && Exists(user))
            _popup.PopupEntity(message, console, user, PopupType.Medium);
        else
            _popup.PopupEntity(message, console, PopupType.Medium);
    }
}
