#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.ShipAccess;
using Content.Server._WG.CrewBarks;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.CrewBarks;

/// <summary>
/// A barking ship's crew take the Boss's blows with thanks and never fight back, answer anyone else's as before, and
/// greet the Boss once per cooldown.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCrewBarkSystem))]
public sealed class WGCrewBarkTest
{
    private static readonly DamageSpecifier Punch = new() { DamageDict = { ["Blunt"] = 5 } };

    /// <summary>A commanded, barking test ship with one marine on it, enrolled as the roster would.</summary>
    private static (EntityUid Grid, EntityUid Crew) Ship(IEntityManager entMan, TestMapData map)
    {
        var grid = map.Grid.Owner;
        entMan.EnsureComponent<WGShipAccessComponent>(grid);
        entMan.EnsureComponent<WGShipCommandComponent>(grid);
        var barks = entMan.EnsureComponent<WGCrewBarksComponent>(grid);
        // On the test map's one tile, beside whoever the test spawns there; off it he would float off the grid.
        var crew = entMan.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Marine, map.GridCoords, "lupercal")!.Value;
        entMan.GetComponent<HTNComponent>(crew).Enabled = false;
        entMan.System<WGCrewBarkSystem>().Enroll(crew, (grid, barks));
        return (grid, crew);
    }

    [Test]
    public async Task BossPunchDrawsThanks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (grid, crew) = Ship(entMan, map);
            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(entMan, entMan.System<SharedHandsSystem>(), map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(entMan, deed, grid);

            entMan.System<DamageableSystem>().TryChangeDamage(crew, Punch, origin: owner);
            // A shove goes through upstream retaliation alone.
            var shove = new DisarmedEvent(crew, owner, 0f);
            entMan.EventBus.RaiseLocalEvent(crew, ref shove);

            var barker = entMan.GetComponent<WGCrewBarkerComponent>(crew);
            var said = barker.Said;
            var memories = entMan.GetComponent<NPCRetaliationComponent>(crew).AttackMemories;
            var hostiles = entMan.GetComponentOrNull<FactionExceptionComponent>(crew)?.Hostiles;
            var struck = entMan.GetComponent<WGCrewComponent>(crew).Struck;
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<DamageableComponent>(crew).TotalDamage.Float(), Is.GreaterThan(0f),
                    "Precondition: the punch landed.");
                Assert.That(said.GetValueOrDefault(WGCrewBark.Struck), Is.EqualTo(1),
                    "The Boss's punch draws one thank-you; the shove right after is inside its gap.");
                Assert.That(entMan.System<WGCrewBarkSystem>().Lines("marine", WGCrewBark.Struck), Does.Contain(barker.LastLine),
                    "The line is a marine's or a common thank-you.");
                Assert.That(memories.ContainsKey(owner), Is.False, "The crewman doesn't remember the Boss as an attacker.");
                Assert.That(hostiles?.Contains(owner) ?? false, Is.False, "Nor is he hostile to the Boss.");
                Assert.That(struck.ContainsKey(owner), Is.False, "Nor does he go after the Boss.");
                Assert.That(entMan.System<WGCrewSecuritySystem>().IsAuthorized(crew, owner), Is.True,
                    "The Boss is still welcome aboard.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StrangerPunchKeepsOldResponse()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (_, crew) = Ship(entMan, map);
            var (stranger, _) = ShipAccessTest.SpawnPersonWithCard(entMan, entMan.System<SharedHandsSystem>(), map, "Random Stranger", 4);

            entMan.System<DamageableSystem>().TryChangeDamage(crew, Punch, origin: stranger);

            var barker = entMan.GetComponent<WGCrewBarkerComponent>(crew);
            var said = barker.Said;
            var memories = entMan.GetComponent<NPCRetaliationComponent>(crew).AttackMemories;
            var struck = entMan.GetComponent<WGCrewComponent>(crew).Struck;
            Assert.Multiple(() =>
            {
                Assert.That(memories.ContainsKey(stranger), Is.True, "The crewman remembers a stranger who hit him.");
                Assert.That(struck.ContainsKey(stranger), Is.True, "And goes after him.");
                Assert.That(entMan.System<WGCrewSecuritySystem>().IsAuthorized(crew, stranger), Is.False,
                    "And turns him away.");
                Assert.That(said.ContainsKey(WGCrewBark.Struck), Is.False, "A stranger gets no thank-you.");
                Assert.That(entMan.System<WGCrewBarkSystem>().Lines("marine", WGCrewBark.Attacked), Does.Contain(barker.LastLine),
                    "He says something else entirely.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GreetingOncePerCooldown()
    {
        // Greetings are for players: the test's own player becomes the Boss.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid crew = default;
        WGCrewBarksComponent barks = default!;

        await server.WaitAssertion(() =>
        {
            (var grid, crew) = Ship(entMan, map);
            barks = entMan.GetComponent<WGCrewBarksComponent>(grid);
            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(entMan, entMan.System<SharedHandsSystem>(), map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(entMan, deed, grid);
            server.PlayerMan.SetAttachedEntity(pair.Player!, owner);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<Robust.Shared.Player.ActorComponent>(owner), Is.True, "Precondition: the Boss is a player.");
                Assert.That(entMan.GetComponent<TransformComponent>(crew).GridUid, Is.EqualTo(grid), "Precondition: the crewman is aboard.");
                Assert.That(entMan.GetComponent<TransformComponent>(owner).GridUid, Is.EqualTo(grid), "Precondition: the Boss is aboard.");
                Assert.That(entMan.System<WGCrewWeaponSystem>().CanSee(crew, owner), Is.True, "Precondition: the crewman sees the Boss.");
                Assert.That(entMan.System<WGCrewSecuritySystem>().IsAuthorized(crew, owner), Is.True, "Precondition: the Boss is welcome.");
                Assert.That(entMan.System<Content.Server._WG.ShipCommand.WGShipCommandSystem>()
                    .IsBoss(owner, (grid, entMan.GetComponent<WGShipCommandComponent>(grid))), Is.True, "Precondition: the owner is the Boss.");
            });
        });

        // Past the line gap, well inside the greeting's cooldown.
        await pair.RunSeconds((float) (barks.LineGap.TotalSeconds + 5));
        await server.WaitAssertion(() =>
        {
            var said = entMan.GetComponent<WGCrewBarkerComponent>(crew).Said;
            Assert.That(said.GetValueOrDefault(WGCrewBark.GreetBoss), Is.EqualTo(1), "The Boss is greeted once while the cooldown runs.");
            barks.GreetAgain = TimeSpan.FromSeconds(1);
        });

        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var said = entMan.GetComponent<WGCrewBarkerComponent>(crew).Said;
            Assert.That(said.GetValueOrDefault(WGCrewBark.GreetBoss), Is.EqualTo(2), "Once it has run out, he greets the Boss again.");
        });

        await pair.CleanReturnAsync();
    }
}
