using System.Linq;
using Content.Server._WG.Dormancy;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>Keeps console operators facing their stations and spaces deck patrols apart.</summary>
public sealed partial class WGCrewRoutineSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCrewPlannerSystem _planner = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;
    [Dependency] private WGCrewWorkSystem _work = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WGDormancySystem _dormancy = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // Only awake crew: a sleeping one neither turns nor walks, and wakes when a player comes.
        var query = EntityQueryEnumerator<ActiveNPCComponent, WGCrewComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var crew, out var htn, out var xform))
        {
            if (!htn.Enabled || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || !_dormancy.IsActive(uid, xform))
                continue;
            if (TryComp<WGPilotDutyComponent>(uid, out var pilot) && pilot.AtHelm
                && TryComp<PilotComponent>(uid, out var held) && held.Console is { } helm)
                Face(uid, helm);
            if (TryComp<WGGunnerDutyComponent>(uid, out var gunner) && gunner.AtConsole && gunner.Console is { } console)
                Face(uid, console);
            _crew.MarkPostReached(uid, crew, htn, xform);
            if (crew.NextPatrol == TimeSpan.Zero)
                crew.NextPatrol = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(45, 91));
            if (_timing.CurTime < crew.NextPatrol || crew.Duty != WGCrewDuties.Guard
                || crew.Role != WGCrewRoles.Deckhand && crew.Role != WGCrewRoles.Marine
                || xform.GridUid is not { } grid || crew.Post is not { } home || home.EntityId != grid
                || _work.HomeGrid(uid) != null || _weapons.HasLiveThreat(uid))
                continue;
            crew.NextPatrol = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(45, 91));
            if (NextPatrolPoint(uid, grid, xform) is not { } point)
                continue;
            _crew.SetPost(uid, crew, htn, point);
            EntityManager.System<WGCrewSpeechSystem>().Say(uid, "patrol");
            EntityManager.System<HTNSystem>().Replan(htn);
        }
    }

    /// <summary>
    /// Where a guard walks next: along his standing route, staying put with none, or else a deck post away from here.
    /// A point another crewman stands on or is heading for is passed over while a free one is left, so guards don't
    /// crowd one spot.
    /// </summary>
    public EntityCoordinates? NextPatrolPoint(EntityUid uid, EntityUid grid, TransformComponent xform)
    {
        var taken = TakenPoints(uid, grid);
        if (TryComp<WGCrewPatrolOrdersComponent>(uid, out var orders))
        {
            if (orders.Route.Count == 0)
                return null;

            orders.Next %= orders.Route.Count;
            var start = orders.Next;
            for (var i = 0; i < orders.Route.Count; i++)
            {
                var index = (start + i) % orders.Route.Count;
                if (IsTaken(orders.Route[index], taken))
                    continue;

                orders.Next = index + 1;
                return orders.Route[index];
            }

            // Every point is held: the round still turns.
            orders.Next = start + 1;
            return orders.Route[start];
        }

        var posts = _planner.DeckPosts(grid)
            .Where(post => !_transform.InRange(post.Coordinates, xform.Coordinates, 3f) && !IsTaken(post.Coordinates, taken))
            .ToList();
        return posts.Count == 0 ? null : _random.Pick(posts).Coordinates;
    }

    /// <summary>Where the other living crew on a grid stand and where they are headed.</summary>
    private List<EntityCoordinates> TakenPoints(EntityUid uid, EntityUid grid)
    {
        var taken = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<WGCrewComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var other, out _, out var htn, out var xform))
        {
            if (other == uid || xform.GridUid != grid || !_mobs.IsAlive(other))
                continue;

            taken.Add(xform.Coordinates);
            if (htn.Blackboard.TryGetValue<EntityCoordinates>(WGCrewSystem.PostKey, out var post, EntityManager))
                taken.Add(post);
        }

        return taken;
    }

    private bool IsTaken(EntityCoordinates point, List<EntityCoordinates> taken)
    {
        foreach (var other in taken)
        {
            if (_transform.InRange(point, other, PatrolSpacing))
                return true;
        }

        return false;
    }

    /// <summary>How far a patrol point must be from another crewman, or from where one is headed.</summary>
    private const float PatrolSpacing = 2f;

    /// <summary>Faces a console while it is occupied, including after movement systems rotate the operator.</summary>
    public void Face(EntityUid mob, EntityUid console)
    {
        if (TerminatingOrDeleted(console))
            return;
        var direction = _transform.GetWorldPosition(console) - _transform.GetWorldPosition(mob);
        if (direction.LengthSquared() > 0.001f)
            _transform.SetWorldRotation(mob, direction.ToWorldAngle());
    }
}
