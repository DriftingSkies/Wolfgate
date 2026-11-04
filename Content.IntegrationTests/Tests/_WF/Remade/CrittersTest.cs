#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Abilities;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Critters;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._DV.Abilities;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.CCVar;
using Content.Shared.Climbing.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The small Remade's habits: dead animals rising, hoppers under tables and leapers against walls.</summary>
[TestFixture]
public sealed class CrittersTest
{
    private const string Hopper = "WFRemadeHopper";
    private const string Lurker = "WFRemadeLurker";
    private const string Leaper = "WFRemadeLeaper";

    private static EntityUid SpawnDead(IEntityManager entMan, string id, EntityCoordinates coordinates)
    {
        var uid = entMan.SpawnEntity(id, coordinates);
        entMan.System<MobStateSystem>().ChangeMobState(uid, MobState.Dead);
        return uid;
    }

    private static List<EntityUid> Remade(IEntityManager entMan, string id)
    {
        return entMan.AllEntities<WFRemadeComponent>()
            .Where(remade => entMan.GetComponent<MetaDataComponent>(remade.Owner).EntityPrototype?.ID == id)
            .Select(remade => remade.Owner)
            .ToList();
    }

    [Test]
    public async Task ADeadAnimalTurnsIntoAHopperOrALurkerBySize()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();
        EntityUid mouse = default, cat = default, cow = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            mouse = SpawnDead(entMan, "MobMouse", WFRemadeDirectorTest.At(map, 0, 0));
            cat = SpawnDead(entMan, "MobCat", WFRemadeDirectorTest.At(map, 3, 0));
            cow = SpawnDead(entMan, "MobCow", WFRemadeDirectorTest.At(map, 6, 0));
        });

        await server.WaitAssertion(() =>
        {
            foreach (var (animal, name) in new[] { (mouse, "mouse"), (cat, "cat"), (cow, "cow") })
            {
                Assert.That(conversion.IsAnimal(animal), Is.True, $"the {name} wasn't counted as an animal.");
                Assert.That(conversion.CanConvert(animal), Is.True, $"a dead {name} can't turn.");
            }

            for (var i = 0; i < 30; i++)
            {
                Assert.That(conversion.Pick(mouse).Id, Is.EqualTo(Hopper), "a dead mouse rose as something else.");
                Assert.That(conversion.Pick(cat).Id, Is.EqualTo(Hopper), "a dead cat rose as something else.");
                Assert.That(conversion.Pick(cow).Id, Is.EqualTo(Lurker), "a dead cow rose as something else.");
            }

            foreach (var animal in new[] { mouse, cat, cow })
                Assert.That(conversion.TryStartConversion(animal, null, null, instant: true), Is.True);
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(mouse) && entMan.Deleted(cat) && entMan.Deleted(cow), 10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(mouse) && entMan.Deleted(cat) && entMan.Deleted(cow), Is.True, "the animals never burst.");
            Assert.That(Remade(entMan, Hopper), Has.Count.EqualTo(2), "a dead mouse and cat didn't make two hoppers.");
            Assert.That(Remade(entMan, Lurker), Has.Count.EqualTo(1), "a dead cow didn't make a lurker.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnAnimalThatCannotTurnDoesNot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();

        await server.WaitAssertion(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);

            var alive = entMan.SpawnEntity("MobMouse", WFRemadeDirectorTest.At(map, 0, 0));
            Assert.That(conversion.CanConvert(alive), Is.False, "a live mouse can turn.");

            var headless = SpawnDead(entMan, "MobMouse", WFRemadeDirectorTest.At(map, 2, 0));
            var body = entMan.System<BodySystem>();
            foreach (var head in body.GetBodyChildrenOfType(headless, BodyPartType.Head).ToList())
                entMan.DeleteEntity(head.Id);
            Assert.That(conversion.CanConvert(headless), Is.False, "a mouse with no head can turn.");

            var minded = SpawnDead(entMan, "MobMouse", WFRemadeDirectorTest.At(map, 4, 0));
            var minds = entMan.System<SharedMindSystem>();
            var mind = minds.CreateMind(null, "test");
            minds.TransferTo(mind, minded);
            Assert.That(conversion.CanConvert(minded), Is.False, "a mouse with someone in it can turn.");

            var remade = SpawnDead(entMan, Hopper, WFRemadeDirectorTest.At(map, 6, 0));
            Assert.That(conversion.CanConvert(remade), Is.False, "a dead Remade can turn.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnAnimalWithNoBodyJustComesApart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();
        EntityUid cat = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            cat = SpawnDead(entMan, "MobCat", WFRemadeDirectorTest.At(map, 0, 0));
            entMan.RemoveComponent<BodyComponent>(cat);
            Assert.That(conversion.CanConvert(cat), Is.True, "a bodiless cat can't turn.");
            Assert.That(conversion.TryStartConversion(cat, null, null, instant: true), Is.True);
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(cat), 10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(cat), Is.True, "the cat never burst.");
            Assert.That(Remade(entMan, Hopper), Has.Count.EqualTo(1), "the cat didn't make a hopper.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADeadHumanoidStillTurnsIntoTheUsualRemade()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            human = SpawnDead(entMan, "MobHuman", WFRemadeDirectorTest.At(map, 0, 0));
            Assert.That(conversion.IsAnimal(human), Is.False, "a human counted as an animal.");
            Assert.That(conversion.CanConvert(human), Is.True);
            for (var i = 0; i < 100; i++)
            {
                var pick = conversion.Pick(human).Id;
                Assert.That(pick, Is.Not.EqualTo(Hopper).And.Not.EqualTo(Lurker), "a human rose as a critter.");
            }

            Assert.That(conversion.TryStartConversion(human, null, null, instant: true), Is.True);
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(human), 10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(human), Is.True, "the human never burst.");
            Assert.That(entMan.AllEntities<WFRemadeComponent>().Length, Is.EqualTo(1), "a human didn't make exactly one Remade.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AHopperWithNoTargetGoesUnderATableAndSpringsOutWhenPreyComesClose()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid crewman = default, hopper = default, table = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            // 24 m apart: past the hopper's sight, but near enough that it stays awake.
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 12, 0));
            table = entMan.SpawnEntity("TableWood", WFRemadeDirectorTest.At(map, -8, 2));
            hopper = entMan.SpawnEntity(Hopper, WFRemadeDirectorTest.At(map, -12, 0));
        });

        var hider = entMan.GetComponent<WFHiderComponent>(hopper);
        await WFRemadeDirectorTest.RunUntil(pair, () => hider.Cover != null, 40);
        await server.WaitAssertion(() =>
        {
            Assert.That(hider.Cover, Is.EqualTo((EntityUid?) table), $"the hopper never went under the table: {WFRemadeDirectorTest.Describe(pair, hopper)}.");
            Assert.That(entMan.GetComponent<ClimbingComponent>(hopper).IsClimbing, Is.True, "it isn't on the table.");
            Assert.That(entMan.GetComponent<CrawlUnderObjectsComponent>(hopper).Enabled, Is.True, "it isn't flagged as under the table.");
            Assert.That(entMan.System<SharedAppearanceSystem>().TryGetData<bool>(hopper, SneakMode.Enabled, out var sneaking) && sneaking,
                Is.True, "the client isn't told to draw it under the table.");
            Assert.That((transforms.GetWorldPosition(hopper) - transforms.GetWorldPosition(table)).Length(), Is.LessThan(0.7f));
        });

        // It stays put with nobody about.
        await WFRemadeDirectorTest.RunUntil(pair, () => hider.Cover == null, 12);
        await server.WaitAssertion(() =>
        {
            Assert.That(hider.Cover, Is.EqualTo((EntityUid?) table), "it left the table with nobody near.");
            transforms.SetCoordinates(crewman, WFRemadeDirectorTest.At(map, -5, 2));
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => hider.Cover == null, 20);
        await server.WaitAssertion(() =>
        {
            Assert.That(hider.Cover, Is.Null, $"it never sprang out: {WFRemadeDirectorTest.Describe(pair, hopper)}.");
            Assert.That(entMan.GetComponent<CrawlUnderObjectsComponent>(hopper).Enabled, Is.False, "it is still flagged as under the table.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ALeaperWithNoTargetStandsAgainstAWallAndLeapsFurtherFromIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cling = entMan.System<WFWallClingSystem>();
        EntityUid crewman = default, leaper = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            for (var y = -4; y <= 4; y++)
                entMan.SpawnEntity("WallSolid", WFRemadeDirectorTest.At(map, 8, y));

            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, -20, -5));
            leaper = entMan.SpawnEntity(Leaper, WFRemadeDirectorTest.At(map, 0, 0));
            Assert.That(cling.AgainstWall(leaper), Is.False, "the leaper started against a wall.");
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => cling.AgainstWall(leaper), 40);
        await server.WaitAssertion(() =>
        {
            Assert.That(cling.AgainstWall(leaper), Is.True, $"the leaper never reached a wall: {WFRemadeDirectorTest.Describe(pair, leaper)}.");
        });

        // And it stays.
        await WFRemadeDirectorTest.RunUntil(pair, () => !cling.AgainstWall(leaper), 8);
        await server.WaitAssertion(() =>
        {
            Assert.That(cling.AgainstWall(leaper), Is.True, "the leaper wandered off the wall.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ALeaperAgainstAWallLeapsFurtherThanOneInTheOpen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var abilities = entMan.System<WFAbilitySystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid crewman = default, walled = default, open = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            for (var y = -4; y <= 4; y++)
                entMan.SpawnEntity("WallSolid", WFRemadeDirectorTest.At(map, 8, y));

            // 14 m from each: past the leap's 12 m, inside the 16 it has from a wall.
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, -7, 0));
            walled = entMan.SpawnEntity(Leaper, WFRemadeDirectorTest.At(map, 7, 0));
            open = entMan.SpawnEntity(Leaper, WFRemadeDirectorTest.At(map, -7, -14));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var wallLeap = abilities.Find<WFLeapAbility>(walled)!;
            var openLeap = abilities.Find<WFLeapAbility>(open)!;
            var wallStart = wallLeap.TryStart(new WFAbilityContext(abilities, walled, crewman, false, timing.CurTime));
            var openStart = openLeap.TryStart(new WFAbilityContext(abilities, open, crewman, false, timing.CurTime));
            Assert.That(abilities.AgainstWall(walled), Is.True);
            Assert.That(abilities.AgainstWall(open), Is.False);
            Assert.That(wallStart, Is.EqualTo(WFAbilityResult.Running), "a leaper at a wall couldn't leap 14 m.");
            Assert.That(openStart, Is.EqualTo(WFAbilityResult.None), "a leaper in the open leapt 14 m.");
        });
        await pair.CleanReturnAsync();
    }
}
