using System.Linq;
using Content.Server._WG.Warheads;
using Content.Server.Wires;
using Content.Shared._WG.Warheads;
using Content.Shared.Wires;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Warheads;

/// <summary>
/// Every warhead has a panel over two fuze wires and no multitool disarm: one wire makes it safe, even in the final
/// seconds, the other sets it off, and neither does anything while it is safe.
/// </summary>
public sealed class WGWarheadWiresTest
{
    private static readonly string[] Chassis =
    {
        "WGWarheadTactical", "WGWarheadField", "WGWarheadStrategic", "WGWarheadScuttleTerminal", "WGWarheadTacticalKeyed",
    };

    /// <summary>Every effect but the blast, so the test map survives.</summary>
    private static WGWarheadPayload QuietPayload()
    {
        return new WGWarheadPayload
        {
            PromptRadiation = 0f,
            FalloutCount = 0,
            EmpRange = 0f,
            DetonationSound = null,
        };
    }

    [Test]
    [TestCaseSource(nameof(Chassis))]
    public async Task OneWireDisarmsTheOtherDetonates(string chassis)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var warheads = entMan.System<WGWarheadSystem>();

        EntityUid good = default, bad = default, safe = default;
        await server.WaitPost(() =>
        {
            var user = entMan.SpawnEntity(null, map.GridCoords);

            good = entMan.SpawnEntity(chassis, map.GridCoords);
            Assert.That(entMan.HasComponent<WiresPanelComponent>(good), Is.True, "the warhead has no panel to unscrew.");
            Assert.That(entMan.GetComponent<WGWarheadComponent>(good).DisarmQuality, Is.Null,
                "a multitool still disarms the warhead around its wires.");

            // The right wire stops it even in the final seconds.
            var goodComp = entMan.GetComponent<WGWarheadComponent>(good);
            warheads.StartCountdown((good, goodComp), null, 5f);
            goodComp.State = WGWarheadState.Final;
            Cut<WGWarheadDisarmWireAction>(entMan, good, user);
            Assert.That(goodComp.State, Is.EqualTo(WGWarheadState.Safe), "the disarm wire didn't make it safe.");

            bad = entMan.SpawnEntity(chassis, map.GridCoords);
            var badComp = entMan.GetComponent<WGWarheadComponent>(bad);
            badComp.CustomPayload = QuietPayload();
            warheads.StartCountdown((bad, badComp), null);
            Cut<WGWarheadDetonateWireAction>(entMan, bad, user);

            // Cutting while safe does nothing at all.
            safe = entMan.SpawnEntity(chassis, map.GridCoords);
            entMan.GetComponent<WGWarheadComponent>(safe).CustomPayload = QuietPayload();
            Cut<WGWarheadDetonateWireAction>(entMan, safe, user);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(bad), Is.True, "the detonate wire didn't set it off.");
            Assert.That(entMan.EntityExists(safe), Is.True, "a safe warhead went off when its wire was cut.");
            Assert.That(entMan.GetComponent<WGWarheadComponent>(good).State, Is.EqualTo(WGWarheadState.Safe));
        });

        await pair.CleanReturnAsync();
    }

    private static void Cut<T>(IEntityManager entMan, EntityUid warhead, EntityUid user) where T : IWireAction
    {
        var wires = entMan.GetComponent<WiresComponent>(warhead).WiresList;
        Assert.That(wires, Has.Count.EqualTo(2), "the warhead's panel doesn't hold exactly two wires.");
        var wire = wires.Single(w => w.Action is T);
        wire.Action!.Cut(user, wire);
    }
}
