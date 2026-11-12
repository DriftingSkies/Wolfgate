using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Outposts;
using Content.Server._NF.Bank;
using Content.Server._WF.Drydock;
using Content.Server._WF.Hideouts;
using Content.Server._WF.Hideouts.Shipwright;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Preferences.Managers;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.Hideouts.Shipwright;
using Content.Shared._WF.Outposts;
using Content.Shared.Preferences;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Hideouts;

/// <summary>
/// The shipwright bay builds a frame from its keel, fits guns within the hull's refit points, refuses what would go over
/// them, and won't save a hull carrying a hand-built gun until it is certified.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipwrightSystem))]
public sealed class ShipwrightTest
{
    private static readonly Vector2i YardFrom = new(-12, -34);
    private static readonly Vector2i YardTo = new(26, -3);

    /// <summary>Every system names real components and parts, and every frame loads and fits the widest bay.</summary>
    [Test]
    public async Task SystemsAndFramesAreSound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var loader = server.System<MapLoaderSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.HasIndex<WFShipwrightRulesPrototype>(WFShipwrightRulesPrototype.Default), Is.True);
            foreach (var system in protos.EnumeratePrototypes<WFShipwrightSystemPrototype>())
            {
                Assert.That(protos.HasIndex<EntityPrototype>(system.Install), Is.True, $"{system.ID} installs unknown {system.Install}.");
                Assert.That(system.Components.Count + system.Prototypes.Count, Is.GreaterThan(0), $"{system.ID} recognises nothing.");
                foreach (var name in system.Components)
                    Assert.That(factory.TryGetRegistration(name, out _), Is.True, $"{system.ID} names unknown component {name}.");
                foreach (var proto in system.Prototypes)
                    Assert.That(protos.HasIndex<EntityPrototype>(proto), Is.True, $"{system.ID} names unknown prototype {proto}.");
            }

            var largest = 0;
            var maps = server.System<SharedMapSystem>();
            foreach (var frame in protos.EnumeratePrototypes<WFShipwrightFramePrototype>())
            {
                Assert.That(loader.TryLoadGrid(map.MapId, frame.Grid, out var grid), Is.True, $"{frame.ID} failed to load.");
                var box = grid.Value.Comp.LocalAABB;
                Assert.That(Math.Max(box.Width, box.Height), Is.LessThanOrEqualTo(25), $"{frame.ID} is wider than the widest bay.");
                var tiles = 0;
                var enumerator = maps.GetAllTilesEnumerator(grid.Value.Owner, grid.Value.Comp);
                while (enumerator.MoveNext(out _))
                    tiles++;
                largest = Math.Max(largest, tiles);
                entMan.DeleteEntity(grid.Value.Owner);
            }

            var rules = protos.Index<WFShipwrightRulesPrototype>(WFShipwrightRulesPrototype.Default);
            Assert.That(rules.CustomMaxTiles, Is.EqualTo(largest), "customMaxTiles isn't the largest frame's floor.");
            Assert.That(protos.EnumeratePrototypes<WFShipwrightFramePrototype>().Count(), Is.EqualTo(26));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheBayBuildsAFrameAndKeepsItWithinItsPoints()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var hideouts = server.System<WFHideoutSystem>();
        var shipwright = server.System<WFShipwrightSystem>();
        var drydock = server.System<WFDrydockSystem>();
        var bank = server.System<BankSystem>();
        var transform = server.System<SharedTransformSystem>();
        var owner = pair.Player.UserId;
        await Fund(pair, user, 20_000_000);
        await PrepareYard(pair, ground);

        int Balance()
        {
            bank.TryGetBalance(pair.Player, out var balance);
            return balance;
        }

        // Planet birds trip a PVS error log unrelated to the shipwright; keep them off the test world while it waits.
        async Task ClearBirds()
        {
            await server.WaitPost(() =>
            {
                var birds = new List<EntityUid>();
                var query = entMan.EntityQueryEnumerator<Content.Shared._WF.Birds.WFBirdComponent>();
                while (query.MoveNext(out var bird, out _))
                    birds.Add(bird);
                foreach (var bird in birds)
                    entMan.DeleteEntity(bird);
            });
        }

        async Task RunJob(EntityUid console)
        {
            for (var second = 0; second < 90; second++)
            {
                await ClearBirds();
                await pair.RunTicksSync(pair.SecondsToTicks(1f));
                if (!entMan.HasComponent<WFDrydockJobComponent>(console))
                    break;
            }

            await ClearBirds();
            await PoolManager.WaitUntil(server, () => !hideouts.IsBusy(owner), maxTicks: 900);
            await pair.RunTicksSync(1);
        }

        try
        {
            Entity<WFOutpostComponent> outpost = default;
            Entity<WFShipwrightConsoleComponent> console = default;
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                outpost = (grid.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value));

                foreach (var module in new[] { "WFHideoutGroundworks", "WFHideoutGroundworks", "WFHideoutPowerhouse", "WFHideoutShipwright" })
                {
                    HideoutTest.Supply(pair, outpost, module);
                    Assert.That(hideouts.TryBuild(user, outpost, module, out reason), Is.True, $"{module} was refused: {reason}.");
                    Assert.That(hideouts.SkipBuild(outpost), Is.True);
                }

                // The powerhouse's stake isn't wanted here; the bay's is the newest.
                var stakes = new List<Entity<WFHideoutStakeComponent>>();
                var query = entMan.EntityQueryEnumerator<WFHideoutStakeComponent>();
                while (query.MoveNext(out var uid, out var comp))
                    stakes.Add((uid, comp));
                Assert.That(stakes, Has.Count.EqualTo(2));
                var bayStake = stakes.Single(s => entMan.GetComponent<MetaDataComponent>(s).EntityName.Contains("Shipwright"));
                transform.SetWorldRotation(user, Direction.South.ToAngle());
                Assert.That(hideouts.TryUseStake(user, bayStake, new EntityCoordinates(ground, 6.5f, -2.5f), out reason), Is.True,
                    $"The bay's stake was refused: {reason}.");

                var consoles = entMan.EntityQueryEnumerator<WFShipwrightConsoleComponent>();
                Assert.That(consoles.MoveNext(out var consoleUid, out var consoleComp), Is.True, "The bay came without its console.");
                console = (consoleUid, consoleComp);
                shipwright.LinkCranes(console);
                var dock = entMan.GetComponent<WFDrydockConsoleComponent>(console);
                Assert.That(drydock.TryGetBerth(console, dock, out _), Is.True, "The console's cranes make no berth.");
                Assert.That(shipwright.TryGetBay(console, out var bay), Is.True, "The console found no bay.");
                Assert.That(bay.Comp.Size, Is.EqualTo(17), "Shipwright 1 sets the bay to 17 tiles.");
                // Keep the test short; the work is sped up to fit.
                dock.MaxDuration = 15f;
                dock.MaxRefitDuration = 15f;

                var before = Balance();
                Assert.That(shipwright.TryBuildFrame(user, console, "WFFrameGuppy", out reason), Is.True, $"The frame was refused: {reason}.");
                Assert.That(Balance(), Is.EqualTo(before - 13000), "The frame was paid for.");
                Assert.That(entMan.HasComponent<WFDrydockJobComponent>(console), Is.True, "No crane job started.");
            });

            await RunJob(console);

            EntityUid hull = default;
            await server.WaitAssertion(() =>
            {
                Assert.That(hideouts.TryGetShip(owner, out hull), Is.True, "The finished frame wasn't registered.");
                Assert.That(hideouts.IsHome(owner, hull), Is.True, "The frame isn't on the bay.");
                var grid = entMan.GetComponent<MapGridComponent>(hull);
                var tiles = 0;
                var enumerator = server.System<SharedMapSystem>().GetAllTilesEnumerator(hull, grid);
                while (enumerator.MoveNext(out _))
                    tiles++;
                Assert.That(tiles, Is.EqualTo(98), "The cranes didn't lay every tile of the Guppy.");
                Assert.That(Children(entMan, hull).Any(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "ComputerShuttle"
                    || entMan.HasComponent<Content.Server.Shuttles.Components.ShuttleConsoleComponent>(e)), Is.True, "The frame has no helm.");
                var survey = shipwright.Survey(hull, outpost);
                Assert.That(survey.Frame, Is.EqualTo("WFFrameGuppy"));
                Assert.That(survey.Budget, Is.EqualTo(10));
                Assert.That(survey.Certified, Is.True, "A bare frame should pass.");
                Assert.That(hideouts.GetStored(owner), Is.Not.Null, "The frame wasn't saved.");
            });

            // Fit a gun to the first hardpoint.
            await server.WaitAssertion(() =>
            {
                var hardpoints = shipwright.Hardpoints(hull);
                Assert.That(hardpoints, Has.Count.EqualTo(2), "The Guppy frame has two hardpoints.");
                var (first, slot) = hardpoints[0];
                var gun = shipwright.Guns().First(g => g.Size <= (int) slot.CompatibleSizes && (g.Types & (int) slot.CompatibleTypes) != 0
                    && g.Points <= 10);
                var mounts = new Dictionary<NetEntity, string> { [entMan.GetNetEntity(first)] = gun.Id };
                Assert.That(shipwright.TryRefit(user, console, mounts, new Dictionary<string, int>(), out var reason), Is.True,
                    $"The refit was refused: {reason}.");

                // A heavy shield is twice the Guppy's points.
                Assert.That(shipwright.TryRefit(user, console, new Dictionary<NetEntity, string>(),
                    new Dictionary<string, int> { ["WFSystemShieldHeavy"] = 1 }, out reason), Is.False, "A refit started while the cranes worked.");
            });

            await RunJob(console);

            await server.WaitAssertion(() =>
            {
                var (first, slot) = shipwright.Hardpoints(hull)[0];
                Assert.That(slot.anchoring, Is.Not.Null, "The gun isn't on its hardpoint.");
                Assert.That(entMan.HasComponent<WFShipwrightFittedComponent>(slot.anchoring!.Value), Is.True, "The refit's gun isn't marked fitted.");
                var survey = shipwright.Survey(hull, outpost);
                Assert.That(survey.Spent, Is.GreaterThan(0));
                Assert.That(survey.Certified, Is.True);

                Assert.That(shipwright.TryRefit(user, console, new Dictionary<NetEntity, string>(),
                    new Dictionary<string, int> { ["WFSystemShieldHeavy"] = 1 }, out var reason), Is.False, "A refit over the points started.");
                Assert.That(reason, Is.EqualTo("wf-shipwright-over-budget"));

                // A gun bolted on by hand: the hull no longer passes, and isn't saved.
                var (second, secondSlot) = shipwright.Hardpoints(hull)[1];
                var gun = shipwright.Guns().First(g => g.Size <= (int) secondSlot.CompatibleSizes && (g.Types & (int) secondSlot.CompatibleTypes) != 0);
                entMan.SpawnEntity(gun.Id, entMan.GetComponent<TransformComponent>(second).Coordinates);
            });
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var survey = shipwright.Survey(hull, outpost);
                Assert.That(survey.UnfittedWeapons.Count + survey.UnfittedSystems.Count, Is.EqualTo(1), "The hand-built gun wasn't seen.");
                Assert.That(survey.Certified, Is.False);
                Assert.That(hideouts.TrySaveHome(owner, out var reason), Is.False, "A hull with a hand-built gun was saved.");
                Assert.That(reason, Is.EqualTo("wf-shipwright-uncertified"));
                Assert.That(shipwright.TryCertify(user, console, out reason), Is.True, $"Certifying was refused: {reason}.");
            });

            await RunJob(console);

            await server.WaitAssertion(() =>
            {
                var survey = shipwright.Survey(hull, outpost);
                Assert.That(survey.Certified, Is.True, "Certifying left the hull failing.");
                Assert.That(hideouts.TrySaveHome(owner, out var reason), Is.True, $"The certified hull wasn't saved: {reason}.");
            });
            await PoolManager.WaitUntil(server, () => !hideouts.IsBusy(owner), maxTicks: 900);

            // Hardpoints: the second one comes off, and a new one goes on a free edge tile.
            Vector2i placedTile = default;
            await server.WaitAssertion(() =>
            {
                var before = shipwright.Survey(hull, outpost);
                Assert.That(before.Stock, Is.EqualTo(2), "The Guppy's own hardpoints aren't marked as the frame's.");
                var (second, _) = shipwright.Hardpoints(hull)[1];
                var free = shipwright.FreeEdgeTiles(hull);
                Assert.That(free, Is.Not.Empty, "The hull has no free edge tile.");
                placedTile = free[0];
                var type = shipwright.MountTypes().First(t => t.Size == 2);
                var hardpoints = new Dictionary<NetEntity, string> { [entMan.GetNetEntity(second)] = string.Empty };
                var placements = new List<WFShipwrightPlacement> { new(placedTile, type.Id, string.Empty) };

                // A tile inside the hull is refused.
                var inner = new List<WFShipwrightPlacement> { new(new Vector2i(9999, 9999), type.Id, string.Empty) };
                Assert.That(shipwright.TryRefit(user, console, new Dictionary<NetEntity, string>(), new Dictionary<string, int>(),
                    new Dictionary<NetEntity, string>(), inner, out var reason), Is.False);
                Assert.That(reason, Is.EqualTo("wf-shipwright-refit-bad-tile"));

                Assert.That(shipwright.TryRefit(user, console, new Dictionary<NetEntity, string>(), new Dictionary<string, int>(),
                    hardpoints, placements, out reason), Is.True, $"The hardpoint refit was refused: {reason}.");
            });

            await RunJob(console);

            await server.WaitAssertion(() =>
            {
                var hardpoints = shipwright.Hardpoints(hull);
                Assert.That(hardpoints, Has.Count.EqualTo(2), "One hardpoint off and one on should leave two.");
                var placed = hardpoints.Single(h => Vector2i.Equals(new Vector2i((int) MathF.Floor(entMan.GetComponent<TransformComponent>(h.Uid).LocalPosition.X),
                    (int) MathF.Floor(entMan.GetComponent<TransformComponent>(h.Uid).LocalPosition.Y)), placedTile));
                Assert.That(entMan.HasComponent<WFShipwrightFittedComponent>(placed.Uid), Is.True, "The new hardpoint isn't fitted.");
                Assert.That(entMan.HasComponent<WFShipwrightStockComponent>(placed.Uid), Is.False, "A laid hardpoint counts as the frame's.");
                var survey = shipwright.Survey(hull, outpost);
                Assert.That(survey.Stock, Is.EqualTo(1));
                Assert.That(survey.Certified, Is.True, "The hull fails after the hardpoint refit.");
            });

            // Upgrades: the drydock's refit lines are on the console, and the cranes bring one up a tier.
            await server.WaitAssertion(() =>
            {
                Assert.That(shipwright.TryUpgrade(user, console, "WFRefitThrusters", out var reason), Is.True, $"The upgrade was refused: {reason}.");
            });

            await RunJob(console);

            await server.WaitAssertion(() =>
                Assert.That(server.System<Content.Server._WF.Drydock.WFShipRefitSystem>().GetRefit(hull, "WFRefitThrusters").Tier, Is.GreaterThan(0),
                    "The thruster upgrade wasn't recorded."));

            // Outposts: a stake marks its module out on the ground, and the outline goes when its time is up.
            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.TryMarkPrefab(user, outpost, new Robust.Shared.Utility.ResPath("/SharedMaps/_WF/Hideouts/vault.yml"),
                    new EntityCoordinates(ground, 10.5f, 3.5f), Direction.East, 1f, out var reason), Is.True, $"Marking out was refused: {reason}.");
                var pending = entMan.GetComponent<WFOutpostPendingLoadComponent>(outpost);
                Assert.That(pending.Tiles, Has.Count.EqualTo(61), "The vault's outline, porch and all, wasn't marked.");
                Assert.That(entMan.GetComponent<MapGridComponent>(outpost.Owner), Is.Not.Null);
            });
            await ClearBirds();
            await pair.RunTicksSync(pair.SecondsToTicks(2f));
            await ClearBirds();
            await server.WaitAssertion(() =>
                Assert.That(entMan.HasComponent<WFOutpostPendingLoadComponent>(outpost), Is.False, "The outline stayed up."));
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The test world command founds an outpost with its buildings, the bay linked and a ship on the hangar pad.</summary>
    [Test]
    public async Task TheTestWorldCommandBuildsAPlayableOutpost()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var map = await pair.CreateTestMap();
        var body = await AttachViewer(pair, map.MapUid, System.Numerics.Vector2.Zero);
        await Fund(pair, body, 0);
        var owner = pair.Player.UserId;
        var outposts = server.System<WFOutpostSystem>();
        var hideouts = server.System<WFHideoutSystem>();
        var shipwright = server.System<WFShipwrightSystem>();
        var drydock = server.System<WFDrydockSystem>();

        await server.WaitPost(() => server.ResolveDependency<Robust.Shared.Console.IConsoleHost>().ExecuteCommand(pair.Player, "wfhideouttest"));
        await RunClearingBirds(pair, 8);

        await server.WaitAssertion(() =>
        {
            Assert.That(outposts.TryGetOutpost(owner, out var outpost), Is.True, "The command founded no outpost.");
            Assert.That(hideouts.TryGetHangar(outpost, out var hangar, shipwright: false), Is.True, "No hangar was laid.");
            Assert.That(hangar.Comp.Parked, Is.Not.Null, "No test ship sits on the hangar pad.");
            Assert.That(hideouts.TryGetHangar(outpost, out var bay, shipwright: true), Is.True, "No shipwright bay was laid.");
            Assert.That(bay.Comp.Widest, Is.EqualTo(25), "The bay isn't at its widest.");
            var consoles = entMan.EntityQueryEnumerator<WFShipwrightConsoleComponent>();
            Assert.That(consoles.MoveNext(out var console, out _), Is.True, "The bay has no console.");
            Assert.That(drydock.TryGetBerth(console, entMan.GetComponent<WFDrydockConsoleComponent>(console), out _), Is.True,
                "The bay's cranes aren't linked.");
            Assert.That(hideouts.Level(outpost, "WFHideoutShipwright"), Is.EqualTo(3));
        });

        await PoolManager.WaitUntil(server, () => !hideouts.IsBusy(owner), maxTicks: 900);
        Entity<WFShipwrightConsoleComponent> hangarConsole = default;
        EntityUid ship = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(outposts.TryGetOutpost(owner, out var outpost), Is.True);
            var user = pair.Player.AttachedEntity!.Value;
            Assert.That(hideouts.TryRegister(user, outpost, out var reason), Is.True, $"The Guppy wasn't registered: {reason}.");
            var consoles = entMan.EntityQueryEnumerator<WFShipwrightConsoleComponent>();
            while (consoles.MoveNext(out var uid, out var comp))
            {
                if (comp.Faction)
                    hangarConsole = (uid, comp);
            }

            Assert.That(hangarConsole.Owner, Is.Not.EqualTo(EntityUid.Invalid), "The hangar has no refit console.");
            shipwright.LinkCranes(hangarConsole);
            entMan.GetComponent<WFDrydockConsoleComponent>(hangarConsole).MaxDuration = 10f;
            Assert.That(hideouts.TryGetShip(owner, out ship), Is.True);
            var stock = shipwright.FactionBudget(ship);
            Assert.That(stock, Is.GreaterThan(0), "The Guppy's stock firepower reads as nothing.");

            var (hardpoint, slot) = shipwright.Hardpoints(ship)[0];
            var current = slot.anchoring is { } gun ? entMan.GetComponent<MetaDataComponent>(gun).EntityPrototype?.ID : null;
            var swap = shipwright.Guns().First(g => g.Id != current && g.Size <= (int) slot.CompatibleSizes
                && (g.Types & (int) slot.CompatibleTypes) != 0 && g.Points <= 2);
            var heavy = new Dictionary<string, int> { ["WFSystemShieldHeavy"] = 1 };
            Assert.That(shipwright.TryFactionRefit(user, hangarConsole, new Dictionary<NetEntity, string>(), heavy, out reason), Is.False,
                "A faction hangar fitted a shield.");
            Assert.That(shipwright.TryFactionRefit(user, hangarConsole,
                new Dictionary<NetEntity, string> { [entMan.GetNetEntity(hardpoint)] = swap.Id }, new Dictionary<string, int>(), out reason),
                Is.True, $"The faction refit was refused: {reason}.");
        });

        for (var second = 0; second < 60 && entMan.HasComponent<Content.Server._WF.Drydock.WFDrydockJobComponent>(hangarConsole); second++)
            await RunClearingBirds(pair, 1);
        await RunClearingBirds(pair, 1);
        await server.WaitAssertion(() =>
        {
            var (_, slot) = shipwright.Hardpoints(ship)[0];
            Assert.That(slot.anchoring, Is.Not.Null, "The swapped gun isn't on its hardpoint.");
            Assert.That(shipwright.FactionSpent(ship), Is.LessThanOrEqualTo(shipwright.FactionBudget(ship)));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The test world command's gallery lays every layout of every module along its streets, and leaves a stake of each.</summary>
    [Test]
    public async Task TheGalleryLaysEveryLayout()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var map = await pair.CreateTestMap();
        var body = await AttachViewer(pair, map.MapUid, System.Numerics.Vector2.Zero);
        await Fund(pair, body, 0);
        var owner = pair.Player.UserId;
        var outposts = server.System<WFOutpostSystem>();
        var protos = server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();

        await server.WaitPost(() => server.ResolveDependency<Robust.Shared.Console.IConsoleHost>().ExecuteCommand(pair.Player, "wfhideouttest gallery"));
        await RunClearingBirds(pair, 8);

        await server.WaitAssertion(() =>
        {
            Assert.That(outposts.TryGetOutpost(owner, out var outpost), Is.True, "The command founded no outpost.");
            var standing = new Dictionary<string, int>();
            var children = entMan.GetComponent<TransformComponent>(outpost).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID is { } id)
                    standing[id] = standing.GetValueOrDefault(id) + 1;
            }

            // Every layout but the two aprons: each has an APC but the gardens, which have a fountain.
            var modules = protos.EnumeratePrototypes<Content.Shared._WF.Hideouts.WFHideoutModulePrototype>()
                .Where(module => module.ID is not ("WFHideoutHangar" or "WFHideoutShipwright")).ToList();
            var layouts = modules.Sum(module => module.Levels.Where(level => level.Prefab != null).Take(1)
                .Sum(level => Math.Max(1, level.Layouts.Count)));
            Assert.That(standing.GetValueOrDefault("APCBasic") + standing.GetValueOrDefault("DarkPackFountain"), Is.EqualTo(layouts),
                "Not every layout was laid.");
            Assert.That(standing.GetValueOrDefault("WallBrick"), Is.GreaterThan(0), "Nothing was laid in the brick theme.");
            Assert.That(standing.GetValueOrDefault("WFPrefabWallFixed"), Is.GreaterThan(0), "Nothing was laid in the colony theme.");

            var stakes = 0;
            var query = entMan.EntityQueryEnumerator<WFHideoutStakeComponent>();
            while (query.MoveNext(out _, out _))
                stakes++;
            Assert.That(stakes, Is.EqualTo(modules.Count(module => module.Levels.Any(level => level.Prefab != null)) + 2),
                "A module's stake is missing.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A faction hangar refuses a hull that isn't a stock shipyard ship.</summary>
    [Test]
    public async Task AFactionHangarTakesOnlyStockHulls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var hull = await BuildHull(pair, map.MapId);
        await server.WaitAssertion(() =>
        {
            var hideouts = server.System<WFHideoutSystem>();
            Assert.That(hideouts.IsFactionHull(hull, hull, out var reason), Is.False, "A hand-built hull passed as stock.");
            Assert.That(reason, Is.EqualTo("wf-hideout-ship-not-stock"));
            server.EntMan.AddComponent<Content.Shared._Mono.Ships.Components.VesselComponent>(hull).VesselId = "Guppy";
            Assert.That(hideouts.IsFactionHull(hull, hull, out reason), Is.True, $"A stock Guppy was refused: {reason}.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Runs the given seconds a second at a time, taking planet birds off the world before each, as they trip a PVS error log.</summary>
    private static async Task RunClearingBirds(TestPair pair, int seconds)
    {
        var entMan = pair.Server.EntMan;
        for (var i = 0; i < seconds; i++)
        {
            await pair.Server.WaitPost(() =>
            {
                var birds = new List<EntityUid>();
                var query = entMan.EntityQueryEnumerator<Content.Shared._WF.Birds.WFBirdComponent>();
                while (query.MoveNext(out var bird, out _))
                    birds.Add(bird);
                foreach (var bird in birds)
                    entMan.DeleteEntity(bird);
            });
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
        }
    }

    private static async Task Fund(TestPair pair, EntityUid user, int balance)
    {
        var server = pair.Server;
        var owner = pair.Player.UserId;
        var funding = Task.CompletedTask;
        await server.WaitPost(() =>
        {
            var prefs = server.ResolveDependency<IServerPreferencesManager>();
            var p = prefs.GetPreferences(owner);
            var profile = (HumanoidCharacterProfile) p.SelectedCharacter;
            funding = prefs.SetProfile(owner, p.SelectedCharacterIndex, profile.WithBankBalance(balance));
            server.EntMan.EnsureComponent<BankAccountComponent>(user);
        });
        await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);
    }

    private static async Task PrepareYard(TestPair pair, EntityUid ground)
    {
        var server = pair.Server;
        await LayTiles(pair, ground, YardFrom, YardTo);
        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var maps = server.System<SharedMapSystem>();
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var clear = new List<EntityUid>();
            for (var x = YardFrom.X; x <= YardTo.X; x++)
            for (var y = YardFrom.Y; y <= YardTo.Y; y++)
            {
                var anchored = maps.GetAnchoredEntitiesEnumerator(ground, grid, new Vector2i(x, y));
                while (anchored.MoveNext(out var ent))
                    clear.Add(ent.Value);
            }

            foreach (var ent in clear)
                entMan.DeleteEntity(ent);
        });
        await pair.RunTicksSync(5);
    }
}
