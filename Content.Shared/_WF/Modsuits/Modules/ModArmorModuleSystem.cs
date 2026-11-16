using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModArmorModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModArmorModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModArmorModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<ModArmorModuleComponent, DamageModifyEvent>(OnDamageModify);
    }

    [Dependency] private ModSuitSystem _modSuit = default!;
    private void OnActivated(Entity<ModArmorModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        RefreshArmor(args.Suit, ent.Comp.Slots);
    }
    private void OnDeactivated(Entity<ModArmorModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        RefreshArmor(args.Suit, ent.Comp.Slots);
    }
    private void OnDamageModify(Entity<ModArmorModuleComponent> ent, ref DamageModifyEvent args)
    {
        if (ent.Comp.Slots.Count == 0)
            args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, ent.Comp.Modifiers);
    }

    private void RefreshArmor(EntityUid suitUid, List<string> slots)
    {
        if (slots.Count == 0 || !TryComp<ModSuitComponent>(suitUid, out var suit))
            return;

        foreach (var slot in slots)
        {
            RefreshPartArmor((suitUid, suit), slot);
        }
    }

    private void RefreshPartArmor(Entity<ModSuitComponent> suit, string slot)
    {
        if (_modSuit.GetPart(suit, slot) is not { } part
            || TerminatingOrDeleted(part)
            || !TryComp<ArmorComponent>(part, out var armor)
            || MetaData(part).EntityPrototype is not { } proto
            || !proto.TryGetComponent<ArmorComponent>(out var baseline, Factory))
            return;

        var modifiers = new DamageModifierSet
        {
            Coefficients = new(baseline.Modifiers.Coefficients),
            FlatReduction = new(baseline.Modifiers.FlatReduction),
            IgnoreArmorPierceFlags = baseline.Modifiers.IgnoreArmorPierceFlags,
        };

        foreach (var module in suit.Comp.ModulesContainer.ContainedEntities)
        {
            if (!TryComp<ModArmorModuleComponent>(module, out var moduleArmor)
                || !moduleArmor.Slots.Contains(slot)
                || !TryComp<ModModuleComponent>(module, out var moduleComp)
                || !moduleComp.Active)
                continue;

            foreach (var (type, coefficient) in moduleArmor.Modifiers.Coefficients)
            {
                modifiers.Coefficients[type] = modifiers.Coefficients.TryGetValue(type, out var existing) ? existing * coefficient : coefficient;
            }

            foreach (var (type, reduction) in moduleArmor.Modifiers.FlatReduction)
            {
                modifiers.FlatReduction[type] = modifiers.FlatReduction.TryGetValue(type, out var existing) ? existing + reduction : reduction;
            }
        }

        armor.Modifiers = modifiers;
        Dirty(part, armor);
    }
}
