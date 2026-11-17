#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WG.Avali.Feathers;
using Content.Server.Forensics;
using Content.Shared._WG.Avali.Feathers;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Forensics.Components;
using Content.Shared.Humanoid;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Avali;

/// <summary>Preening spends feathers, preserves donor evidence, and regrows only to the configured cap.</summary>
[TestFixture]
[TestOf(typeof(PreenableSystem))]
public sealed class AvaliPreeningTest : InteractionTest
{
    private static readonly ProtoId<DamageTypePrototype> BluntProto = "Blunt";

    [Test]
    public async Task SelfAndOtherPreeningConsumeFeathersAndCopyDonorData()
    {
        var selfTarget = await SpawnTarget("MobAvali");
        var other = await SpawnEntity("MobAvali", SEntMan.GetCoordinates(TargetCoords));
        var self = ToServer(selfTarget);
        const string selfDna = "Avali-preening-self-dna";
        const string otherDna = "Avali-preening-other-dna";
        var selfColor = new Color(0.18f, 0.46f, 0.72f);
        var otherColor = new Color(0.72f, 0.32f, 0.18f);

        await Server.WaitAssertion(() =>
        {
            SEntMan.GetComponent<HumanoidAppearanceComponent>(self).SkinColor = selfColor;
            SEntMan.GetComponent<DnaComponent>(self).DNA = selfDna;
            SEntMan.GetComponent<HumanoidAppearanceComponent>(other).SkinColor = otherColor;
            SEntMan.GetComponent<DnaComponent>(other).DNA = otherDna;

            var doAfter = SEntMan.System<SharedDoAfterSystem>();
            var selfDoAfter = new DoAfterArgs(SEntMan, self, 5f, new PreeningEvent(), self, self)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
            };
            var otherDoAfter = new DoAfterArgs(SEntMan, SPlayer, 5f, new PreeningEvent(), other, other)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
            };
            Assert.That(doAfter.TryStartDoAfter(selfDoAfter), Is.True);
            Assert.That(doAfter.TryStartDoAfter(otherDoAfter), Is.True);
        });

        await RunSeconds(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<PreenableComponent>(self).CurrentFeathers, Is.EqualTo(2));
            Assert.That(SEntMan.GetComponent<PreenableComponent>(other).CurrentFeathers, Is.EqualTo(2));

            var feathers = new List<EntityUid>();
            var query = SEntMan.EntityQueryEnumerator<FeatherComponent>();
            while (query.MoveNext(out var uid, out _))
                feathers.Add(uid);

            Assert.That(feathers, Has.Count.EqualTo(2));
            var appearance = SEntMan.System<SharedAppearanceSystem>();
            var byDna = new Dictionary<string, EntityUid>();
            foreach (var feather in feathers)
            {
                var evidence = SEntMan.GetComponent<ForensicsComponent>(feather);
                foreach (var dna in evidence.DNAs)
                    byDna[dna] = feather;
            }

            Assert.That(byDna.ContainsKey(selfDna), Is.True, "Self-preened feather lost the donor DNA.");
            Assert.That(byDna.ContainsKey(otherDna), Is.True, "Other-preened feather lost the donor DNA.");
            Assert.That(appearance.TryGetData<Color>(byDna[selfDna], FeatherVisuals.FeatherColor, out var selfFeatherColor), Is.True);
            Assert.That(appearance.TryGetData<Color>(byDna[otherDna], FeatherVisuals.FeatherColor, out var otherFeatherColor), Is.True);
            Assert.That(selfFeatherColor, Is.EqualTo(selfColor), "Self-preened feather lost the donor color.");
            Assert.That(otherFeatherColor, Is.EqualTo(otherColor), "Other-preened feather lost the donor color.");
        });
    }

    [Test]
    public async Task CancelledAndEmptyPreeningDoNotCreateFeathers()
    {
        var targetNet = await SpawnTarget("MobAvali");
        var target = ToServer(targetNet);
        DoAfterId? doAfterId = null;

        await Server.WaitAssertion(() =>
        {
            var doAfter = SEntMan.System<SharedDoAfterSystem>();
            Assert.That(doAfter.TryStartDoAfter(
                new DoAfterArgs(SEntMan, target, 5f, new PreeningEvent(), target, target), out doAfterId), Is.True);
            Assert.That(doAfterId, Is.Not.Null);
            doAfter.Cancel(doAfterId!.Value);
        });

        await RunSeconds(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<PreenableComponent>(target).CurrentFeathers, Is.EqualTo(3));
            Assert.That(SEntMan.Count<FeatherComponent>(), Is.Zero);
            SEntMan.GetComponent<PreenableComponent>(target).CurrentFeathers = 0;
            Assert.That(SEntMan.System<SharedDoAfterSystem>().TryStartDoAfter(
                new DoAfterArgs(SEntMan, target, 5f, new PreeningEvent(), target, target)), Is.True);
        });

        await RunSeconds(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<PreenableComponent>(target).CurrentFeathers, Is.Zero);
            Assert.That(SEntMan.Count<FeatherComponent>(), Is.Zero);
        });
    }

    [Test]
    public async Task FeatherRegrowthStopsAtMaximum()
    {
        var targetNet = await SpawnTarget("MobAvali");
        var target = ToServer(targetNet);

        await Server.WaitAssertion(() =>
        {
            var component = SEntMan.GetComponent<PreenableComponent>(target);
            Assert.That(component.CurrentFeathers, Is.EqualTo(3));
            Assert.That(component.ReplenishDelay, Is.EqualTo(TimeSpan.FromSeconds(150)));
            component.CurrentFeathers = 2;
            component.ReplenishTime = STiming.CurTime;
        });

        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var component = SEntMan.GetComponent<PreenableComponent>(target);
            Assert.That(component.CurrentFeathers, Is.EqualTo(component.MaximumFeathers));
            Assert.That(component.ReplenishTime, Is.Null);
        });
    }

    [Test]
    public async Task BruteDamageCanShedFeatherAndGrantAdrenaline()
    {
        var targetNet = await SpawnTarget("MobAvali");
        var target = ToServer(targetNet);

        await Server.WaitAssertion(() =>
        {
            SEntMan.GetComponent<PreenableComponent>(target).ShedScalingChance = 1f;
            var blunt = ProtoMan.Index<DamageTypePrototype>(BluntProto);
            SEntMan.System<DamageableSystem>().TryChangeDamage(target,
                new DamageSpecifier(blunt, FixedPoint2.New(10)), ignoreResistances: true);
        });

        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<PreenableComponent>(target).CurrentFeathers, Is.EqualTo(2));
            Assert.That(SEntMan.Count<FeatherComponent>(), Is.EqualTo(1));
            Assert.That(SEntMan.HasComponent<IgnoreSlowOnDamageComponent>(target), Is.True,
                "The feather-shedding hit did not grant the adrenaline effect.");
        });
    }
}
