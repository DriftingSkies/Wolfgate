using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Operations;
using Content.Shared._WG.Operations;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Operations;

/// <summary>Every PvE operation that can land in space arrives with guards, and clears once they are all killed.</summary>
[TestFixture]
public sealed class WGOperationClearTest
{
    [Test]
    public async Task KillingEveryGuardClearsTheSite()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var ops = entMan.System<WGOperationSystem>();
        var started = new Dictionary<string, EntityUid>();

        await server.WaitPost(() =>
        {
            foreach (var op in protos.EnumeratePrototypes<WGOperationPrototype>().Where(o => o.Reward > 0 && o.PlanetChance < 1f))
            {
                if (ops.Start(op) is not { } manager)
                {
                    Assert.Fail($"{op.ID}: found no site in space.");
                    continue;
                }
                entMan.GetComponent<WGActiveOperationComponent>(manager).ArrivesAt = timing.CurTime;
                started[op.ID] = manager;
            }
        });

        // Past the window in which a site keeps picking up guards its spawners make late.
        await pair.RunSeconds(35f);

        var notes = new List<string>();
        await server.WaitPost(() =>
        {
            var damageable = entMan.System<DamageableSystem>();
            var mobState = entMan.System<MobStateSystem>();
            var lethal = new DamageSpecifier(protos.Index<DamageTypePrototype>("Blunt"), 100000);
            foreach (var (id, manager) in started)
            {
                if (!entMan.TryGetComponent<WGActiveOperationComponent>(manager, out var active))
                {
                    notes.Add($"{id}: ended before it could be cleared.");
                    continue;
                }
                if (active.Grid == null)
                    notes.Add($"{id}: never arrived.");
                if (active.Paid)
                    notes.Add($"{id}: paid out with nobody there.");
                if (active.Npcs.Count == 0)
                    notes.Add($"{id}: has no guards, so it can't be cleared.");
                foreach (var npc in active.Npcs.Where(entMan.EntityExists))
                {
                    if (entMan.GetComponent<TransformComponent>(npc).Anchored)
                        notes.Add($"{id}: counts the anchored {entMan.ToPrettyString(npc)} as a guard.");
                }

                foreach (var npc in active.Npcs.Where(entMan.EntityExists).ToList())
                {
                    damageable.TryChangeDamage(npc, lethal, ignoreResistances: true);
                    if (entMan.EntityExists(npc) && !mobState.IsIncapacitated(npc))
                        notes.Add($"{id}: guard {entMan.ToPrettyString(npc)} survives a lethal hit.");
                }
            }
        });

        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            foreach (var (id, manager) in started)
            {
                if (entMan.TryGetComponent<WGActiveOperationComponent>(manager, out var active) && !active.Paid)
                {
                    var left = string.Join(", ", active.Npcs.Select(n => entMan.EntityExists(n) ? entMan.ToPrettyString(n).ToString() : $"{n} (deleted)"));
                    notes.Add($"{id}: not cleared with every guard down; tracked: {left}");
                }
            }
            Assert.That(notes, Is.Empty, string.Join("\n", notes));
        });

        await pair.CleanReturnAsync();
    }
}
