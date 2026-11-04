#nullable enable
using System.Linq;
using Content.Server._WF.Remade.Closets;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Echoes;
using Content.Server._WF.Remade.Monolith;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage.EntitySystems;
using Content.Server.SurveillanceCamera;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Remade.WFRemadeDirectorTest;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>Closet lurkers, hiding in lockers, echoes on the radio, the stage stings and the omens.</summary>
[TestFixture]
public sealed class OmensTest
{
    private static Entity<WFRemadeDirectorComponent, WFMonolithComponent> Stone(IEntityManager entMan, EntityUid scion)
    {
        return (scion, entMan.GetComponent<WFRemadeDirectorComponent>(scion), entMan.GetComponent<WFMonolithComponent>(scion));
    }

    /// <summary>A Growth Scion whose Director plans nothing of its own, so a test can drive one omen at a time.</summary>
    private static EntityUid QuietScion(IEntityManager entMan, IGameTiming timing, Robust.Shared.Map.EntityCoordinates at)
    {
        var scion = entMan.SpawnEntity("WFMonolithScion", at);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        var far = timing.CurTime + TimeSpan.FromHours(1);
        director.BuildUp = false;
        director.PhaseEnd = far;
        director.NextAmbush = far;
        director.NextStalker = far;
        director.NextCloset = far;
        director.NextDread = far;
        director.NextBuild = far;
        entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
        return scion;
    }

    [Test]
    public async Task ARemadeWaitsInALockerAndComesOutWhenItIsOpened()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, locker = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, 0, 0));
            scion = QuietScion(entMan, timing, At(map, -18, -18));
            locker = entMan.SpawnEntity("ClosetMaintenance", At(map, 10, 0));
        });

        await pair.RunSeconds(2f);
        EntityUid lurker = default;
        await server.WaitPost(() =>
        {
            var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            director.Biomass = 500f;
            director.Focus = null;
            Assert.That(directors.TryLurkInCloset(Stone(entMan, scion), timing.CurTime), Is.True, "no Remade went into the locker.");
            lurker = director.Horde.Single(entMan.HasComponent<WFRemadeClosetLurkerComponent>);
            Assert.That(entMan.System<SharedContainerSystem>().TryGetContainingContainer(lurker, out var container) && container.Owner == locker,
                Is.True, "the lurker isn't in the locker.");
            entMan.System<SharedEntityStorageSystem>().OpenStorage(locker);
        });

        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadePlayingDeadComponent>(lurker), Is.False, "the lurker didn't come out when the locker opened.");
            Assert.That(entMan.HasComponent<WFRemadeClosetLurkerComponent>(lurker), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ACrewmanHidingQuietlyInALockerIsNotFound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid scion = default, locker = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            var crewman = SpawnCrewman(pair, At(map, 0, 0));
            locker = entMan.SpawnEntity("ClosetMaintenance", At(map, 0, 0));
            Assert.That(entMan.System<SharedEntityStorageSystem>().Insert(crewman, locker), Is.True);
            scion = QuietScion(entMan, timing, At(map, 6, 0));
        });

        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await pair.RunSeconds(3f);
        await server.WaitPost(() =>
        {
            Assert.That(director.Seeded, Is.False, "the Director woke for a crewman hiding quietly.");
            entMan.EnsureComponent<WFRemadeGiveawayComponent>(locker).Until = timing.CurTime + TimeSpan.FromSeconds(30);
        });

        await RunUntil(pair, () => director.Seeded, 5);
        await server.WaitAssertion(() => Assert.That(director.Seeded, Is.True, "a crewman who gave themselves away wasn't found."));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADeadCrewmanCallsOnTheirHeadsetAndATrapIsLaid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var echoes = entMan.System<WFRemadeEchoSystem>();
        EntityUid scion = default, corpse = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, -15, -15));
            scion = QuietScion(entMan, timing, At(map, -18, -18));
            corpse = entMan.SpawnEntity("MobHuman", At(map, 8, 8));
            var headset = entMan.SpawnEntity("ClothingHeadsetGrey", At(map, 8, 8));
            Assert.That(entMan.System<InventorySystem>().TryEquip(corpse, headset, "ears", true, true), Is.True, "the headset wouldn't go on.");
            entMan.EnsureComponent<WFRemadeStressComponent>(corpse);
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
        });

        await pair.RunSeconds(2f);
        var before = 0;
        await server.WaitPost(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadeEchoComponent>(corpse), Is.True, "the dead crewman wasn't marked to echo.");
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).Biomass = 500f;
            before = entMan.GetComponent<WFRemadeDirectorComponent>(scion).Horde.Count;
            entMan.GetComponent<WFRemadeEchoComponent>(corpse).At = timing.CurTime;
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(echoes.Sent, Is.EqualTo(1), "the headset never spoke.");
            Assert.That(entMan.HasComponent<WFRemadeEchoComponent>(corpse), Is.False, "the corpse can echo twice.");
            var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            Assert.That(director.Horde.Count(entMan.HasComponent<WFRemadePlayingDeadComponent>), Is.GreaterThan(0), "no trap lay by the body.");
            Assert.That(director.Horde.Count, Is.GreaterThan(before));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryoneHearsTheStoneClimb()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var monoliths = entMan.System<WFMonolithSystem>();
        EntityUid scion = default;
        var before = 0;

        await server.WaitPost(() =>
        {
            before = monoliths.StingsPlayed;
            monoliths.NextSting = TimeSpan.Zero;
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 0, 0));
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 20f;
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFMonolithComponent>(scion).Stage, Is.EqualTo(Content.Shared._WF.Remade.WFMonolithStage.Hymn));
            Assert.That(monoliths.StingsPlayed, Is.EqualTo(before + 1), "reaching the Hymn played no sting.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OmensKnockCrawlAndLieOnCamera()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, crewman = default, camera = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = QuietScion(entMan, timing, At(map, -18, -18));
            entMan.SpawnEntity("Airlock", At(map, 5, 0));
            // A small room round the crewman, so the crawl always finds a wall.
            for (var k = -2; k <= 2; k++)
            {
                entMan.SpawnEntity("WallSolid", At(map, k, 2));
                entMan.SpawnEntity("WallSolid", At(map, k, -2));
                if (k is > -2 and < 2)
                {
                    entMan.SpawnEntity("WallSolid", At(map, 2, k));
                    entMan.SpawnEntity("WallSolid", At(map, -2, k));
                }
            }

            camera = entMan.SpawnEntity("SurveillanceCameraGeneral", At(map, 15, 15));
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            var stone = Stone(entMan, scion);
            Assert.That(directors.Knock(stone, crewman, timing.CurTime), Is.True, "nothing knocked on the shut airlock.");
            Assert.That(directors.Crawl(crewman, timing.CurTime), Is.True, "nothing crawled along the wall.");
            entMan.System<SurveillanceCameraSystem>().AddActiveViewer(camera, crewman);
            var lied = directors.CameraLie(stone);
            entMan.System<SurveillanceCameraSystem>().RemoveActiveViewer(camera, crewman);
            Assert.That(lied, Is.True, "nothing stood in front of the watched camera.");
        });
        await pair.CleanReturnAsync();
    }
}
