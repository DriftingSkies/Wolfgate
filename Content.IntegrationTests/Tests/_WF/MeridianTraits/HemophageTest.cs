#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.MeridianTraits;
using Content.Shared._Onyx.Wounds;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Server._WF.MeridianTraits.Hemophage;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Goobstation.EatToGrow;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.MeridianTraits.Hemophage;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Traits;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.MeridianTraits;

/// <summary>
/// The Hemophagia trait turns a human into a hemophage: the tumor replaces the heart, the body stops eating and
/// breathing, blood drained from a victim fills the reserve, a dormant tumor makes the body fragile, ordinary food is
/// thrown up, bloody food kept and plain drinks rejected, and running out of blood kills.
/// </summary>
[TestFixture]
public sealed class HemophageTest
{
    private static readonly ProtoId<TraitPrototype> Trait = "WFHemophagia";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> Slash = "Slash";

    [Test]
    public async Task HemophageLifecycle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var hemophages = entMan.System<WFHemophageSystem>();
        var body = entMan.System<SharedBodySystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var damageable = entMan.System<DamageableSystem>();
        var mobState = entMan.System<MobStateSystem>();
        var map = await WolfmedGameTest.CreateTestMap(pair);

        EntityUid hemophage = default, victim = default, control = default;
        await server.WaitPost(() =>
        {
            hemophage = entMan.SpawnEntity("MobHuman", map.MapCoords);
            victim = entMan.SpawnEntity("MobHuman", map.MapCoords);
            control = entMan.SpawnEntity("MobHuman", map.MapCoords);
            entMan.AddComponents(hemophage, protoMan.Index(Trait).Components, false);
        });

        await pair.RunSeconds(2);

        WFHemophageComponent comp = default!;
        BloodstreamComponent blood = default!;
        Entity<WFHemophageComponent, BloodstreamComponent> full = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent(hemophage, out WFHemophageComponent? found), "The tumor should make a hemophage.");
            comp = found!;
            blood = entMan.GetComponent<BloodstreamComponent>(hemophage);
            full = (hemophage, comp, blood);

            var tumors = body.GetBodyOrganEntityComps<WFHemophageTumorComponent>(hemophage);
            Assert.That(tumors, Has.Count.EqualTo(1));
            Assert.That(entMan.GetComponent<OrganComponent>(tumors[0].Owner).Removable, Is.False);
            Assert.That(body.GetBodyOrganEntityComps<HeartComponent>(hemophage), Has.Count.EqualTo(1), "Only the tumor should beat.");

            Assert.That(entMan.HasComponent<BreathingImmunityComponent>(hemophage));
            Assert.That(entMan.HasComponent<HungerComponent>(hemophage), Is.False);
            Assert.That(entMan.HasComponent<ThirstComponent>(hemophage), Is.False);

            foreach (var organ in body.GetBodyOrgans(hemophage))
            {
                if (organ.Component.SlotId is not ("liver" or "stomach" or "lungs"))
                    continue;

                Assert.That(entMan.TryGetComponent<WFTumorCorruptedComponent>(organ.Id, out var corrupted) && corrupted.Corrupted,
                    $"The {organ.Component.SlotId} should be corrupted.");
            }

            FixedPoint2 normalVolume = blood.BloodMaxVolume;
            var normal = normalVolume.Float();
            var ratio = hemophages.TotalBlood(full).Float() / normal;
            Assert.That(ratio, Is.EqualTo(comp.StartRatio).Within(0.02f), "A hemophage starts a little over full.");
            Assert.That(comp.Reserve, Is.GreaterThan(FixedPoint2.Zero));
        });

        // Feeding moves the victim's blood into the hemophage.
        await server.WaitAssertion(() =>
        {
            var victimBlood = entMan.GetComponent<BloodstreamComponent>(victim);
            var victimBefore = hemophages.BloodOf(victim, victimBlood);
            var before = hemophages.TotalBlood(full);
            var drained = hemophages.TryFeed(hemophage, victim);

            Assert.That(drained, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(hemophages.BloodOf(victim, victimBlood), Is.EqualTo(victimBefore - drained));
            Assert.That(hemophages.TotalBlood(full), Is.EqualTo(before + drained));
        });

        // A dormant tumor triples brute damage.
        await server.WaitAssertion(() =>
        {
            hemophages.SetDormant((hemophage, comp), true);
            Assert.That(comp.Dormant);

            var hit = new DamageSpecifier(protoMan.Index(Blunt), 5);
            var dealtControl = damageable.TryChangeDamage(control, hit)?.GetTotal() ?? FixedPoint2.Zero;
            var dealtDormant = damageable.TryChangeDamage(hemophage, hit)?.GetTotal() ?? FixedPoint2.Zero;
            Assert.That(dealtControl, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(dealtDormant.Float(), Is.EqualTo(dealtControl.Float() * comp.DormantDamageMultiplier).Within(0.5f));

            hemophages.SetDormant((hemophage, comp), false);
            Assert.That(comp.Dormant, Is.False);
        });

        // Ordinary food comes back up; bloody food stays down.
        EntityUid noodles = default, curd = default;
        await server.WaitPost(() =>
        {
            noodles = entMan.SpawnEntity("FoodNoodlesBoiled", map.MapCoords);
            curd = entMan.SpawnEntity("WFFoodBloodCurd", map.MapCoords);
            FillStomach(entMan, body, solutions, hemophage);
            var ev = new AfterEatingEvent(noodles);
            entMan.EventBus.RaiseLocalEvent(hemophage, ref ev);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(StomachVolume(entMan, body, solutions, hemophage), Is.EqualTo(FixedPoint2.Zero), "Ordinary food should be thrown up.");
            Assert.That(comp.Nausea, Is.GreaterThan(comp.NauseaGross), "Rejected food should leave the hemophage queasy.");
            FillStomach(entMan, body, solutions, hemophage);
            var ev = new AfterEatingEvent(curd);
            entMan.EventBus.RaiseLocalEvent(hemophage, ref ev);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(StomachVolume(entMan, body, solutions, hemophage), Is.GreaterThan(FixedPoint2.Zero), "Bloody food should stay down.");

            // A plain drink upsets the corrupted liver just the same.
            var drank = new WFDrankEvent(noodles, new Solution("Water", 10));
            entMan.EventBus.RaiseLocalEvent(hemophage, ref drank);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(StomachVolume(entMan, body, solutions, hemophage), Is.EqualTo(FixedPoint2.Zero), "Water should be thrown up.");

            // Animal blood is foul: it feeds, but it sickens.
            FixedPoint2 normal = blood.BloodMaxVolume;
            hemophages.RemoveBlood(full, hemophages.TotalBlood(full) - normal * 0.8f);
            comp.Nausea = 0f;
            var monkey = entMan.SpawnEntity("MobMonkey", map.MapCoords);
            Assert.That(hemophages.TryFeed(hemophage, monkey), Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(comp.Nausea, Is.EqualTo(comp.VileNausea));

            // Another species' blood is just as foul; sap isn't blood at all.
            hemophages.RemoveBlood(full, hemophages.TotalBlood(full) - normal * 0.8f);
            comp.Nausea = 0f;
            var arachnid = entMan.SpawnEntity("MobArachnid", map.MapCoords);
            Assert.That(hemophages.TryFeed(hemophage, arachnid), Is.GreaterThan(FixedPoint2.Zero), "Copper blood should feed.");
            Assert.That(comp.Nausea, Is.EqualTo(comp.VileNausea), "Copper blood should sicken.");
            Assert.That(hemophages.TotalBlood(full), Is.LessThanOrEqualTo(normal * comp.AnimalBloodCapRatio), "Foreign blood caps like an animal's.");
            var diona = entMan.SpawnEntity("MobDiona", map.MapCoords);
            Assert.That(hemophages.TryFeed(hemophage, diona), Is.EqualTo(FixedPoint2.Zero), "Sap should be refused.");

            // Out of blood, the tumor starves its host to death.
            hemophages.RemoveBlood(full, hemophages.TotalBlood(full) - normal * 0.1f);
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(hemophage), "A hemophage out of blood should die.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The actions as a player uses them: Drain Victim from the corrupted tongue on a pulled victim, the dormant state
    /// and what it disables, Master of the House, regeneration and clotting; plus mail goodies and IV blood past normal.
    /// </summary>
    [Test]
    public async Task HemophageActions()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var hemophages = entMan.System<WFHemophageSystem>();
        var body = entMan.System<SharedBodySystem>();
        var damageable = entMan.System<DamageableSystem>();
        var pulling = entMan.System<PullingSystem>();
        var stamina = entMan.System<StaminaSystem>();
        var wounds = entMan.System<WoundSystem>();
        var blocker = entMan.System<ActionBlockerSystem>();
        var map = await WolfmedGameTest.CreateTestMap(pair);

        EntityUid hemophage = default, victim = default, control = default;
        await server.WaitPost(() =>
        {
            hemophage = entMan.SpawnEntity("MobHuman", map.MapCoords);
            victim = entMan.SpawnEntity("MobHuman", map.MapCoords);
            control = entMan.SpawnEntity("MobHuman", map.MapCoords);
            entMan.AddComponents(hemophage, protoMan.Index(Trait).Components, false);
        });

        await pair.RunSeconds(2);

        WFHemophageComponent comp = default!;
        BloodstreamComponent blood = default!;
        WFHemophageDrainerComponent drainer = default!;
        FixedPoint2 victimBefore = default, before = default, bruise = default;
        await server.WaitAssertion(() =>
        {
            comp = entMan.GetComponent<WFHemophageComponent>(hemophage);
            blood = entMan.GetComponent<BloodstreamComponent>(hemophage);

            var tongue = body.GetBodyOrgans(hemophage).Single(organ => organ.Component.SlotId == "tongue");
            Assert.That(entMan.GetComponent<WFTumorCorruptedComponent>(tongue.Id).Corrupted, "The tongue should be corrupted.");
            Assert.That(entMan.TryGetComponent(hemophage, out WFHemophageDrainerComponent? found), "A corrupted tongue grants Drain Victim.");
            drainer = found!;
            Assert.That(drainer.DrainAction, Is.Not.Null);
            Assert.That(comp.DormantAction, Is.Not.Null);
            Assert.That(comp.RegenAction, Is.Not.Null);
            Assert.That(comp.ClotAction, Is.Not.Null);
            Assert.That(comp.MasterAction, Is.Not.Null);

            // Drain Victim: pull the victim, use the action, wait out the bite.
            Assert.That(pulling.TryStartPull(hemophage, victim));
            victimBefore = hemophages.BloodOf(victim, entMan.GetComponent<BloodstreamComponent>(victim));
            before = hemophages.TotalBlood((hemophage, comp, blood));
            var drain = new WFHemophageDrainActionEvent { Performer = hemophage };
            entMan.EventBus.RaiseLocalEvent(hemophage, drain);
            Assert.That(drain.Handled, "Draining a pulled living victim should start.");

            // The victim is held fast for the bite.
            Assert.That(entMan.HasComponent<WFBeingDrainedComponent>(victim));
            Assert.That(blocker.CanMove(victim), Is.False, "A bitten victim can't walk out of the grab.");
            Assert.That(blocker.CanInteract(victim, null), Is.False, "A bitten victim can't let go of the pull.");
            Assert.That(blocker.CanAttack(victim, hemophage), Is.False, "A bitten victim can't fight back.");
            Assert.That(pulling.TryStopPull(victim, entMan.GetComponent<PullableComponent>(victim), victim), Is.False);
            Assert.That(blocker.CanAttack(control, hemophage), "Bystanders can still attack.");
        });

        await pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            var victimNow = hemophages.BloodOf(victim, entMan.GetComponent<BloodstreamComponent>(victim));
            Assert.That(victimNow, Is.LessThan(victimBefore), "The bite should take the victim's blood.");
            Assert.That(entMan.HasComponent<WFBeingDrainedComponent>(victim), Is.False, "The hold ends with the bite.");
            Assert.That(blocker.CanMove(victim), "The victim can move again after the bite.");
            Assert.That(hemophages.TotalBlood((hemophage, comp, blood)), Is.GreaterThan(before));

            // Dormant state through its action and do-after.
            entMan.EventBus.RaiseLocalEvent(hemophage, new WFHemophageDormantActionEvent { Performer = hemophage });
        });

        await pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            Assert.That(comp.Dormant, "The dormant do-after should put the tumor to sleep.");
            Assert.That(entMan.GetComponent<InstantActionComponent>(comp.RegenAction!.Value).Enabled, Is.False);
            Assert.That(entMan.GetComponent<InstantActionComponent>(drainer.DrainAction!.Value).Enabled, Is.False);

            stamina.TakeStaminaDamage(hemophage, 10f);
            stamina.TakeStaminaDamage(control, 10f);
            var dormantStamina = entMan.GetComponent<StaminaComponent>(hemophage).StaminaDamage;
            var controlStamina = entMan.GetComponent<StaminaComponent>(control).StaminaDamage;
            Assert.That(dormantStamina, Is.EqualTo(controlStamina * comp.DormantStaminaMultiplier).Within(0.01f));

            hemophages.SetDormant((hemophage, comp), false);
            Assert.That(entMan.GetComponent<InstantActionComponent>(drainer.DrainAction!.Value).Enabled);

            // Master of the House gives the lungs back; ending it with clear lungs restores the immunity at once.
            var staminaComp = entMan.GetComponent<StaminaComponent>(hemophage);
            var crit = staminaComp.CritThreshold;
            entMan.EventBus.RaiseLocalEvent(hemophage, new WFHemophageMasterActionEvent { Performer = hemophage });
            Assert.That(comp.MasterOfTheHouse);
            Assert.That(entMan.HasComponent<BreathingImmunityComponent>(hemophage), Is.False);
            Assert.That(staminaComp.CritThreshold, Is.EqualTo(crit * comp.MasterStaminaMultiplier).Within(0.01f));
            // Another system raising the threshold mid-power keeps its change once the power ends.
            staminaComp.CritThreshold += 10f;
            entMan.EventBus.RaiseLocalEvent(hemophage, new WFHemophageMasterActionEvent { Performer = hemophage });
            Assert.That(comp.MasterOfTheHouse, Is.False);
            Assert.That(entMan.HasComponent<BreathingImmunityComponent>(hemophage));
            Assert.That(staminaComp.CritThreshold, Is.EqualTo(crit + 10f / comp.MasterStaminaMultiplier).Within(0.01f));
            staminaComp.CritThreshold = crit;

            // Regeneration heals for blood.
            damageable.TryChangeDamage(hemophage, new DamageSpecifier(protoMan.Index(Blunt), 20));
            entMan.EventBus.RaiseLocalEvent(hemophage, new WFHemophageRegenActionEvent { Performer = hemophage });
            Assert.That(comp.Regenerating);
            before = hemophages.TotalBlood((hemophage, comp, blood));
            bruise = WoundSeverity(body, wounds, hemophage);
            Assert.That(bruise, Is.GreaterThan(FixedPoint2.Zero), "The blow should leave a wound.");
        });

        await pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            var brute = entMan.GetComponent<DamageableComponent>(hemophage).DamagePerGroup.GetValueOrDefault("Brute");
            // Three ticks at least, all of it on the one damaged type.
            Assert.That(brute.Float(), Is.LessThanOrEqualTo(20f - comp.RegenPerSecond * 3f + 0.01f), "Regeneration should heal its full rate.");
            Assert.That(hemophages.TotalBlood((hemophage, comp, blood)), Is.LessThan(before), "Healing costs blood.");
            Assert.That(WoundSeverity(body, wounds, hemophage).Float(),
                Is.LessThanOrEqualTo(FixedPoint2.Max(FixedPoint2.Zero, bruise - comp.RegenPerSecond * 3f).Float() + 0.01f),
                "Regeneration should close wounds at its full rate, not only what damage removal does.");
            entMan.EventBus.RaiseLocalEvent(hemophage, new WFHemophageRegenActionEvent { Performer = hemophage });
            Assert.That(comp.Regenerating, Is.False);

            // A deep cut bleeds; the clot closes the worst wound.
            damageable.TryChangeDamage(hemophage, new DamageSpecifier(protoMan.Index(Slash), 25));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var clot = new WFHemophageClotActionEvent { Performer = hemophage };
            entMan.EventBus.RaiseLocalEvent(hemophage, clot);
            Assert.That(clot.Handled, "A bleeding wound should be clotted.");
            Assert.That(comp.Clotted, Has.Count.EqualTo(1));
            Assert.That(entMan.HasComponent<WoundBleedingComponent>(comp.Clotted[0].Wound), Is.False);

            // Mail brings blood; IV blood fills the reserve past a full bloodstream.
            var parcels = new Dictionary<string, float>();
            var mail = new WFMailGoodiesEvent(parcels);
            entMan.EventBus.RaiseLocalEvent(hemophage, ref mail);
            Assert.That(parcels, Contains.Key("WFMailHemophageBlood"));

            var room = hemophages.BloodRoom(hemophage)!.Value;
            Assert.That(room, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(hemophages.TryGiveBlood(hemophage, room));
            Assert.That(hemophages.TotalBlood((hemophage, comp, blood)), Is.EqualTo(hemophages.MaxBlood((hemophage, comp, blood))));
            Assert.That(hemophages.BloodRoom(control), Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    private static FixedPoint2 WoundSeverity(SharedBodySystem body, WoundSystem wounds, EntityUid uid)
    {
        var total = FixedPoint2.Zero;
        foreach (var (part, _) in body.GetBodyChildren(uid))
        {
            foreach (var wound in wounds.GetWounds(part))
                total += wound.Comp.Severity;
        }

        return total;
    }

    private static void FillStomach(IEntityManager entMan, SharedBodySystem body, SharedSolutionContainerSystem solutions, EntityUid uid)
    {
        var stomach = body.GetBodyOrganEntityComps<StomachComponent>(uid).First();
        Assert.That(solutions.TryGetSolution(stomach.Owner, StomachSystem.DefaultSolutionName, out var contents, out _));
        solutions.TryAddReagent(contents!.Value, "Nutriment", 10);
    }

    private static FixedPoint2 StomachVolume(IEntityManager entMan, SharedBodySystem body, SharedSolutionContainerSystem solutions, EntityUid uid)
    {
        var stomach = body.GetBodyOrganEntityComps<StomachComponent>(uid).First();
        return solutions.TryGetSolution(stomach.Owner, StomachSystem.DefaultSolutionName, out _, out var solution)
            ? solution.Volume
            : FixedPoint2.Zero;
    }
}
