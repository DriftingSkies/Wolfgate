using System.Diagnostics.CodeAnalysis;
using Content.Server._WF.Outposts.Components;
using Content.Shared._WF.OutpostRaids;
using Content.Shared._WF.Outposts;
using Content.Server.Cargo.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Deploy and repack: a packed gizmo used in hand deploys onto the outpost floor tile in front of its user, and the
/// owner or a builder packs a deployed one up again with all its state, the machine itself going into the pack. The
/// console kit deploys by founding; packing up the console of an untouched outpost gives the founding back.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private TurfSystem _turf = default!;

    private const CollisionGroup DeployBlockers = CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.LowImpassable;

    private void InitializeDeploy()
    {
        SubscribeLocalEvent<WFDeployableComponent, UseInHandEvent>(OnDeployUse);
        SubscribeLocalEvent<WFDeployableComponent, WFDeployDoAfterEvent>(OnDeployDoAfter);
        SubscribeLocalEvent<WFDeployableComponent, PriceCalculationEvent>(OnPackPrice);
        SubscribeLocalEvent<WFRepackableComponent, GetVerbsEvent<AlternativeVerb>>(OnRepackVerbs);
        SubscribeLocalEvent<WFRepackableComponent, WFRepackDoAfterEvent>(OnRepackDoAfter);
    }

    private void OnDeployUse(Entity<WFDeployableComponent> pack, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = args.User;
        // A console kit founds where it lands, so only an outpost gizmo is checked up front.
        if (!HasComp<WFOutpostKitComponent>(pack) && !CanDeploy(user, out _, out _, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), user, user);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, pack.Comp.Delay, new WFDeployDoAfterEvent(), pack, used: pack)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnDeployDoAfter(Entity<WFDeployableComponent> pack, ref WFDeployDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        string reason;
        if (TryComp<WFOutpostKitComponent>(pack, out var kit))
        {
            var front = _transform.ToCoordinates(DeployTarget(args.User));
            if (!TryFound(args.User, (pack, kit), front, out _, out reason))
                _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
            return;
        }

        if (!TryDeploy(args.User, pack, out _, out reason))
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
    }

    /// <summary>The map position one tile in front of the user, the way they face.</summary>
    private MapCoordinates DeployTarget(EntityUid user)
    {
        var (position, rotation) = _transform.GetWorldPositionRotation(user);
        return new MapCoordinates(position + rotation.GetCardinalDir().ToVec(), Transform(user).MapID);
    }

    /// <summary>The outpost floor tile at a map position, on the ground under it.</summary>
    private bool TryGetOutpostTile(MapCoordinates at, out Entity<WFOutpostComponent, MapGridComponent> outpost, out Vector2i index)
    {
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var comp, out var grid))
        {
            if (Transform(uid).MapID != at.MapId)
                continue;

            index = _maps.TileIndicesFor(uid, grid, at);
            if (!_maps.TryGetTileRef(uid, grid, index, out var tile) || tile.Tile.IsEmpty)
                continue;

            outpost = (uid, comp, grid);
            return true;
        }

        outpost = default;
        index = default;
        return false;
    }

    /// <summary>
    /// Whether the user may deploy a gizmo on the tile in front of them: an outpost floor tile they build on, with nothing
    /// standing on it.
    /// </summary>
    public bool CanDeploy(EntityUid user, out Entity<WFOutpostComponent, MapGridComponent> outpost, out Vector2i index, out string reason)
    {
        if (!TryGetOutpostTile(DeployTarget(user), out outpost, out index))
        {
            reason = "wf-outpost-deploy-no-floor";
            return false;
        }

        if (!CanBuild(user, (outpost, outpost.Comp1)))
        {
            reason = "wf-outpost-deploy-not-yours";
            return false;
        }

        if (!_maps.TryGetTileRef(outpost, outpost.Comp2, index, out var tile) || _turf.IsTileBlocked(tile, DeployBlockers))
        {
            reason = "wf-outpost-deploy-blocked";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Deploys a pack on the tile in front of the user, anchored and facing them: the machine it holds, or a new one.
    /// <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryDeploy(EntityUid user, Entity<WFDeployableComponent> pack, [NotNullWhen(true)] out EntityUid? machine, out string reason)
    {
        machine = null;
        if (!CanDeploy(user, out var outpost, out var index, out reason))
            return false;

        if (Deploys<WFOutpostCryopodComponent>(pack) && CountPods(outpost) >= MaxPods)
        {
            reason = "wf-outpost-cryopod-full";
            return false;
        }

        if (Deploys<WFOutpostTurretComponent>(pack) && CountTurrets(outpost) >= _cfg.GetCVar(OutpostRaidCVars.MaxTurrets))
        {
            reason = "wf-outpost-turrets-full";
            return false;
        }

        var coords = _maps.GridTileToLocal(outpost, outpost.Comp2, index);
        if (_containers.TryGetContainer(pack, WFDeployableComponent.ContainerId, out var container)
            && container.ContainedEntities.Count > 0)
        {
            machine = container.ContainedEntities[0];
            _containers.Remove(machine.Value, container, destination: coords);
        }
        else
        {
            machine = Spawn(pack.Comp.Machine, coords);
        }

        // Facing back at whoever set it down, else square with the outpost.
        var facing = pack.Comp.Face
            ? _transform.GetWorldRotation(user).GetCardinalDir().ToAngle() + Angle.FromDegrees(180)
            : _transform.GetWorldRotation(outpost);
        // Spawned or taken out onto the tile already; an anchored prototype is anchored on spawn.
        _transform.SetWorldRotation(machine.Value, facing.Reduced());
        if (!Transform(machine.Value).Anchored)
            _transform.AnchorEntity(machine.Value);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
            $"{ToPrettyString(user):user} deployed {ToPrettyString(machine.Value):entity} on outpost {ToPrettyString(outpost):grid}");
        QueueDel(pack);
        return true;
    }

    /// <summary>Turrets set up on an outpost.</summary>
    public int CountTurrets(EntityUid grid)
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFOutpostTurretComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                count++;
        }

        return count;
    }

    /// <summary>Holding a machine, a pack is worth the machine; empty, its own price.</summary>
    private void OnPackPrice(Entity<WFDeployableComponent> pack, ref PriceCalculationEvent args)
    {
        if (!_containers.TryGetContainer(pack, WFDeployableComponent.ContainerId, out var container)
            || container.ContainedEntities.Count == 0)
            return;

        foreach (var machine in container.ContainedEntities)
            args.Price += _pricing.GetPrice(machine);
        args.Handled = true;
    }

    private void OnRepackVerbs(Entity<WFRepackableComponent> machine, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !CanRepack(args.User, machine, out _))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-outpost-repack-verb"),
            Act = () => _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, machine.Comp.Delay,
                new WFRepackDoAfterEvent(), machine, target: machine)
            {
                BreakOnMove = true,
            }),
        });
    }

    private void OnRepackDoAfter(Entity<WFRepackableComponent> machine, ref WFRepackDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (!TryRepack(args.User, machine, out _, out var reason))
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
    }

    /// <summary>Whether the user may pack the machine up: it stands on an outpost they build on.</summary>
    public bool CanRepack(EntityUid user, Entity<WFRepackableComponent> machine, out string reason)
    {
        if (Transform(machine).GridUid is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || !CanBuild(user, (grid, outpost)))
        {
            reason = "wf-outpost-repack-not-yours";
            return false;
        }

        // A pad holding a ship, or guiding one, stays down until it is clear.
        if (TryComp<WFLandingPadComponent>(machine, out var pad) && (pad.Parked != null || pad.Guiding))
        {
            reason = "wf-pad-in-use";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Packs a deployed machine up into a new pack, given to the user: the machine goes into the pack as it is. The
    /// console only packs up on an outpost still as it was founded, which it gives back. <paramref name="reason"/> is a
    /// loc id when it fails.
    /// </summary>
    public bool TryRepack(EntityUid user, Entity<WFRepackableComponent> machine, [NotNullWhen(true)] out EntityUid? pack, out string reason)
    {
        pack = null;
        if (!CanRepack(user, machine, out reason))
            return false;

        var grid = Transform(machine).GridUid!.Value;
        var outpost = new Entity<WFOutpostComponent>(grid, Comp<WFOutpostComponent>(grid));
        if (HasComp<WFOutpostConsoleComponent>(machine))
            return TryRepackConsole(user, outpost, machine, out pack, out reason);

        pack = Spawn(machine.Comp.Pack, Transform(user).Coordinates);
        var container = _containers.EnsureContainer<ContainerSlot>(pack.Value, WFDeployableComponent.ContainerId);
        _transform.Unanchor(machine, Transform(machine));
        if (!_containers.Insert(machine.Owner, container))
        {
            Del(pack.Value);
            pack = null;
            _transform.AnchorEntity(machine.Owner);
            reason = "wf-outpost-repack-failed";
            return false;
        }

        _hands.PickupOrDrop(user, pack.Value);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
            $"{ToPrettyString(user):user} packed up {ToPrettyString(machine.Owner):entity} on outpost {ToPrettyString(grid):grid}");
        return true;
    }

    /// <summary>Packs up the console of an untouched outpost, its owner's alone: the outpost goes and the kit comes back.</summary>
    private bool TryRepackConsole(EntityUid user, Entity<WFOutpostComponent> outpost, EntityUid console, [NotNullWhen(true)] out EntityUid? pack, out string reason)
    {
        pack = null;
        if (!IsOwnerOf(user, outpost.Comp))
        {
            reason = "wf-outpost-repack-not-yours";
            return false;
        }

        if (_loads.ContainsKey(outpost) || !IsFresh(outpost, out _))
        {
            reason = "wf-outpost-repack-console-built";
            return false;
        }

        var kit = Comp<WFRepackableComponent>(console).Pack;
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"{ToPrettyString(user):user} packed up outpost {ToPrettyString(outpost):grid}");
        // Deleting it puts the ground back and frees the owner to found again; whoever stands on it stays.
        DropLoose(outpost);
        Del(outpost);
        pack = Spawn(kit, Transform(user).Coordinates);
        _hands.PickupOrDrop(user, pack.Value);
        reason = string.Empty;
        return true;
    }
}
