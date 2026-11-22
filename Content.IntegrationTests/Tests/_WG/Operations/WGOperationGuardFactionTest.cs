using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.Operations;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Operations;

/// <summary>Every PvE operation's guards are hostile to the faction that called it, so the caller can clear the site.</summary>
[TestFixture]
public sealed class WGOperationGuardFactionTest
{
    /// <summary>The NPC faction each caller's players carry.</summary>
    private static readonly Dictionary<string, string> PlayerFaction = new()
    {
        ["TSF"] = "TSFMC",
        ["PDV"] = "PirateNF",
    };

    [Test]
    public async Task GuardsAreHostileToTheCaller()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var factions = server.System<NpcFactionSystem>();

        await server.WaitAssertion(() =>
        {
            foreach (var op in protos.EnumeratePrototypes<WGOperationPrototype>().Where(o => o.Reward > 0))
            {
                Assert.That(PlayerFaction.TryGetValue(op.Company.Id, out var caller), $"{op.ID}: unknown caller {op.Company}");

                foreach (var npc in op.Npcs)
                {
                    var guard = op.GuardFaction is { } joined
                        ? new List<string> { joined.Id }
                        : protos.Index(npc.Proto).TryGetComponent<NpcFactionMemberComponent>(out var member, factory)
                            ? member.Factions.Select(f => f.Id).ToList()
                            : new List<string>();
                    Assert.That(guard, Is.Not.Empty, $"{op.ID}: {npc.Proto} has no faction");
                    Assert.That(guard.Any(f => factions.IsFactionHostile(f, caller!)),
                        $"{op.ID}: {npc.Proto} ({string.Join(", ", guard)}) isn't hostile to {caller}");
                }
            }

            // The Dug-in Garrison's vault is mapped with PDV guards; only the guard faction makes them fight a PDV caller.
            var garrison = protos.Index<WGOperationPrototype>("WGOperationGarrisonPdv");
            Assert.That(garrison.GuardFaction, Is.Not.Null);
            Assert.That(factions.IsFactionHostile(garrison.GuardFaction!.Value.Id, "PirateNF"));
        });

        await pair.CleanReturnAsync();
    }
}
