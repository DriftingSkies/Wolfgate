using System.Linq;
using Content.Server.Construction;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Shared._WF.Barricades;

namespace Content.Server._WF.Barricades;

/// <summary>Keeps the wire through construction changes and raises the destruction threshold while wired.</summary>
public sealed class BarbedSystem : SharedBarbedSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BarbedComponent, ConstructionChangeEntityEvent>(OnConstructionChange);
        SubscribeLocalEvent<BarbedComponent, BarbedStateChangedEvent>(OnBarbedStateChanged);
    }

    private void OnConstructionChange(Entity<BarbedComponent> ent, ref ConstructionChangeEntityEvent args)
    {
        CopyBarbed(ent, args.New);
    }

    private void OnBarbedStateChanged(Entity<BarbedComponent> ent, ref BarbedStateChangedEvent args)
    {
        if (BarricadeHealth.GetLastDamageTrigger(EntityManager, ent) is not { } trigger)
            return;

        trigger.Damage += ent.Comp.IsBarbed ? ent.Comp.MaxHealthIncrease : -ent.Comp.MaxHealthIncrease;
    }
}

/// <summary>Fills a directional blocker's max health from its destruction threshold.</summary>
public sealed class DirectionalAttackBlockSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<DirectionalAttackBlockerComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<DirectionalAttackBlockerComponent> ent, ref MapInitEvent args)
    {
        if (BarricadeHealth.GetLastDamageTrigger(EntityManager, ent) is not { } trigger)
            return;

        ent.Comp.MaxHealth = trigger.Damage;
        Dirty(ent);
    }
}

internal static class BarricadeHealth
{
    /// <summary>The damage trigger of the entity's last destructible threshold.</summary>
    public static DamageTrigger? GetLastDamageTrigger(IEntityManager entMan, EntityUid uid)
    {
        if (!entMan.TryGetComponent(uid, out DestructibleComponent? destructible))
            return null;

        return destructible.Thresholds.LastOrDefault(t => t.Trigger is DamageTrigger)?.Trigger as DamageTrigger;
    }
}
