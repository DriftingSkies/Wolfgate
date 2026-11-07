using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.MeridianTraits.Hemophage;

/// <summary>The predicted side of a hemophage: how the tumor's states slow it down and make it fragile.</summary>
public abstract partial class SharedWFHemophageSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] protected MovementSpeedModifierSystem Movement = default!;

    private static readonly ProtoId<DamageGroupPrototype>[] DormantGroups = ["Brute", "Burn", "Toxin"];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFHemophageComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<WFHemophageComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<WFHemophageComponent, BeforeStaminaDamageEvent>(OnStaminaDamage);
    }

    private void OnStaminaDamage(Entity<WFHemophageComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        if (ent.Comp.Dormant && args.Value > 0f)
            args.Value *= ent.Comp.DormantStaminaMultiplier;
    }

    private void OnRefreshSpeed(Entity<WFHemophageComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Dormant)
            args.ModifySpeed(ent.Comp.DormantSpeedMultiplier);
        else if (ent.Comp.MasterOfTheHouse)
            args.ModifySpeed(ent.Comp.MasterSpeedMultiplier);
    }

    private void OnDamageModify(Entity<WFHemophageComponent> ent, ref DamageModifyEvent args)
    {
        if (!ent.Comp.Dormant)
            return;

        foreach (var groupId in DormantGroups)
        {
            if (!_proto.TryIndex(groupId, out var group))
                continue;

            foreach (var type in group.DamageTypes)
            {
                if (args.Damage.DamageDict.TryGetValue(type, out var amount) && amount > 0)
                    args.Damage.DamageDict[type] = amount * ent.Comp.DormantDamageMultiplier;
            }
        }
    }
}
