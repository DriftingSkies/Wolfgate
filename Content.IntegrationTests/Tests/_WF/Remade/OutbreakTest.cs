#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Dread;
using Content.Server._WF.Remade.Gifts;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Sector;
using Content.Shared._WF.Remade;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Remade.WFRemadeDirectorTest;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>A Crescendo stone breaking out on other ships, a splinter's outbreak kept to its own ship, and wiping it all.</summary>
[TestFixture]
public sealed class OutbreakTest
{
    /// <summary>A plated ship far off on the same map, with a vent out of the crewman's sight.</summary>
    private static Entity<MapGridComponent> OtherShip(IEntityManager entMan, ITileDefinitionManager tiles, MapId map)
    {
        var maps = entMan.System<SharedMapSystem>();
        var ship = maps.CreateGridEntity(map);
        entMan.System<SharedTransformSystem>().SetLocalPosition(ship.Owner, new Vector2(300f, 0f));
        var plating = new Tile(tiles["Plating"].TileId);
        for (var x = -12; x <= 12; x++)
        for (var y = -12; y <= 12; y++)
            maps.SetTile(ship.Owner, ship.Comp, new Vector2i(x, y), plating);

        // A wall between the crewman and the vent, so the vent is out of sight.
        for (var y = -3; y <= 3; y++)
            entMan.SpawnEntity("WallSolid", new EntityCoordinates(ship.Owner, new Vector2(4.5f, y + 0.5f)));

        entMan.SpawnEntity("GasVentPump", new EntityCoordinates(ship.Owner, new Vector2(9.5f, 0.5f)));
        return ship;
    }

    [Test]
    public async Task ACrescendoStoneBreaksOutOnAnotherCrewedShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid scion = default;
        Entity<MapGridComponent> ship = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            ship = OtherShip(entMan, server.ResolveDependency<ITileDefinitionManager>(), map.MapId);
            SpawnCrewman(pair, new EntityCoordinates(ship.Owner, new Vector2(0.5f, 0.5f)));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 0, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 100f;
        });

        await pair.RunSeconds(3f);
        await server.WaitPost(() =>
        {
            var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            Assert.That(entMan.GetComponent<WFMonolithComponent>(scion).Stage, Is.EqualTo(WFMonolithStage.Crescendo));
            Assert.That(director.NextOutbreak, Is.GreaterThan(TimeSpan.Zero), "the Crescendo planned no outbreak.");
            director.NextOutbreak = timing.CurTime;
        });

        var directorComp = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await RunUntil(pair, () => directorComp.Outbreaks > 0, 5);
        await server.WaitAssertion(() =>
        {
            Assert.That(directorComp.Outbreaks, Is.EqualTo(1), "the stone never broke out on the other ship.");
            var aboard = entMan.EntityQuery<WFRemadeComponent>()
                .Count(remade => remade.Source == scion && entMan.GetComponent<TransformComponent>(remade.Owner).GridUid == ship.Owner);
            Assert.That(aboard, Is.InRange(2, 3), "the outbreak didn't bring two or three Remade aboard.");
            var shard = entMan.EntityQuery<WFMonolithShardComponent>()
                .FirstOrDefault(s => entMan.GetComponent<TransformComponent>(s.Owner).GridUid == ship.Owner);
            Assert.That(shard?.Deployed, Is.True, "no shard took root aboard.");
            Assert.That(directorComp.NextOutbreak, Is.GreaterThan(timing.CurTime + TimeSpan.FromSeconds(60)), "the next outbreak comes too soon.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASplinterKeepsItsOutbreakToItsOwnShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var monoliths = entMan.System<WFMonolithSystem>();
        EntityUid splinter = default, crewman = default;
        Entity<MapGridComponent> ship = default;
        var stings = 0;

        await server.WaitPost(() =>
        {
            // A stone another test left on this server would reach every map through the sector share.
            server.CfgMan.SetCVar(Content.Shared._WF.Remade.Dread.WFDreadCVars.Sector, false);
            LayDeck(pair, map);
            ship = OtherShip(entMan, server.ResolveDependency<ITileDefinitionManager>(), map.MapId);
            crewman = SpawnCrewman(pair, At(map, 3, 0));
            // The sweep skips the dead, and the carp sometimes died under CI load; this test is about reach, not harm.
            entMan.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(crewman);
            monoliths.NextSting = TimeSpan.FromHours(10);
            stings = monoliths.StingsPlayed;
            splinter = entMan.SpawnEntity("WFMonolithSplinter", At(map, 0, 0));

            // Its Director plans nothing, so no Remade comes for the crewman this test watches.
            var director = entMan.GetComponent<WFRemadeDirectorComponent>(splinter);
            var far = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromHours(1);
            director.BuildUp = false;
            director.PhaseEnd = far;
            director.NextAmbush = far;
            director.NextStalker = far;
            director.NextCloset = far;
            director.NextDread = far;
            director.NextBuild = far;
            entMan.GetComponent<WFMonolithComponent>(splinter).Resonance = 78f;
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            var monolith = entMan.GetComponent<WFMonolithComponent>(splinter);
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Harvest), "the splinter climbed past the Harvest.");
            Assert.That(monolith.Resonance, Is.LessThan(80f));
            Assert.That(monolith.Resonance, Is.GreaterThan(78f), "the splinter doesn't climb on its own.");
            Assert.That(monoliths.StingsPlayed, Is.GreaterThan(stings), "a splinter's sting waited on the sector's gap.");
            Assert.That(entMan.GetComponent<WFDreadSubjectComponent>(crewman).Bed, Is.GreaterThan(0f), "the crew aboard feel nothing.");
            Assert.That(entMan.System<WFRemadeSiteSystem>().ScionCount(), Is.Zero, "a splinter counts as a sector Scion.");

            // Off the splinter's ship, a short way across open space.
            entMan.System<SharedTransformSystem>().SetCoordinates(crewman, new EntityCoordinates(ship.Owner, new Vector2(-11.5f, 0.5f)));
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            var subject = entMan.GetComponent<WFDreadSubjectComponent>(crewman);
            Assert.That(entMan.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(crewman), Is.True,
                "the crewman died, so the sweep skipped them: " + string.Join(", ", entMan.GetComponent<Content.Shared.Damage.DamageableComponent>(crewman).Damage.DamageDict.Where(kv => kv.Value > 0)));
            var xform = entMan.GetComponent<TransformComponent>(crewman);
            Assert.That(subject.Level, Is.Zero, $"the splinter's dread reached another ship: crewman on {xform.GridUid} at "
                + $"{entMan.System<SharedTransformSystem>().GetWorldPosition(crewman)}, splinter on {entMan.GetComponent<TransformComponent>(splinter).GridUid}, ship {ship.Owner}.");
            Assert.That(subject.Bed, Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClearWipesTheRemadeFromTheRound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, 0, 0));
            var scion = entMan.SpawnEntity("WFMonolithScion", At(map, 6, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 60f;
            entMan.SpawnEntity("WFMonolithSplinter", At(map, -6, 0));
            var remade = entMan.SpawnEntity("WFRemadeSlasher", At(map, 10, 10));
            entMan.System<WFRemadeDirectorSystem>().Enlist(remade, scion);
        });

        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<WFCorruptionComponent>().Any(), Is.True, "nothing grew to clear.");
            Assert.That(entMan.System<WFRemadeClearSystem>().Clear(), Is.GreaterThan(3));
        });

        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<WFMonolithComponent>().Any(), Is.False, "a stone survived the clear.");
            Assert.That(entMan.EntityQuery<WFRemadeComponent>().Any(), Is.False, "a Remade survived the clear.");
            Assert.That(entMan.EntityQuery<WFCorruptionComponent>().Any(), Is.False, "corruption survived the clear.");
        });
        await pair.CleanReturnAsync();
    }
}
