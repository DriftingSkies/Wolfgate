using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Outposts;
using Content.Server._NF.Bank;
using Content.Server._WF.Hideouts;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.ShipAccess;
using Content.Server.Preferences.Managers;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Hideouts;
using Content.Shared._WF.Outposts;
using Content.Shared.Preferences;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Hideouts;

/// <summary>Outpost modules are bought in order and lay their prefabs; the hangar keeps, insures and brings back a ship.</summary>
[TestFixture]
[TestOf(typeof(WFHideoutSystem))]
public sealed class HideoutTest
{
    private const string Groundworks = "WFHideoutGroundworks";
    private const string Hangar = "WFHideoutHangar";
    private const string Quarters = "WFHideoutQuarters";

    /// <summary>Open ground south of the founding patch, for the prefabs.</summary>
    private static readonly Vector2i YardFrom = new(-12, -30);
    private static readonly Vector2i YardTo = new(22, -7);

    private static async Task<EntityUid> Fund(TestPair pair, EntityUid user, int balance)
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
        return user;
    }

    /// <summary>Clears and floors the yard south of the founding patch.</summary>
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

    private static Entity<WFOutpostComponent> Found(TestPair pair, EntityUid user, EntityUid ground)
    {
        var entMan = pair.Server.EntMan;
        var site = OutpostFoundingTest.Site;
        Assert.That(pair.Server.System<WFOutpostSystem>().TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
            new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
        return (grid.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value));
    }

    /// <summary>Lays the materials and parts a module's next level needs on the outpost, beside its console.</summary>
    internal static void Supply(TestPair pair, Entity<WFOutpostComponent> outpost, string module)
    {
        var entMan = pair.Server.EntMan;
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();
        var hideouts = pair.Server.System<WFHideoutSystem>();
        var stacks = pair.Server.System<SharedStackSystem>();
        var level = protos.Index<WFHideoutModulePrototype>(module).Levels[hideouts.Level(outpost, module)];
        var at = entMan.GetComponent<TransformComponent>(outpost.Comp.Console).Coordinates;
        foreach (var (stack, amount) in level.Stacks)
        {
            var stackProto = protos.Index(stack);
            var left = amount;
            while (left > 0)
            {
                var spawned = entMan.SpawnEntity(stackProto.Spawn, at);
                var count = Math.Min(left, stacks.GetMaxCount(spawned));
                stacks.SetCount(spawned, count);
                left -= count;
            }
        }

        foreach (var (part, amount) in level.Parts)
        {
            for (var i = 0; i < amount; i++)
                entMan.SpawnEntity(part, at);
        }
    }

    /// <summary>Supplies and buys a module's next level and finishes it at once.</summary>
    private static void Build(TestPair pair, EntityUid user, Entity<WFOutpostComponent> outpost, string module)
    {
        var hideouts = pair.Server.System<WFHideoutSystem>();
        Supply(pair, outpost, module);
        Assert.That(hideouts.TryBuild(user, outpost, module, out var reason), Is.True, $"{module} was refused: {reason}.");
        Assert.That(hideouts.SkipBuild(outpost), Is.True);
    }

    /// <summary>The stake the last level left, used on a ground tile with the user facing south.</summary>
    private static void UseStake(TestPair pair, EntityUid user, EntityUid ground, Vector2i tile)
    {
        var entMan = pair.Server.EntMan;
        var stakes = new List<Entity<WFHideoutStakeComponent>>();
        var query = entMan.EntityQueryEnumerator<WFHideoutStakeComponent>();
        while (query.MoveNext(out var uid, out var comp))
            stakes.Add((uid, comp));

        Assert.That(stakes, Has.Count.EqualTo(1), "The level left one survey stake.");
        pair.Server.System<SharedTransformSystem>().SetWorldRotation(user, Direction.South.ToAngle());
        Assert.That(pair.Server.System<WFHideoutSystem>().TryUseStake(user, stakes[0], new EntityCoordinates(ground, tile.X + 0.5f, tile.Y + 0.5f),
            out var reason), Is.True, $"The stake was refused: {reason}.");
    }

    /// <summary>
    /// A module waits for the modules it needs, costs its price and takes its build time; a finished level raises the
    /// outpost's caps and leaves a stake that lays its prefab onto the outpost, turned the way its user faces.
    /// </summary>
    [Test]
    public async Task ModulesBuildInOrderAndLayTheirPrefabs()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var hideouts = server.System<WFHideoutSystem>();
        var bank = server.System<BankSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        await Fund(pair, user, 5_000_000);
        await PrepareYard(pair, ground);

        int Balance()
        {
            bank.TryGetBalance(pair.Player, out var balance);
            return balance;
        }

        try
        {
            Entity<WFOutpostComponent> outpost = default;
            await server.WaitAssertion(() =>
            {
                outpost = Found(pair, user, ground);
                Assert.That(hideouts.TryBuild(user, outpost, Quarters, out var reason), Is.False, "Quarters started without Groundworks.");
                Assert.That(reason, Is.EqualTo("wf-hideout-build-requires"));

                var before = Balance();
                var price = protos.Index<WFHideoutModulePrototype>(Groundworks).Levels[0].Price;
                Assert.That(hideouts.TryBuild(user, outpost, Groundworks, out reason), Is.False, "Groundworks started without its materials.");
                Assert.That(reason, Is.EqualTo("wf-hideout-build-materials"));
                Supply(pair, outpost, Groundworks);
                Assert.That(hideouts.TryBuild(user, outpost, Groundworks, out reason), Is.True, $"Groundworks was refused: {reason}.");
                Assert.That(hideouts.Holdings(outpost).GetValueOrDefault(WFHideoutSystem.StackKey("Steel")), Is.EqualTo(0),
                    "The level's steel wasn't used up.");
                Assert.That(Balance(), Is.EqualTo(before - price), "The level was paid for.");
                Assert.That(hideouts.TryBuild(user, outpost, Quarters, out reason), Is.False, "Two modules were built at once.");
                Assert.That(hideouts.Level(outpost, Groundworks), Is.EqualTo(0), "The level stood before its build time ran.");
            });

            // Groundworks takes a minute.
            await pair.RunTicksSync(pair.SecondsToTicks(62f));

            await server.WaitAssertion(() =>
            {
                Assert.That(hideouts.Level(outpost, Groundworks), Is.EqualTo(1), "The build never finished.");
                var tiles = outposts.TileCap(outpost);
                var pods = outposts.PodLimit(outpost);
                Build(pair, user, outpost, Groundworks);
                Assert.That(outposts.TileCap(outpost), Is.EqualTo(tiles + 1000), "Groundworks 2 raises the floor cap.");

                Build(pair, user, outpost, Quarters);
                Assert.That(outposts.PodLimit(outpost), Is.EqualTo(pods + 1), "Quarters 1 allows one more cryopod.");

                var grid = entMan.GetComponent<MapGridComponent>(outpost);
                var floor = outposts.CountTiles((outpost, grid));
                UseStake(pair, user, ground, new Vector2i(6, -3));
                Assert.That(outposts.CountTiles((outpost, grid)), Is.EqualTo(floor + 102), "The bunkhouse's floor and porch joined the outpost.");

                var pod = Children(entMan, outpost).Single(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WFOutpostCryopod");
                var xform = entMan.GetComponent<TransformComponent>(pod);
                Assert.That(xform.Anchored, Is.True, "The cryopod came loose.");
                // Facing south, the bunkhouse runs south from the stake and is turned about: the pod, two rows in on its west side, lands east.
                var at = server.System<SharedTransformSystem>().GetWorldPosition(pod);
                Assert.That(at.Y, Is.EqualTo(-4.5f).Within(0.01f), "The prefab wasn't turned to face its user.");
                Assert.That(at.X, Is.EqualTo(11.5f).Within(0.01f));
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Every module's prefab loads as one grid with floor, and every level's needs name real modules and levels.</summary>
    [Test]
    public async Task EveryModuleIsSound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var loader = server.System<MapLoaderSystem>();
        var maps = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            foreach (var module in protos.EnumeratePrototypes<WFHideoutModulePrototype>())
            {
                Assert.That(module.Levels, Is.Not.Empty, $"{module.ID} has no levels.");
                for (var i = 0; i < module.Levels.Count; i++)
                {
                    var level = module.Levels[i];
                    foreach (var (needed, at) in level.Requires)
                    {
                        Assert.That(protos.TryIndex(needed, out var other), Is.True, $"{module.ID} level {i + 1} needs unknown module {needed}.");
                        Assert.That(at, Is.InRange(1, other.Levels.Count), $"{module.ID} level {i + 1} needs {needed} at a level it lacks.");
                        Assert.That(needed.Id, Is.Not.EqualTo(module.ID), $"{module.ID} level {i + 1} needs itself.");
                    }

                    foreach (var grant in level.Grants)
                        Assert.That(protos.HasIndex(grant), Is.True, $"{module.ID} level {i + 1} grants unknown {grant}.");

                    if (level.Prefab is not { } first)
                    {
                        Assert.That(level.Layouts, Is.Empty, $"{module.ID} level {i + 1} has layouts but no prefab.");
                        continue;
                    }

                    if (level.Layouts.Count > 0)
                    {
                        Assert.That(level.Layouts.Select(layout => layout.Prefab), Does.Contain(first),
                            $"{module.ID} level {i + 1}'s prefab isn't among its layouts.");
                    }

                    // The prefab and every layout the stake can be set to.
                    foreach (var prefab in level.Layouts.Select(layout => layout.Prefab).Append(first).Distinct())
                    {
                        Assert.That(loader.TryLoadGrid(map.MapId, prefab, out var grid), Is.True, $"{prefab} failed to load.");
                        var tiles = 0;
                        var enumerator = maps.GetAllTilesEnumerator(grid.Value.Owner, grid.Value.Comp);
                        while (enumerator.MoveNext(out _))
                            tiles++;
                        Assert.That(tiles, Is.GreaterThan(0), $"{prefab} has no floor.");
                        entMan.DeleteEntity(grid.Value.Owner);
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ship landed on the hangar pad is registered and stored; insured, destroyed and claimed, it comes back on the pad
    /// as it was insured; one that only went with the round comes back out of store.
    /// </summary>
    [Test]
    public async Task TheHangarKeepsInsuresAndBringsBackAShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var orbit = layers[^1];
        var outposts = server.System<WFOutpostSystem>();
        var hideouts = server.System<WFHideoutSystem>();
        var transform = server.System<SharedTransformSystem>();
        var bank = server.System<BankSystem>();
        var entry = server.System<WFOrbitEntrySystem>();
        var owner = pair.Player.UserId;
        await Fund(pair, user, 5_000_000);
        await PrepareYard(pair, ground);

        int Balance()
        {
            bank.TryGetBalance(pair.Player, out var balance);
            return balance;
        }

        async Task Settle()
        {
            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(server, () => !hideouts.IsBusy(owner), maxTicks: 900);
            await pair.RunTicksSync(1);
        }

        try
        {
            Entity<WFOutpostComponent> outpost = default;
            Entity<WFLandingPadComponent> pad = default;
            Vector2 padWorld = default;
            await server.WaitAssertion(() =>
            {
                outpost = Found(pair, user, ground);
                Build(pair, user, outpost, Groundworks);
                Build(pair, user, outpost, Hangar);
                UseStake(pair, user, ground, new Vector2i(6, -3));
                Assert.That(hideouts.TryGetHangar(outpost, out pad), Is.True, "The apron came without its beacon.");
                Assert.That(pad.Comp.Size, Is.EqualTo(9));
                padWorld = transform.GetWorldPosition(pad);
            });

            // A lander in orbit over the hangar, the owner's by registration.
            var hull = await BuildLander(pair, entMan.GetComponent<MapComponent>(orbit).MapId, padWorld - new Vector2(3.5f, 4.5f) + new Vector2(6f, 3f));
            var console = EntityUid.Invalid;
            await server.WaitPost(() =>
            {
                console = FindShuttleConsole(entMan, hull);
                server.System<WFShipAccessServerSystem>().SetupRegisteredShip(hull, "Tester", new[] { owner });
                // A faction hangar only takes stock shipyard hulls.
                entMan.AddComponent<VesselComponent>(hull).VesselId = "Guppy";

                // Cargo isn't kept; a strongbox and what it holds are.
                entMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(hull, 2.5f, 6.5f));
                var box = entMan.SpawnEntity("WFShipStrongbox", new EntityCoordinates(hull, 4.5f, 6.5f));
                var kept = entMan.SpawnEntity("Wrench", new EntityCoordinates(hull, 4.5f, 5.5f));
                Assert.That(server.System<SharedStorageSystem>().Insert(box, kept, out _, playSound: false), Is.True, "The strongbox took nothing.");
            });
            await pair.RunTicksSync(5);
            await server.WaitAssertion(() =>
                Assert.That(entry.TryEnterAtmosphere(console, false, out var reason, user), Is.True, $"The hangar refused the hull: {reason}."));
            await pair.RunTicksSync(pair.SecondsToTicks(12f));
            await Settle();

            await server.WaitAssertion(() =>
            {
                Assert.That(pad.Comp.Parked, Is.EqualTo(hull), "The hull never came down onto the hangar.");
                Assert.That(hideouts.GetStored(owner), Is.Null, "An unregistered ship was stored.");
                Assert.That(hideouts.TryRegister(user, outpost, out var reason), Is.True, $"Registering was refused: {reason}.");
            });
            await Settle();

            var before = 0;
            await server.WaitAssertion(() =>
            {
                var stored = hideouts.GetStored(owner);
                Assert.That(stored, Is.Not.Null, "The registered ship wasn't stored.");
                Assert.That(stored.Value, Is.GreaterThan(0));
                Assert.That(hideouts.TryGetShip(owner, out var live) && live == hull, Is.True);
                before = Balance();
                Assert.That(hideouts.TryInsure(user, outpost, out var reason), Is.True, $"Insuring was refused: {reason}.");
                Assert.That(Balance(), Is.LessThan(before), "The premium was paid.");
            });
            await Settle();

            await server.WaitAssertion(() =>
            {
                Assert.That(hideouts.GetStored(owner).Insured, Is.True, "The policy wasn't stored.");
                // Something added after the policy, and then the ship is destroyed.
                entMan.SpawnEntity("Crowbar", new EntityCoordinates(hull, 3.5f, 3.5f));
                entMan.DeleteEntity(hull);
                hideouts.Lose(owner);
            });
            await Settle();

            await server.WaitAssertion(() =>
            {
                Assert.That(hideouts.GetStored(owner).Lost, Is.True);
                Assert.That(hideouts.TryRecall(user, outpost, out _), Is.False, "A destroyed ship came out of store.");
                before = Balance();
                Assert.That(hideouts.TryClaim(user, outpost, out var reason), Is.True, $"The claim was refused: {reason}.");
                Assert.That(Balance(), Is.LessThan(before), "The deductible was paid.");
            });
            await Settle();

            var second = EntityUid.Invalid;
            await server.WaitAssertion(() =>
            {
                Assert.That(hideouts.TryGetShip(owner, out second), Is.True, "The claim brought no ship.");
                Assert.That(pad.Comp.Parked, Is.EqualTo(second), "The claimed ship isn't on the hangar pad.");
                Assert.That(entMan.GetComponent<TransformComponent>(second).MapUid, Is.EqualTo(ground));
                Assert.That(Children(entMan, second).Any(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "ComputerShuttle"),
                    Is.True, "The claimed ship lost its console.");
                Assert.That(Children(entMan, second).Any(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "Crowbar"),
                    Is.False, "The claimed ship has what was added after the policy.");
                Assert.That(Children(entMan, second).Any(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "CrateGenericSteel"),
                    Is.False, "A crate came back with the ship.");
                var strongbox = Children(entMan, second).Single(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WFShipStrongbox");
                var held = entMan.GetComponent<StorageComponent>(strongbox).Container.ContainedEntities;
                Assert.That(held.Count, Is.EqualTo(1), "The strongbox lost what it held.");
                Assert.That(entMan.HasComponent<WFShipSaveOriginComponent>(held[0]), Is.True, "What the strongbox kept isn't stamped.");
                var stored = hideouts.GetStored(owner);
                Assert.That(stored.Lost, Is.False);
                Assert.That(stored.Insured, Is.False, "The claim didn't use the policy up.");

                // Gone with the round, not destroyed: it stays in store.
                entMan.DeleteEntity(second);
            });
            await Settle();

            await server.WaitAssertion(() =>
            {
                Assert.That(hideouts.GetStored(owner).Lost, Is.False, "A ship that went with the round counted as destroyed.");
                Assert.That(hideouts.TryRecall(user, outpost, out var reason), Is.True, $"The recall was refused: {reason}.");
            });
            await Settle();

            await server.WaitAssertion(() =>
            {
                Assert.That(hideouts.TryGetShip(owner, out var third), Is.True, "The recall brought no ship.");
                Assert.That(pad.Comp.Parked, Is.EqualTo(third));
                Assert.That(hideouts.TryUnregister(user, outpost, out _), Is.True);
            });
            await Settle();
            await server.WaitAssertion(() => Assert.That(hideouts.GetStored(owner), Is.Null, "Giving the ship up left it stored."));
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>No outpost is founded on a world that forbids them.</summary>
    [Test]
    public async Task CarcinomaTakesNoOutposts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var surface in protos.EnumeratePrototypes<Content.Shared._WF.Planets.WFPlanetSurfacePrototype>())
            {
                var forbidden = surface.ID == "WFSurfaceCarcinoma";
                Assert.That(surface.Outposts, Is.EqualTo(!forbidden), $"{surface.ID} has the wrong outposts flag.");
                if (forbidden && surface.Pois is { } table)
                    Assert.That(protos.Index(table).OutpostLots, Is.EqualTo(0), "Carcinoma keeps outpost lots.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
