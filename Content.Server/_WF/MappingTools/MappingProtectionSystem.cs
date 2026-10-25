using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Test;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.MappingTools;

/// <summary>
/// Which entities mapping edits never delete, move or copy: players, ghosts, mapping observers, live-test characters,
/// bodies holding a mind, and anything parented under one of them. Used by the mapping tools and mapping mode alike.
/// </summary>
public sealed partial class MappingProtectionSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<ActorComponent> _actorQuery;
    private EntityQuery<GhostComponent> _ghostQuery;
    private EntityQuery<MindContainerComponent> _mindQuery;
    private EntityQuery<WFMappingObserverComponent> _observerQuery;
    private EntityQuery<WFMappingTesterComponent> _testerQuery;

    public override void Initialize()
    {
        base.Initialize();

        _actorQuery = GetEntityQuery<ActorComponent>();
        _ghostQuery = GetEntityQuery<GhostComponent>();
        _mindQuery = GetEntityQuery<MindContainerComponent>();
        _observerQuery = GetEntityQuery<WFMappingObserverComponent>();
        _testerQuery = GetEntityQuery<WFMappingTesterComponent>();
    }

    /// <summary>Whether the entity itself is a player, a ghost, a mapping observer, a test character or a body with a mind.</summary>
    public bool IsProtectedSelf(EntityUid uid)
    {
        return _actorQuery.HasComp(uid) ||
               _ghostQuery.HasComp(uid) ||
               _observerQuery.HasComp(uid) ||
               _testerQuery.HasComp(uid) ||
               _mindQuery.TryComp(uid, out var mind) && mind.HasMind;
    }

    /// <summary>Whether the entity, or anything it is parented to, is protected.</summary>
    public bool IsProtected(EntityUid uid)
    {
        while (uid.IsValid())
        {
            if (IsProtectedSelf(uid))
                return true;

            uid = Transform(uid).ParentUid;
        }

        return false;
    }

    /// <summary>Whether anything parented under the entity, at any depth, is protected.</summary>
    public bool HoldsProtected(EntityUid uid)
    {
        var stack = new Stack<EntityUid>();
        PushChildren(uid, stack);
        while (stack.TryPop(out var child))
        {
            if (IsProtectedSelf(child))
                return true;

            PushChildren(child, stack);
        }

        return false;
    }

    /// <summary>Whether a mapping edit may delete, move or copy the entity: neither protected nor holding one.</summary>
    public bool CanTouch(EntityUid uid)
    {
        return !IsProtected(uid) && !HoldsProtected(uid);
    }

    /// <summary>The topmost protected entities under <paramref name="root"/>, the root itself left out.</summary>
    public List<EntityUid> CollectProtected(EntityUid root)
    {
        var found = new List<EntityUid>();
        var stack = new Stack<EntityUid>();
        PushChildren(root, stack);
        while (stack.TryPop(out var uid))
        {
            if (IsProtectedSelf(uid))
            {
                found.Add(uid);
                continue;
            }

            PushChildren(uid, stack);
        }

        return found;
    }

    /// <summary>
    /// Moves every protected entity under <paramref name="root"/> out of it, onto its map where they are, so deleting
    /// the root doesn't take them along. A root that isn't a grid hands them to the grid under them, if any.
    /// </summary>
    public void MoveProtectedOff(EntityUid root)
    {
        if (TerminatingOrDeleted(root) || Transform(root).MapUid is not { } map || map == root)
            return;

        var keepGrid = !HasComp<MapGridComponent>(root);
        foreach (var uid in CollectProtected(root))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            _container.TryRemoveFromContainer(uid, force: true);
            var (position, rotation) = _transform.GetWorldPositionRotation(uid);
            var xform = Transform(uid);
            _transform.SetCoordinates(uid, xform, new EntityCoordinates(map, position), rotation);
            if (keepGrid)
                _transform.AttachToGridOrMap(uid, xform);
        }
    }

    /// <summary>Moves the protected entities on a map onto another map, at the same position.</summary>
    public void MoveProtectedToMap(EntityUid map, MapId target)
    {
        foreach (var uid in CollectProtected(map))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            _container.TryRemoveFromContainer(uid, force: true);
            _transform.SetMapCoordinates(uid, new MapCoordinates(_transform.GetWorldPosition(uid), target));
        }
    }

    private void PushChildren(EntityUid uid, Stack<EntityUid> stack)
    {
        var children = Transform(uid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            stack.Push(child);
        }
    }
}
