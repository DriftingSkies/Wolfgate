using System.Linq;
using Content.Server._WF.Lattice;
using Content.Shared._WF.Lattice;
using Content.Shared.NPC.Systems;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>The song is offered only deep in Drift and within the caps, and answering it makes someone Choir.</summary>
public sealed class WFChoirTest
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
        var choir = entities.System<WFChoirSystem>();
        var factions = entities.System<NpcFactionSystem>();

        await server.WaitAssertion(() =>
        {
            var session = players.Sessions.Single();
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(session, mob, force: true);

            config.SetCVar(WFChoirCVars.MinPlayers, 0);
            choir.Consider(session, mob, 10f);
            Assert.That(entities.HasComponent<WFChoirOfferComponent>(mob), Is.False, "the song came at low Drift.");

            config.SetCVar(WFChoirCVars.MinPlayers, 100);
            choir.Consider(session, mob, 95f);
            Assert.That(entities.HasComponent<WFChoirOfferComponent>(mob), Is.False, "the song came in a quiet round.");

            config.SetCVar(WFChoirCVars.MinPlayers, 0);
            choir.Consider(session, mob, 95f);
            var offer = entities.GetComponent<WFChoirOfferComponent>(mob);
            Assert.That(offer.Action, Is.Not.Null, "the offer has no action to answer it.");

            choir.Join(mob);
            Assert.That(entities.HasComponent<WFChoirMemberComponent>(mob), Is.True);
            Assert.That(entities.HasComponent<WFChoirOfferComponent>(mob), Is.False);
            Assert.That(factions.IsMember(mob, WFChoirSystem.Faction), Is.True);
            Assert.That(choir.Members(), Is.EqualTo(1));

            entities.RemoveComponent<WFChoirMemberComponent>(mob);
            Assert.That(factions.IsMember(mob, WFChoirSystem.Faction), Is.False, "leaving the Choir kept its faction.");

            config.SetCVar(WFChoirCVars.MinPlayers, 15);
        });

        await pair.CleanReturnAsync();
    }
}
