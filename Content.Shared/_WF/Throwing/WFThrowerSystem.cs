using Content.Shared.Throwing;

namespace Content.Shared._WF.Throwing;

/// <summary>
/// Clears a thrown item's thrower when the thrower is deleted mid-flight, such as a body gibbed as it throws. The
/// networked thrower otherwise pointed at a deleted entity and every state sent logged an error.
/// </summary>
public sealed class WFThrowerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFThrowerComponent, EntityTerminatingEvent>(OnTerminating);
    }

    /// <summary>Remembers an item a thrower has in flight, forgetting the ones that have landed.</summary>
    public void Track(EntityUid thrower, EntityUid item)
    {
        var comp = EnsureComp<WFThrowerComponent>(thrower);
        comp.InFlight.RemoveWhere(old => !TryComp<ThrownItemComponent>(old, out var thrown) || thrown.Thrower != thrower);
        comp.InFlight.Add(item);
    }

    private void OnTerminating(Entity<WFThrowerComponent> ent, ref EntityTerminatingEvent args)
    {
        foreach (var item in ent.Comp.InFlight)
        {
            if (!TryComp<ThrownItemComponent>(item, out var thrown) || thrown.Thrower != ent.Owner)
                continue;

            thrown.Thrower = null;
            Dirty(item, thrown);
        }
    }
}
