using Content.Server._WF.Outposts.Components;
using Content.Shared._WF.Hideouts;
using Content.Shared.Mobs.Components;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Server._WF.Hideouts;

/// <summary>
/// Module materials: what a level needs besides money, by stack (sheets, cable) and by part (stock parts, medkits,
/// toolboxes). Whatever lies on the outpost counts, in crates and lockers too, but not what people carry; starting the
/// level uses it up.
/// </summary>
public sealed partial class WFHideoutSystem
{
    [Dependency] private SharedStackSystem _stack = default!;

    /// <summary>The key a requirement goes by in the console's counts: a stack type or a part prototype.</summary>
    public static string StackKey(string stack) => "stack:" + stack;
    public static string PartKey(string part) => "part:" + part;

    /// <summary>What the outpost holds of every stack type and part, by requirement key.</summary>
    public Dictionary<string, int> Holdings(EntityUid outpost)
    {
        var held = new Dictionary<string, int>();
        foreach (var uid in Stores(outpost))
        {
            if (TryComp<StackComponent>(uid, out var stack))
                held[StackKey(stack.StackTypeId)] = held.GetValueOrDefault(StackKey(stack.StackTypeId)) + stack.Count;
            if (MetaData(uid).EntityPrototype is { } proto)
                held[PartKey(proto.ID)] = held.GetValueOrDefault(PartKey(proto.ID)) + 1;
        }

        return held;
    }

    /// <summary>Everything lying on the outpost and in its containers, leaving out people and what they carry.</summary>
    private List<EntityUid> Stores(EntityUid outpost)
    {
        var found = new List<EntityUid>();
        var pending = new Stack<EntityUid>();
        pending.Push(outpost);
        while (pending.TryPop(out var parent))
        {
            var children = Transform(parent).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<MobStateComponent>(child) || HasComp<ActorComponent>(child))
                    continue;

                found.Add(child);
                pending.Push(child);
            }

            if (parent == outpost || !TryComp<ContainerManagerComponent>(parent, out var containers))
                continue;

            foreach (var container in containers.Containers.Values)
            {
                foreach (var held in container.ContainedEntities)
                {
                    if (HasComp<MobStateComponent>(held))
                        continue;

                    found.Add(held);
                    pending.Push(held);
                }
            }
        }

        return found;
    }

    /// <summary>Whether the outpost holds everything a level needs besides money.</summary>
    public bool HasMaterials(EntityUid outpost, WFHideoutModuleLevel level)
    {
        var held = Holdings(outpost);
        foreach (var (stack, amount) in level.Stacks)
        {
            if (held.GetValueOrDefault(StackKey(stack)) < amount)
                return false;
        }

        foreach (var (part, amount) in level.Parts)
        {
            if (held.GetValueOrDefault(PartKey(part)) < amount)
                return false;
        }

        return true;
    }

    /// <summary>Uses up what a level needs from the outpost's stores; call only once <see cref="HasMaterials"/> passed.</summary>
    private void TakeMaterials(EntityUid outpost, WFHideoutModuleLevel level)
    {
        var stacks = new Dictionary<string, int>();
        foreach (var (stack, amount) in level.Stacks)
            stacks[stack] = amount;
        var parts = new Dictionary<string, int>();
        foreach (var (part, amount) in level.Parts)
            parts[part] = amount;

        foreach (var uid in Stores(outpost))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (TryComp<StackComponent>(uid, out var stack) && stacks.TryGetValue(stack.StackTypeId, out var wanted) && wanted > 0)
            {
                var take = Math.Min(wanted, stack.Count);
                stacks[stack.StackTypeId] = wanted - take;
                if (take >= stack.Count)
                    Del(uid);
                else
                    _stack.SetCount(uid, stack.Count - take, stack);
                continue;
            }

            if (MetaData(uid).EntityPrototype is { } proto && parts.TryGetValue(proto.ID, out var count) && count > 0)
            {
                parts[proto.ID] = count - 1;
                Del(uid);
            }
        }
    }
}
