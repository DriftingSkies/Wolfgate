#nullable enable
#pragma warning disable RA0002 // The test sets the director's book by hand.
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.OutpostRaids;
using Content.Server.Power.Components;
using Content.Shared._WF.OutpostRaids;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Outposts.OutpostFoundingTest;
using static Content.IntegrationTests.Tests._WF.OutpostRaids.RaidArrivalTest;

namespace Content.IntegrationTests.Tests._WF.OutpostRaids;

/// <summary>F10.3: a step 5 raid shuttle comes down from orbit on a defended outpost, under fire from its batteries.</summary>
[TestFixture]
[TestOf(typeof(WFOrbitalDefenceSystem))]
public sealed class OrbitalDefenceTest
{
    /// <summary>An uplink and two batteries bring the shuttle down before it lands; the raid is beaten and lands nobody.</summary>
    [Test]
    public async Task BatteriesShootTheShuttleDown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await Configure(server);
        await server.WaitPost(() => server.CfgMan.SetCVar(OutpostRaidCVars.SalvoInterval, 0.5f));
        var (layers, user) = await Setup(pair);
        var director = server.System<WFRaidDirectorSystem>();
        EntityUid outpost = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                outpost = Found(server, layers[0], user);
                Defend(server, outpost, "WFOutpostSensorUplink", "WFOutpostDefenceBattery", "WFOutpostFlakBattery");
                Pick(entMan, director, outpost);
            });

            await server.WaitRunTicks(pair.SecondsToTicks(4));
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<WFOrbitalDefenceSystem>().Descents, Is.Empty, "The descent is still going.");
                Assert.That(Shuttles(entMan), Is.Empty, "A shot-down shuttle landed.");
                Assert.That(director.ActiveRaids.Values.SelectMany(raid => raid.Mobs), Is.Empty, "A shot-down shuttle's crew walked in.");
            });

            await server.WaitRunTicks(pair.SecondsToTicks(6));
            await server.WaitAssertion(() =>
            {
                Assert.That(director.IsRaided(outpost), Is.False, "The raid is still on the books.");
                Assert.That(entMan.GetComponent<WFRaidAttentionComponent>(outpost).Step, Is.GreaterThan(5), "Shooting it down didn't count as beating it.");
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(OutpostRaidCVars.SalvoInterval, OutpostRaidCVars.SalvoInterval.DefaultValue));
            await LeaveAndTeardown(pair, user, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>An uplink with no battery only watches: the shuttle spends its descent coming down, then lands.</summary>
    [Test]
    public async Task UndefendedDescentLands()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await Configure(server);
        await server.WaitPost(() => server.CfgMan.SetCVar(OutpostRaidCVars.SalvoInterval, 2f));
        var (layers, user) = await Setup(pair);
        var director = server.System<WFRaidDirectorSystem>();
        EntityUid outpost = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                outpost = Found(server, layers[0], user);
                Defend(server, outpost, "WFOutpostSensorUplink");
                Pick(entMan, director, outpost);
            });

            await server.WaitRunTicks(pair.SecondsToTicks(2));
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<WFOrbitalDefenceSystem>().Descents, Has.Count.EqualTo(1), "The shuttle isn't descending.");
                Assert.That(director.IsRaided(outpost), "A descending raid isn't a raid.");
                Assert.That(Shuttles(entMan), Is.Empty, "It landed before its descent was over.");
            });

            await server.WaitRunTicks(pair.SecondsToTicks(7));
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<WFOrbitalDefenceSystem>().Descents, Is.Empty);
                Assert.That(Shuttles(entMan), Has.Count.EqualTo(1), "The shuttle didn't land after its descent.");
                Assert.That(director.ActiveRaids.Values.SelectMany(raid => raid.Mobs), Is.Not.Empty, "Its crew didn't walk in.");
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(OutpostRaidCVars.SalvoInterval, OutpostRaidCVars.SalvoInterval.DefaultValue));
            await LeaveAndTeardown(pair, user, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Puts the defence gizmos, powered, on the outpost's corner tiles.</summary>
    private static void Defend(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid outpost, params string[] ids)
    {
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var grid = entMan.GetComponent<MapGridComponent>(outpost);
        var tiles = new[] { new Vector2i(0, 0), new Vector2i(2, 2), new Vector2i(0, 2), new Vector2i(2, 0) };
        for (var i = 0; i < ids.Length; i++)
        {
            var uid = entMan.SpawnEntity(ids[i], maps.GridTileToLocal(outpost, grid, tiles[i]));
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(uid, false);
            entMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
        }
    }

    /// <summary>Sends a step 5 raid, the one that comes by shuttle.</summary>
    private static void Pick(IEntityManager entMan, WFRaidDirectorSystem director, EntityUid outpost)
    {
        var attention = entMan.EnsureComponent<WFRaidAttentionComponent>(outpost);
        attention.Started = true;
        attention.Attention = 100;
        attention.Step = 5;
        Assert.That(director.TryPickRaid(Book(entMan, outpost)), Is.True, "The director sent no raid.");
    }

    private static List<EntityUid> Shuttles(IEntityManager entMan)
    {
        return entMan.EntityQuery<WFRaidShuttleComponent>().Select(comp => comp.Owner).ToList();
    }
}
