using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._WG.Administration.RadarTeleport;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Follower;
using Content.Shared.Follower.Components;
using Content.Shared.Ghost;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._WG.Administration.Systems;

/// <summary>
/// Moves an admin ghost to the spot it clicked on the mass scanner.
/// </summary>
public sealed partial class RadarTeleportSystem : EntitySystem
{
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private FollowerSystem _follower = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RadarTeleportRequestEvent>(OnTeleportRequest);
    }

    private void OnTeleportRequest(RadarTeleportRequestEvent ev, EntitySessionEventArgs args)
    {
        TryTeleport(args.SenderSession, ev.Target);
    }

    /// <summary>
    /// Moves the player's ghost to the target. Only for admins who are currently a ghost.
    /// </summary>
    public bool TryTeleport(ICommonSession player, MapCoordinates target)
    {
        if (player.AttachedEntity is not { } ghost
            || !HasComp<GhostComponent>(ghost)
            || !_adminManager.HasAdminFlag(player, AdminFlags.Admin)
            || !float.IsFinite(target.X)
            || !float.IsFinite(target.Y)
            || !_map.MapExists(target.MapId))
            return false;

        if (TryComp<FollowerComponent>(ghost, out var follower))
            _follower.StopFollowingEntity(ghost, follower.Following);

        var xform = Transform(ghost);
        _transform.SetMapCoordinates((ghost, xform), target);
        _transform.AttachToGridOrMap(ghost, xform);
        if (TryComp<PhysicsComponent>(ghost, out var physics))
            _physics.SetLinearVelocity(ghost, Vector2.Zero, body: physics);

        _adminLogger.Add(LogType.Teleport, LogImpact.Low,
            $"{ToPrettyString(ghost):player} teleported to {target} from the mass scanner");
        return true;
    }
}
