using Content.Server.Popups;
using Content.Server.Spawners.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Handles the demo arena buttons: pick a mob, fight it, restock the item tables.
/// </summary>
public sealed partial class FaunaArenaSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private FaunaDisplaySystem _display = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _leftovers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaArenaSpawnerComponent, ComponentShutdown>(OnSpawnerShutdown);
        SubscribeLocalEvent<FaunaArenaButtonComponent, ActivateInWorldEvent>(OnButtonActivate);
    }

    private void OnSpawnerShutdown(Entity<FaunaArenaSpawnerComponent> ent, ref ComponentShutdown args)
    {
        QueueDel(ent.Comp.Spawned);
    }

    private void OnButtonActivate(Entity<FaunaArenaButtonComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        if (_timing.CurTime < ent.Comp.NextPress)
            return;

        args.Handled = true;
        ent.Comp.NextPress = _timing.CurTime + ent.Comp.Delay;

        var map = _transform.GetMap(ent.Owner);
        if (ent.Comp.Action == FaunaArenaAction.Restock)
        {
            var items = new List<Entity<FaunaDisplaySpawnerComponent>>();
            var itemQuery = EntityQueryEnumerator<FaunaDisplaySpawnerComponent, TransformComponent>();
            while (itemQuery.MoveNext(out var uid, out var spawner, out var xform))
            {
                if (xform.MapUid == map)
                    items.Add((uid, spawner));
            }

            foreach (var item in items)
            {
                _display.Respawn(item, false);
            }

            _popup.PopupEntity(Loc.GetString("fauna-arena-restock"), ent, args.User);
            return;
        }

        Entity<FaunaArenaSpawnerComponent>? found = null;
        var arenaQuery = EntityQueryEnumerator<FaunaArenaSpawnerComponent, TransformComponent>();
        while (arenaQuery.MoveNext(out var uid, out var spawner, out var xform))
        {
            if (xform.MapUid == map && spawner.Roster.Count > 0)
            {
                found = (uid, spawner);
                break;
            }
        }

        if (found is not { } arena)
            return;

        var count = arena.Comp.Roster.Count;
        arena.Comp.Index = ent.Comp.Action switch
        {
            FaunaArenaAction.Previous => ((arena.Comp.Index - 1) % count + count) % count,
            FaunaArenaAction.Next => (arena.Comp.Index + 1) % count,
            _ => Math.Max(arena.Comp.Index, 0),
        };

        var live = ent.Comp.Action == FaunaArenaAction.Fight;
        var proto = arena.Comp.Roster[arena.Comp.Index];
        ClearArena(arena);
        arena.Comp.Spawned = _display.SpawnDisplayMob(proto, Transform(arena).Coordinates, live);

        var message = live
            ? Loc.GetString("fauna-arena-fight")
            : Loc.GetString("fauna-arena-pick",
                ("index", arena.Comp.Index + 1),
                ("count", count),
                ("name", arena.Comp.Spawned is { } mob ? Name(mob) : proto.Id));
        _popup.PopupEntity(message, ent, args.User);
    }

    /// <summary>
    /// Deletes the arena's mob and any non-player mobs, eggs and spawners it left behind.
    /// </summary>
    private void ClearArena(Entity<FaunaArenaSpawnerComponent> ent)
    {
        QueueDel(ent.Comp.Spawned);
        ent.Comp.Spawned = null;

        _leftovers.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.ClearRadius, _leftovers);
        foreach (var uid in _leftovers)
        {
            if (HasComp<ActorComponent>(uid))
                continue;

            if (HasComp<MobStateComponent>(uid) || HasComp<SpawnOnDespawnComponent>(uid) ||
                HasComp<TimedSpawnerComponent>(uid))
                QueueDel(uid);
        }
    }
}
