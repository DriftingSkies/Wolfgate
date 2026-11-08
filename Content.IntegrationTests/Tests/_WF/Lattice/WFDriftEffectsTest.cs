using System;
using System.Linq;
using Content.Server._WF.Lattice;
using Content.Server._WF.Remade.Conversion;
using Content.Shared._WF.Lattice;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>Every hallucination can be shown, and the Deep takes a ship's small animal and turns it.</summary>
public sealed class WFDriftEffectsTest
{
    [Test]
    public async Task EveryHallucinationShowsAndAnAnimalIsTaken()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var effects = entities.System<WFDriftEffectsSystem>();
        var mobs = entities.System<MobStateSystem>();

        EntityUid mouse = default;
        await server.WaitAssertion(() =>
        {
            var session = players.Sessions.Single();
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(session, mob, force: true);

            foreach (var kind in Enum.GetValues<WFLatticeHallucinationKind>())
            {
                Assert.DoesNotThrow(() => effects.Show(session, mob, kind), kind.ToString());
            }

            mouse = entities.SpawnEntity("MobMouse", map.GridCoords);
            Assert.That(effects.TryTake(map.Grid, 0.01f), Is.False, "a mouse counted as small under a 10 g limit.");
            Assert.That(effects.TryTake(map.Grid, 30f), Is.True);
            Assert.That(mobs.IsDead(mouse), Is.True);
            Assert.That(entities.HasComponent<WFRemadeConvertingComponent>(mouse), Is.True, "the mouse didn't start turning.");
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }
}
