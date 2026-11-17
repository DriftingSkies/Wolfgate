using System.Numerics;
using Content.Server._WG.Planets;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Shuttles;
using Content.Shared._WG.Planets;
using Content.Shared._WG.RadarMenu;
using Robust.Shared.Map;

namespace Content.Server._WG.RadarMenu;

/// <summary>
/// "Autopilot into orbit" from the radar menu: the helm's autopilot flies toward a world and, once the ship is inside the
/// world's orbit range, enters orbit through the same entry as the console's orbit button.
/// </summary>
public sealed partial class WGAutoDockSystem
{
    [Dependency] private WGOrbitEntrySystem _orbits = default!;

    /// <summary>How far into a world's orbit range, as a share of it, the autopilot aims.</summary>
    private const float OrbitAimDepth = 0.75f;

    private static readonly TimeSpan OrbitCheckGap = TimeSpan.FromSeconds(0.5);

    private void InitializeOrbit()
    {
        SubscribeLocalEvent<WGAutoOrbitComponent, ShuttleConsoleAutopilotPositionMessage>(OnOrbitOtherAutopilot);
    }

    private void OnOrbitRequest(Entity<ShuttleConsoleComponent> ent, ref WGAutoOrbitRequestMessage args)
    {
        if (TryGetEntity(args.Planet, out var planet))
            TryStartOrbit(ent, planet.Value, args.Confirmed, args.Actor);
    }

    /// <summary>
    /// Sends the helm's autopilot toward a world to enter its orbit, or enters at once when already in range. False,
    /// with the pilot told why, when the world has no orbit to enter or is unsanctioned and unconfirmed.
    /// </summary>
    public bool TryStartOrbit(Entity<ShuttleConsoleComponent> console, EntityUid planet, bool confirmed, EntityUid? pilot)
    {
        if (!TryComp<WGSectorPlanetComponent>(planet, out var sector) || OrbitRange(sector) is not { } range
            || Transform(console).GridUid is not { } ship || Transform(ship).MapID != Transform(planet).MapID)
        {
            Tell(console, pilot, "wg-radar-menu-orbit-none");
            return false;
        }

        // Unsanctioned worlds need a confirmed request, as the orbit button asks.
        if (!confirmed && !sector.Sanctioned)
        {
            Tell(console, pilot, "wg-orbit-unsanctioned-unconfirmed", ("planet", Name(planet)));
            return false;
        }

        // A new trip replaces a docking.
        if (HasComp<WGAutoDockComponent>(console))
        {
            _steering.Stop(console.Owner);
            RemComp<WGAutoDockComponent>(console);
        }

        var shipPosition = _xform.GetWorldPosition(ship);
        var centre = _xform.GetWorldPosition(planet);
        if (Vector2.Distance(shipPosition, centre) <= range)
            return EnterOrbit(console, planet, pilot);

        if (!TryComp<HTNComponent>(console, out var htn))
            return false;

        // Aim well inside the orbit range on our side, not at the world's middle.
        var toShip = shipPosition - centre;
        var aim = centre + Vector2.Normalize(toShip) * range * OrbitAimDepth;
        htn.Blackboard.SetValue(console.Comp.AutopilotTargetKey,
            _xform.ToCoordinates(new MapCoordinates(aim, Transform(ship).MapID)));
        htn.Blackboard.SetValue(console.Comp.AutopilotRotationKey, _xform.GetWorldRotation(ship) + MathF.PI);

        var request = EnsureComp<WGAutoOrbitComponent>(console);
        request.Planet = planet;
        request.Pilot = pilot;
        request.NextCheck = _timing.CurTime;
        Tell(console, pilot, "wg-radar-menu-orbit-started", ("planet", Name(planet)));
        return true;
    }

    /// <summary>Enters orbit once the ship is in range; drops the request if the autopilot was stopped short.</summary>
    private void UpdateOrbit()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGAutoOrbitComponent>();
        while (query.MoveNext(out var uid, out var request))
        {
            if (now < request.NextCheck)
                continue;
            request.NextCheck = now + OrbitCheckGap;
            AdvanceOrbit(uid, request);
        }
    }

    /// <summary>One check of an orbit trip: enter once in range, else carry on while the autopilot flies.</summary>
    public void AdvanceOrbit(EntityUid console, WGAutoOrbitComponent request)
    {
        if (TerminatingOrDeleted(request.Planet) || !TryComp<WGSectorPlanetComponent>(request.Planet, out var sector)
            || OrbitRange(sector) is not { } range || Transform(console).GridUid is not { } ship
            || Transform(ship).MapID != Transform(request.Planet).MapID)
        {
            RemCompDeferred<WGAutoOrbitComponent>(console);
            return;
        }

        if (Vector2.Distance(_xform.GetWorldPosition(ship), _xform.GetWorldPosition(request.Planet)) <= range)
        {
            RemCompDeferred<WGAutoOrbitComponent>(console);
            StopAutopilot(console);
            EnterOrbit(console, request.Planet, request.Pilot);
            return;
        }

        // The pilot cancelled or flew off it: the trip is over.
        if (!AutopilotEngaged(console))
            RemCompDeferred<WGAutoOrbitComponent>(console);
    }

    private bool EnterOrbit(EntityUid console, EntityUid planet, EntityUid? pilot)
    {
        if (_orbits.TryEnterOrbit(console, planet, out var reason))
            return true;

        Tell(console, pilot, "wg-radar-menu-orbit-failed", ("reason", reason));
        return false;
    }

    /// <summary>The range of a world's orbit, or null when it has none to enter.</summary>
    private float? OrbitRange(WGSectorPlanetComponent sector)
    {
        return sector.OrbitMap is { } orbitNet && TryGetEntity(orbitNet, out var orbit)
            && TryComp<WGOrbitLayerComponent>(orbit, out var layer)
            ? layer.Range
            : null;
    }

    private bool AutopilotEngaged(EntityUid console)
    {
        return TryComp<HTNComponent>(console, out var htn) && TryComp<ShuttleConsoleComponent>(console, out var helm)
            && htn.Blackboard.TryGetValue<EntityCoordinates>(helm.AutopilotTargetKey, out _, EntityManager);
    }

    private void OnOrbitOtherAutopilot(Entity<WGAutoOrbitComponent> ent, ref ShuttleConsoleAutopilotPositionMessage args)
    {
        RemCompDeferred<WGAutoOrbitComponent>(ent);
    }
}
