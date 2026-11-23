using System.Linq;
using Content.Server._Mono.Traits.Physical;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Wolfmed;

/// <summary>Nanite Repair Drones close a chassis breach; they only mended flesh, so on a synth they did nothing.</summary>
[TestFixture]
[TestOf(typeof(PlateletFactoriesSystem))]
public sealed class WolfmedNaniteDronesTest
{
    private static readonly ProtoId<TraitPrototype> NaniteRepairDronesProto = "NaniteRepairDrones";

    [Test]
    public async Task NaniteDronesCloseAChassisBreachTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid torso = default;
        EntityUid breach = default;

        await server.WaitAssertion(() =>
        {
            // Vacuum damage outpaces the drones' 0.3 a second and reopens the breach.
            em.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            var body = em.SpawnEntity("MobIPC", map.GridCoords);
            torso = em.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            breach = em.System<WoundSystem>().CreateOrMergeWound(torso, "WGWolfmedBreachWound", 5)!.Value;
            em.AddComponents(body, server.ProtoMan.Index<TraitPrototype>(NaniteRepairDronesProto).Components, false);
        });

        // The drones mend 0.3 severity a second: a severity-5 breach closes in under 20 s.
        await server.WaitRunTicks(server.Timing.TickRate * 30);

        await server.WaitAssertion(() =>
            Assert.That(em.System<WoundSystem>().GetWounds(torso).Any(open => open.Owner == breach), Is.False,
                "the drones left the breach open."));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A chassis with only Heat damage gets the whole Burn budget, 0.15 a second. Split over every Burn type it got
    /// 0.03 a second, and nothing in crit.
    /// </summary>
    [Test]
    public async Task NaniteDronesHealHeatOnlyDamageTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid body = default;
        var dealt = FixedPoint2.New(3);

        await server.WaitAssertion(() =>
        {
            em.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            body = em.SpawnEntity("MobIPC", map.GridCoords);
            var heat = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Heat"), dealt);
            em.System<DamageableSystem>().TryChangeDamage(body, heat, true);
            Assert.That(em.GetComponent<DamageableComponent>(body).Damage.DamageDict["Heat"], Is.EqualTo(dealt));
            em.AddComponents(body, server.ProtoMan.Index<TraitPrototype>(NaniteRepairDronesProto).Components, false);
        });

        await server.WaitRunTicks(server.Timing.TickRate * 10);

        await server.WaitAssertion(() =>
        {
            em.GetComponent<DamageableComponent>(body).Damage.DamageDict.TryGetValue("Heat", out var heat);
            Assert.That(dealt - heat, Is.GreaterThanOrEqualTo(FixedPoint2.New(1)),
                $"10 s of drones healed only {dealt - heat} Heat.");
        });

        await pair.CleanReturnAsync();
    }
}
