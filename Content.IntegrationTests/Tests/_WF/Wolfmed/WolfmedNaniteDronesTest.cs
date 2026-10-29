using System.Linq;
using Content.Server._Mono.Traits.Physical;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>Nanite Repair Drones close a chassis breach; they only mended flesh, so on a synth they did nothing.</summary>
[TestFixture]
[TestOf(typeof(PlateletFactoriesSystem))]
public sealed class WolfmedNaniteDronesTest
{
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
            var body = em.SpawnEntity("MobIPC", map.GridCoords);
            torso = em.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            breach = em.System<WoundSystem>().CreateOrMergeWound(torso, "WFWolfmedBreachWound", 10)!.Value;
            em.AddComponents(body, server.ProtoMan.Index<TraitPrototype>("NaniteRepairDrones").Components, false);
        });

        await server.WaitRunTicks(server.Timing.TickRate * 12);

        await server.WaitAssertion(() =>
            Assert.That(em.System<WoundSystem>().GetWounds(torso).Any(open => open.Owner == breach), Is.False,
                "the drones left the breach open."));

        await pair.CleanReturnAsync();
    }
}
