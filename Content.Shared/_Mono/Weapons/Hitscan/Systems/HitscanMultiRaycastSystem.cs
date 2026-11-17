using System.Linq;
using Content.Shared._WG.ShipShields; // WOLFGATE(ShipShields)
using Content.Shared._Mono.Weapons.Hitscan.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.Weapons.Hitscan.Systems;

public sealed partial class HitscanMultiRaycastSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ISharedAdminLogManager _log = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private Robust.Shared.Network.INetManager _net = default!; // WOLFGATE(Weapons)

    [Dependency] private EntityQuery<PhysicsComponent> _physicQuery = default!;
    private HashSet<EntityUid> _hitEntities = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanMultiRaycastComponent, HitscanTraceEvent>(OnHitscanFired);
    }

    private void OnHitscanFired(Entity<HitscanMultiRaycastComponent> ent, ref HitscanTraceEvent args)
    {
        var shooter = args.Shooter ?? args.Gun;
        var mapCords = _transform.ToMapCoordinates(args.FromCoordinates);
        var ray = new CollisionRay(mapCords.Position, args.ShotDirection, (int) ent.Comp.CollisionMask);
        var rayCastResults = _physics.IntersectRay(mapCords.MapId, ray, ent.Comp.MaxDistance, shooter, false);
        // WOLFGATE(ShipShields): shield fixtures are resolved against the visible perimeter
        rayCastResults = rayCastResults.Where(hit => !HasComp<WGShipShieldVisualsComponent>(hit.HitEntity));
        var hitDistances = new Dictionary<EntityUid, float>(); // WOLFGATE(ShipShields)
        var hitCount = 0;
        var latestDistance = ent.Comp.MaxDistance;

        foreach (var result in rayCastResults)
        {
            if (!_physicQuery.TryComp(result.HitEntity, out var phys))
                continue;

            _hitEntities.Add(result.HitEntity);
            hitDistances[result.HitEntity] = result.Distance; // WOLFGATE(ShipShields)
            latestDistance = result.Distance;

            hitCount++;

            if (hitCount > ent.Comp.MaxPierce)
                break;

            if ((phys.CollisionLayer & (int) ent.Comp.PierceCollisionMask) != 0x0)
                break;

            // WOLFGATE(ShipShields) START: log piercing targets after shield interception
            // _log.Add(LogType.HitScanHit,
            //     $"{ToPrettyString(shooter):user} hit {ToPrettyString(result.HitEntity):target}"
            //     + $" using {ToPrettyString(args.Gun):entity}.");
            // WOLFGATE END
        }

        var trace = new HitscanRaycastFiredEvent
        {
            FromCoordinates = args.FromCoordinates,
            ShotDirection = args.ShotDirection,
            Gun = args.Gun,
            Shooter = args.Shooter,
            HitEntities = _hitEntities,
            DistanceTried = latestDistance,
            Predicted = args.Predicted, // WOLFGATE(Weapons)
            // WOLFGATE(Weapons): the client predicts the beam only; damage, stun and reflections stay server-side
            Canceled = _net.IsClient,
        };

        // WOLFGATE(ShipShields) START: stop piercing beams while retaining targets before the perimeter
        var shieldTrace = new WGShipShieldHitscanTraceEvent(ent.Owner, trace) { HitDistances = hitDistances };
        RaiseLocalEvent(ref shieldTrace);
        trace = shieldTrace.Trace;
        foreach (var hitEntity in trace.HitEntities)
        {
            _log.Add(LogType.HitScanHit,
                $"{ToPrettyString(shooter):user} hit {ToPrettyString(hitEntity):target}"
                + $" using {ToPrettyString(args.Gun):entity}.");
        }
        // WOLFGATE END
        RaiseLocalEvent(ent, ref trace);
        _hitEntities.Clear();
    }
}
