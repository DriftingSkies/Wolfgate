using System.Linq;
using Content.Server._WG.Lattice;
using Content.Shared._WG.Lattice;
using Content.Shared.NPC.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Lattice;

/// <summary>The song is offered only deep in Drift and within the caps, and answering it makes someone Choir.</summary>
public sealed class WGChoirTest
{
    [Test]
    public async Task OfferAndJoin()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var choir = entities.System<WGChoirSystem>();
        var factions = entities.System<NpcFactionSystem>();

        await server.WaitAssertion(() =>
        {
            var session = players.Sessions.Single();
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(session, mob, force: true);

            config.SetCVar(WGChoirCVars.MinPlayers, 0);
            choir.Consider(session, mob, 10f);
            Assert.That(entities.HasComponent<WGChoirOfferComponent>(mob), Is.False, "the song came at low Drift.");

            config.SetCVar(WGChoirCVars.MinPlayers, 100);
            choir.Consider(session, mob, 95f);
            Assert.That(entities.HasComponent<WGChoirOfferComponent>(mob), Is.False, "the song came in a quiet round.");

            config.SetCVar(WGChoirCVars.MinPlayers, 0);
            choir.Consider(session, mob, 95f);
            var offer = entities.GetComponent<WGChoirOfferComponent>(mob);
            Assert.That(offer.Action, Is.Not.Null, "the offer has no action to answer it.");

            choir.Join(mob);
            Assert.That(entities.HasComponent<WGChoirMemberComponent>(mob), Is.True);
            Assert.That(entities.HasComponent<WGChoirOfferComponent>(mob), Is.False);
            Assert.That(factions.IsMember(mob, WGChoirSystem.Faction), Is.True);
            Assert.That(choir.Members(), Is.EqualTo(1));
            Assert.That(entities.HasComponent<UnrevivableComponent>(mob), Is.True, "a Choir member can be revived.");

            entities.RemoveComponent<WGChoirMemberComponent>(mob);
            Assert.That(factions.IsMember(mob, WGChoirSystem.Faction), Is.False, "leaving the Choir kept its faction.");
            Assert.That(entities.HasComponent<UnrevivableComponent>(mob), Is.False, "leaving the Choir kept them unrevivable.");

            config.SetCVar(WGChoirCVars.MinPlayers, 15);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnsweringIsACoinToss()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var choir = entities.System<WGChoirSystem>();

        await server.WaitAssertion(() =>
        {
            var lucky = entities.SpawnEntity("MobHuman", map.GridCoords);
            var minds = entities.System<Content.Shared.Mind.SharedMindSystem>();
            var mind = minds.CreateMind(null);
            minds.TransferTo(mind, lucky);
            entities.EnsureComponent<WGChoirOfferComponent>(lucky);
            choir.Answer(lucky, false);
            Assert.That(entities.HasComponent<WGChoirMemberComponent>(lucky), Is.True, "the good side of the toss didn't join.");
            Assert.That(entities.System<Content.Shared.Roles.SharedRoleSystem>().MindHasRole<WGChoirRoleComponent>(mind), Is.True,
                "a member has no Choir role for admins to see.");

            var unlucky = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.EnsureComponent<WGChoirOfferComponent>(unlucky);
            choir.Answer(unlucky, true);
            Assert.That(entities.HasComponent<WGChoirMemberComponent>(unlucky), Is.False, "the bad side of the toss joined anyway.");
            Assert.That(entities.System<MobStateSystem>().IsDead(unlucky), Is.True, "the bad side of the toss didn't die.");
            Assert.That(entities.HasComponent<Content.Server._WG.Remade.Conversion.WGRemadeConvertingComponent>(unlucky), Is.True,
                "the bad side of the toss isn't turning Remade.");

            // Someone who can't turn, here dosed with necrotoxin protection, joins instead of dying for nothing.
            var shielded = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.EnsureComponent<WGChoirOfferComponent>(shielded);
            entities.EnsureComponent<Content.Server._WG.Remade.Counterplay.WGNecrotoxinProtectedComponent>(shielded);
            choir.Answer(shielded, true);
            Assert.That(entities.HasComponent<WGChoirMemberComponent>(shielded), Is.True, "a body that can't turn didn't join.");
            Assert.That(entities.System<MobStateSystem>().IsDead(shielded), Is.False, "a body that can't turn died anyway.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeadMembersTurnAndTakeThePlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var choir = entities.System<WGChoirSystem>();

        EntityUid mob = default;
        await server.WaitAssertion(() =>
        {
            var session = players.Sessions.Single();
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            var minds = entities.System<Content.Shared.Mind.SharedMindSystem>();
            var mind = minds.CreateMind(session.UserId);
            minds.TransferTo(mind, mob);
            choir.Join(mob);
            entities.System<MobStateSystem>().ChangeMobState(mob, MobState.Dead);
        });

        await pair.RunSeconds(50f);
        await server.WaitAssertion(() =>
        {
            var body = players.Sessions.Single().AttachedEntity;
            Assert.That(body, Is.Not.Null);
            Assert.That(body, Is.Not.EqualTo(mob), "a dead Choir member's body never turned.");
            Assert.That(entities.HasComponent<Content.Server._WG.Remade.Conversion.WGRemadeComponent>(body!.Value), Is.True,
                "the player didn't go with the risen Remade.");
        });

        await pair.CleanReturnAsync();
    }
}
