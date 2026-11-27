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
            GetUp(uid);
            if (crew.NextPatrol == TimeSpan.Zero)
                crew.NextPatrol = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(45, 91));
            if (_timing.CurTime < crew.NextPatrol || crew.Duty != WGCrewDuties.Guard
                || crew.Role != WGCrewRoles.Deckhand && crew.Role != WGCrewRoles.Marine
                || xform.GridUid is not { } grid || crew.Post is not { } home || home.EntityId != grid
                || _work.HomeGrid(uid) != null || _weapons.HasLiveThreat(uid)
                || EntityManager.System<WGCrewReadinessSystem>().AtStations(uid)
                || EntityManager.System<WGCrewDowntimeSystem>().OnBreak(uid))
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

    /// <summary>When each crewman found lying in a bed was first seen there.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _inBed = new();

    /// <summary>How long a crewman back on his feet stays in a bed that heals before he gets up anyway.</summary>
    private static readonly TimeSpan BedRest = TimeSpan.FromMinutes(2);

    /// <summary>Below this much damage he gets up from a bed at once.</summary>
    private const float WellEnough = 40f;

    /// <summary>
    /// Gets a crewman out of a bed or off a table once he is up again and no medic is seeing to him: well enough, or
    /// after a short rest. A medic leaves a patient buckled in, and an NPC had nothing to get him up, so a revived
    /// crewman lay there for good. A chair he sat in for a break is the break's to leave.
    /// </summary>
    public void GetUp(EntityUid uid)
    {
        // Beds and tables only, where he lies: a vehicle's or a gun's seat is where he is meant to be.
        if (CompOrNull<Content.Shared.Buckle.Components.BuckleComponent>(uid)?.BuckledTo is not { } strap
            || CompOrNull<Content.Shared.Buckle.Components.StrapComponent>(strap)?.Position != Content.Shared.Buckle.Components.StrapPosition.Down
            || EntityManager.System<WGCrewDowntimeSystem>().OnBreak(uid))
        {
            _inBed.Remove(uid);
            return;
        }

        var medics = EntityManager.System<WGCrewMedicSystem>();
        if (medics.IsDown(uid) || medics.UnderCare(uid))
        {
            _inBed.Remove(uid);
            return;
        }

        var now = _timing.CurTime;
        if (!_inBed.TryGetValue(uid, out var since))
            _inBed[uid] = since = now;
        var damage = CompOrNull<Content.Shared.Damage.DamageableComponent>(uid)?.TotalDamage.Float() ?? 0f;
        if (damage >= WellEnough && now - since < BedRest)
            return;

        _inBed.Remove(uid);
        EntityManager.System<Content.Shared.Buckle.SharedBuckleSystem>().TryUnbuckle(uid, uid, popup: false);
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
