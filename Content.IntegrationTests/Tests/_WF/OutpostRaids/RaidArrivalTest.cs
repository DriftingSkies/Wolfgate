#nullable enable
#pragma warning disable RA0002 // The test sets the director's book and the burrow's clock by hand.
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Fauna;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server.Cargo.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.OutpostRaids;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.NPC.Components;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting;
using static Content.IntegrationTests.Tests._WF.Outposts.OutpostFoundingTest;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.OutpostRaids;

/// <summary>Raids that bring something with them: a burrow that keeps spawning, and a shuttle set down beyond the claim.</summary>
[TestFixture]
[TestOf(typeof(WFRaidDirectorSystem))]
public sealed class RaidArrivalTest
{
    [Test]
    public async Task ABurrowKeepsSpawningUntilDestroyed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await Configure(server);
        var (layers, user) = await Setup(pair);
        var ground = layers[0];
        var director = server.System<WFRaidDirectorSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid outpost = default, burrow = default;
        var raidId = 0;
        var first = new List<EntityUid>();

        try
        {
            await server.WaitAssertion(() =>
            {
                outpost = Found(server, ground, user);
                var attention = entMan.EnsureComponent<WFRaidAttentionComponent>(outpost);
                attention.Started = true;
                attention.Attention = 30;
                attention.Step = 2;
                Assert.That(director.TryPickRaid(Book(entMan, outpost)), Is.True, "The director sent no raid.");
                Assert.That(attention.Attention, Is.EqualTo(12).Within(0.01), "A step 2 raid should spend 18 threat points.");
            });

            await server.WaitRunTicks(pair.SecondsToTicks(2));
            await server.WaitAssertion(() =>
            {
                Assert.That(director.ActiveRaids, Has.Count.EqualTo(1), "The raid found nowhere to walk in.");
                var raid = director.ActiveRaids.Values.First();
                raidId = raid.Id;
                Assert.That(raid.Step, Is.EqualTo(2));
                Assert.That(raid.Burrows, Has.Count.EqualTo(1), "A step 2 raid opens one burrow.");
                burrow = raid.Burrows[0];
                Assert.That(raid.Mobs, Has.Count.InRange(1, 2), "The burrow comes with one or two of the world's wildlife.");
                Assert.That(raid.Mobs.Any(m => entMan.HasComponent<WFRaidBurrowComponent>(m)), Is.False, "The burrow was counted as a mob.");
                first.AddRange(raid.Mobs);

                // How often it spawns is the burrow's balance, not this test's.
                var hole = entMan.GetComponent<FaunaBurrowComponent>(burrow);
                hole.Interval = TimeSpan.FromSeconds(1);
                hole.NextSpawn = timing.CurTime;
            });

            // The director takes in what the burrow spawned on its next pass, every five seconds.
            await server.WaitRunTicks(pair.SecondsToTicks(4));
            var spawned = new List<EntityUid>();
            await server.WaitPost(() =>
            {
                var hole = entMan.GetComponent<FaunaBurrowComponent>(burrow);
                spawned.AddRange(hole.Spawned.Where(e => !entMan.Deleted(e)));
                hole.NextSpawn = TimeSpan.MaxValue;
            });
            await server.WaitRunTicks(pair.SecondsToTicks(6));
            await server.WaitAssertion(() =>
            {
                var raid = director.ActiveRaids[raidId];
                entMan.GetComponent<FaunaBurrowComponent>(burrow).NextSpawn = timing.CurTime;
                Assert.That(spawned, Has.Count.GreaterThanOrEqualTo(2), "The burrow didn't keep spawning.");
                foreach (var mob in spawned)
                {
                    Assert.That(entMan.TryGetComponent<WFRaidMobComponent>(mob, out var raider) && raider.Raid == raidId, Is.True,
                        "A mob from the burrow didn't join the raid.");
                    Assert.That(raid.Mobs, Does.Contain(mob));
                }

                // Clearing what is out doesn't end the raid while the burrow stands.
                foreach (var mob in raid.Mobs.ToList())
                    entMan.DeleteEntity(mob);
            });

            await server.WaitRunTicks(pair.SecondsToTicks(6));
            await server.WaitAssertion(() =>
            {
                Assert.That(director.ActiveRaids.ContainsKey(raidId), Is.True, "The raid ended with its burrow still open.");
                Assert.That(director.ActiveRaids[raidId].Mobs, Is.Not.Empty, "The burrow stopped spawning once its mobs were gone.");

                var blunt = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Blunt");
                server.System<DamageableSystem>().TryChangeDamage(burrow, new DamageSpecifier(blunt, FixedPoint2.New(300)), ignoreResistances: true);
            });

            await server.WaitRunTicks(pair.SecondsToTicks(1));
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.Deleted(burrow), Is.True, "Enough damage didn't destroy the burrow.");
                foreach (var mob in director.ActiveRaids[raidId].Mobs.ToList())
                    entMan.DeleteEntity(mob);
            });

            await server.WaitRunTicks(pair.SecondsToTicks(6));
            await server.WaitAssertion(() =>
            {
                Assert.That(director.ActiveRaids, Is.Empty, "The raid went on after its burrow was destroyed and its mobs cleared.");
                Assert.That(entMan.GetComponent<WFRaidAttentionComponent>(outpost).Step, Is.EqualTo(3), "Beating a step 2 raid should open step 3.");
            });
        }
        finally
        {
            await LeaveAndTeardown(pair, user, layers);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARaidShuttleLandsOutsideTheClaimAndLeavesAtDawn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await Configure(server);
        var (layers, user) = await Setup(pair);
        var ground = layers[0];
        var director = server.System<WFRaidDirectorSystem>();
        var outposts = server.System<WFOutpostSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();
        EntityUid outpost = default, shuttle = default;
        var landedAt = System.Numerics.Vector2.Zero;

        try
        {
            await server.WaitAssertion(() =>
            {
                outpost = Found(server, ground, user);
                var attention = entMan.EnsureComponent<WFRaidAttentionComponent>(outpost);
                attention.Started = true;
                attention.Attention = 100;
                attention.Step = 5;
                Assert.That(director.TryPickRaid(Book(entMan, outpost)), Is.True, "The director sent no raid.");
                Assert.That(attention.Attention, Is.EqualTo(10).Within(0.01), "A step 5 raid should spend 90 threat points.");
            });

            await server.WaitRunTicks(pair.SecondsToTicks(2));
            await server.WaitAssertion(() =>
            {
                Assert.That(director.ActiveRaids, Has.Count.EqualTo(1), "The raid found nowhere to walk in.");
                var raid = director.ActiveRaids.Values.First();
                Assert.That(raid.Step, Is.EqualTo(5));
                Assert.That(raid.Mobs, Has.Count.InRange(4, 5), "A shuttle raid is a breacher, two raiders and one or two gunners.");
                var landed = raid.Props.Where(p => entMan.HasComponent<WFRaidShuttleComponent>(p)).ToList();
                Assert.That(landed, Has.Count.EqualTo(1), "The raid set no shuttle down.");
                shuttle = landed[0];
                Assert.That(entMan.GetComponent<WFRaidShuttleComponent>(shuttle).Raid, Is.EqualTo(raid.Id));
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(shuttle), Is.True, "The shuttle isn't held down.");
                landedAt = xforms.GetWorldPosition(shuttle);

                var (min, max) = outposts.GetClaimZone(Outpost(entMan, outpost));
                var groundGrid = entMan.GetComponent<MapGridComponent>(ground);
                var shuttleGrid = entMan.GetComponent<MapGridComponent>(shuttle);
                var tiles = maps.GetAllTilesEnumerator(shuttle, shuttleGrid);
                var count = 0;
                while (tiles.MoveNext(out var tile))
                {
                    count++;
                    var at = maps.GridTileToWorld(shuttle, shuttleGrid, tile.Value.GridIndices);
                    var index = maps.TileIndicesFor(ground, groundGrid, at);
                    Assert.That(index.X >= min.X && index.X <= max.X && index.Y >= min.Y && index.Y <= max.Y, Is.False,
                        $"The shuttle's tile {tile.Value.GridIndices} landed inside the claim at {index}.");
                }

                Assert.That(count, Is.GreaterThan(0), "The shuttle has no tiles.");

                var children = new List<EntityUid>();
                var enumerator = entMan.GetComponent<TransformComponent>(shuttle).ChildEnumerator;
                while (enumerator.MoveNext(out var child))
                    children.Add(child);
                Assert.That(children, Is.Not.Empty, "The shuttle is empty.");
                Assert.That(children.All(c => entMan.HasComponent<WFRaidLootComponent>(c) && entMan.HasComponent<CargoSellBlacklistComponent>(c)),
                    Is.True, "Something on the raid shuttle isn't raid loot.");
            });

            // The client takes in the shuttle and its decals before it goes; a client left behind would get them after.
            await pair.RunTicksSync(pair.SecondsToTicks(3));
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.Deleted(shuttle), Is.False);
                Assert.That((xforms.GetWorldPosition(shuttle) - landedAt).Length(), Is.LessThan(0.01f), "The shuttle moved after landing.");
                var world = entMan.GetComponent<CEZMapComponent>(ground).NetworkUid;
                Assert.That(server.System<WFPlanetWeatherSystem>().SetMinuteOfDay(world, 400), Is.True);
            });

            await server.WaitRunTicks(pair.SecondsToTicks(6));
            await server.WaitAssertion(() =>
            {
                Assert.That(director.ActiveRaids, Is.Empty, "The raid is still on the books after dawn.");
                Assert.That(entMan.Deleted(shuttle), Is.True, "The raid shuttle was still down after dawn.");
                Assert.That(entMan.GetComponent<WFRaidAttentionComponent>(outpost).Step, Is.EqualTo(5), "A raid that left at dawn was counted as beaten.");
            });
        }
        finally
        {
            await LeaveAndTeardown(pair, user, layers);
        }

        await pair.CleanReturnAsync();
    }

    internal static async Task Configure(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        await server.WaitPost(() =>
        {
            // With PVS off, wildlife's owner-only alerts log as empty states over a long run.
            server.CfgMan.SetCVar(CVars.NetPVS, true);
            server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, false);
            server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, true);
            server.CfgMan.SetCVar(OutpostRaidCVars.Grace, 0f);
            server.CfgMan.SetCVar(OutpostRaidCVars.Lead, 1f);
            server.CfgMan.SetCVar(OutpostRaidCVars.SpawnMin, 20);
            server.CfgMan.SetCVar(OutpostRaidCVars.SpawnMax, 24);
            server.CfgMan.SetCVar(OutpostRaidCVars.PlayerClearance, 12);
        });
    }

    /// <summary>Founds the outpost at night, with the founder out of every raider's sights.</summary>
    internal static EntityUid Found(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid ground, EntityUid user)
    {
        var entMan = server.EntMan;
        var world = entMan.GetComponent<CEZMapComponent>(ground).NetworkUid;
        Assert.That(server.System<WFPlanetWeatherSystem>().SetMinuteOfDay(world, 1100), Is.True);
        Assert.That(server.System<WFOutpostSystem>().TryFound(user, SpawnKit(entMan, ground), new EntityCoordinates(ground, Site.X, Site.Y), out var founded, out var reason),
            Is.True, $"Founding was refused: {reason}.");
        entMan.RemoveComponent<NpcFactionMemberComponent>(user);
        return founded!.Value;
    }

    private static Entity<WFOutpostComponent, MapGridComponent> Outpost(IEntityManager entMan, EntityUid outpost)
    {
        return (outpost, entMan.GetComponent<WFOutpostComponent>(outpost), entMan.GetComponent<MapGridComponent>(outpost));
    }

    internal static Entity<WFOutpostComponent, MapGridComponent, WFRaidAttentionComponent> Book(IEntityManager entMan, EntityUid outpost)
    {
        return (outpost, entMan.GetComponent<WFOutpostComponent>(outpost), entMan.GetComponent<MapGridComponent>(outpost),
            entMan.GetComponent<WFRaidAttentionComponent>(outpost));
    }

    /// <summary>
    /// Moves the player off the planet first: raid mobs leave footprints, and a client still there is sent decals of
    /// grids the teardown has already deleted.
    /// </summary>
    internal static async Task LeaveAndTeardown(TestPair pair, EntityUid user, List<EntityUid> layers)
    {
        var away = await pair.CreateTestMap();
        await pair.Server.WaitPost(() => pair.Server.System<SharedTransformSystem>().SetCoordinates(user, away.GridCoords));
        await pair.RunTicksSync(30);
        await Teardown(pair, layers);
    }
}
