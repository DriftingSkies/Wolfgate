using Content.Server.Cargo.Components;
using Robust.Shared.Containers;

namespace Content.Server._WF.OutpostRaids;

/// <summary>
/// Gear a raider brought: cargo won't buy it, and an outpost neither keeps it in a save nor pays for it in a sale
/// (<c>WFOutpostSystem.IsKept</c>).
/// </summary>
[RegisterComponent]
public sealed partial class WFRaidLootComponent : Component;

/// <summary>Marks what raiders carry as raid loot.</summary>
public sealed class WFRaidLootSystem : EntitySystem
{
    /// <summary>Marks everything on a raid's grid, such as its shuttle, as raid loot.</summary>
    public void MarkGrid(EntityUid grid)
    {
        var pending = new Stack<EntityUid>();
        pending.Push(grid);
        while (pending.TryPop(out var uid))
        {
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                EnsureComp<WFRaidLootComponent>(child);
                EnsureComp<CargoSellBlacklistComponent>(child);
                pending.Push(child);
            }
        }
    }

    /// <summary>Marks everything a mob carries, contents of containers included, as raid loot.</summary>
    public void MarkCarried(EntityUid mob)
    {
        var pending = new Stack<EntityUid>();
        pending.Push(mob);
        while (pending.TryPop(out var uid))
        {
            if (!TryComp<ContainerManagerComponent>(uid, out var manager))
                continue;

            foreach (var container in manager.Containers.Values)
            {
                foreach (var item in container.ContainedEntities)
                {
                    EnsureComp<WFRaidLootComponent>(item);
                    EnsureComp<CargoSellBlacklistComponent>(item);
                    pending.Push(item);
                }
            }
        }
    }
}
