using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Operations;
using Content.Shared._WG.Operations;
using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Operations;

/// <summary>
/// A site with a guard faction puts everything it brings into that faction, the turrets its map places included.
/// Loading a map clears CreationTick on mapped entities, so a filter on that tick once left the vault's turrets hostile,
/// and they shot the guards whenever one stood in their sight.
/// </summary>
[TestFixture]
public sealed class WGOperationSiteFactionTest
{
    [Test]
    public async Task EverythingOnASiteJoinsItsGuardFaction()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var ops = entMan.System<WGOperationSystem>();
        var started = new Dictionary<WGOperationPrototype, EntityUid>();

        await server.WaitPost(() =>
        {
            foreach (var op in protos.EnumeratePrototypes<WGOperationPrototype>()
                         .Where(o => o.Reward > 0 && o.GuardFaction != null && o.PlanetChance < 1f))
            {
                if (ops.Start(op) is not { } manager)
                {
                    Assert.Fail($"{op.ID}: found no site.");
                    continue;
                }
                entMan.GetComponent<WGActiveOperationComponent>(manager).ArrivesAt = timing.CurTime;
                started[op] = manager;
            }
        });

        // Sites arrive on the operation system's next one-second check.
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(started, Is.Not.Empty, "No operation with a guard faction started.");
            var siteGrid = entMan.System<WGSiteGridSystem>();
            Assert.Multiple(() =>
            {
                foreach (var (op, manager) in started)
                {
                    var active = entMan.GetComponent<WGActiveOperationComponent>(manager);
                    Assert.That(active.Grid, Is.Not.Null, $"{op.ID}: never arrived.");
                    if (active.Grid is not { } grid)
                        continue;

                    foreach (var child in siteGrid.Children(grid))
                    {
                        if (!entMan.TryGetComponent<NpcFactionMemberComponent>(child, out var member)
                            || entMan.HasComponent<ActorComponent>(child))
                            continue;

                        Assert.That(member.Factions.Select(f => f.Id), Is.EquivalentTo(new[] { op.GuardFaction!.Value.Id }),
                            $"{op.ID}: {entMan.ToPrettyString(child)} is not in the guard faction.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
