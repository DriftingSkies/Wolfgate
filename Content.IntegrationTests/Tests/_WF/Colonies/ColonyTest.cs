#nullable enable
#pragma warning disable RA0002 // The test reads the colony's rates off its component.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._NF.Bank;
using Content.Server._WF.AdsIncursion.Systems;
using Content.Server._WF.Colonies;
using Content.Server._WF.Companies;
using Content.Server._WF.Contracts;
using Content.Server._WF.Defcon;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Husbandry;
using Content.Server._WF.OutpostIndustry;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.PlanetMap;
using Content.Server._WF.Planets;
using Content.Server._WF.SectorControl.Systems;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Preferences.Managers;
using Content.Shared._Mono.VendingMachine;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.Companies;
using Content.Shared._WF.Defcon;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.OutpostRaids;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.PlanetMap;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.TradeGoods;
using Content.Shared.GameTicking;
using Content.Shared.Interaction.Events;
using Content.Shared.Materials;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.VendingMachines;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.UnitTesting;
using static Content.IntegrationTests.Tests._WF.Outposts.OutpostFoundingTest;
using static Content.IntegrationTests.Tests._WF.OutpostRaids.RaidArrivalTest;

namespace Content.IntegrationTests.Tests._WF.Colonies;

/// <summary>Colonies: world scores and tiers, the charter and its succession, the colony's buildings and the orbital yard.</summary>
[TestFixture]
[TestOf(typeof(WFColonySystem))]
public sealed class ColonyTest
{
    /// <summary>Two holders' points add up to one world's tier, and a month of decay takes it back down.</summary>
    [Test]
    public async Task ScoresAddUpAndDecay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var colonies = server.System<WFColonySystem>();
        const string world = "WFTestWorldScores";
        await WaitFor(pair, () => colonies.Loaded);

        await server.WaitAssertion(() =>
        {
            colonies.AddPoints(world, "c900001", "First", 60);
            Assert.That(colonies.Tier(world), Is.EqualTo(WFWorldTier.None), "One holder's 60 points shouldn't settle a world.");
            colonies.AddPoints(world, "c900002", "Second", 60);
            Assert.That(colonies.Tier(world), Is.EqualTo(WFWorldTier.Settled), "Two holders' points should settle it together.");
            Assert.That(colonies.Share(world, "c900001"), Is.EqualTo(0.5f).Within(0.001f));

            colonies.AddPoints(world, "c900001", "First", -1000);
            Assert.That(colonies.Points(world, "c900001"), Is.Zero, "A score never goes below zero.");
            colonies.AddPoints(world, "c900001", "First", 60);

            colonies.Decay(DateTime.UtcNow.AddDays(30));
            Assert.That(colonies.Total(world), Is.EqualTo(120 * Math.Pow(0.95, 30)).Within(1.0));
            Assert.That(colonies.Tier(world), Is.EqualTo(WFWorldTier.None), "A month of decay should unsettle the world.");

            // A world with a city on it never reads below Settled, whatever its score.
            Assert.That(colonies.BaseTier("WFSurfaceAsclepiu"), Is.EqualTo(WFWorldTier.Settled));
            Assert.That(colonies.Tier("WFSurfaceAsclepiu"), Is.GreaterThanOrEqualTo(WFWorldTier.Settled));
            Assert.That(colonies.BaseTier(world), Is.EqualTo(WFWorldTier.None));

            // A world gets the seat of the highest tier it has reached.
            Assert.That(colonies.SeatFor(WFWorldTier.None), Is.Null, "A wild world has no seat.");
            Assert.That(colonies.SeatFor(WFWorldTier.Settled)?.ID, Is.EqualTo("WFColonySeatField"));
            Assert.That(colonies.SeatFor(WFWorldTier.Colony)?.ID, Is.EqualTo("WFColonySeatDepot"));
            Assert.That(colonies.SeatFor(WFWorldTier.Dominion)?.ID, Is.EqualTo("WFColonySeatHall"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The largest company takes the charter, a rival with a quarter more points takes it after three daily checks, and the
    /// governor at Dominion lays down a yard build: its stages take crates, the build survives a reload, and its completion
    /// spawns the hull and grants the licence. Another company has no berth until the governor opens it one, and a shipyard
    /// hull is never a yard design.
    /// </summary>
    [Test]
    public async Task CharterPassesAndYardBuildGrantsLicence()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var colonies = server.System<WFColonySystem>();
        var yard = server.System<WFColonyYardSystem>();
        var companies = server.System<WFCompanySystem>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        const string world = "WFTestWorldCharter";

        // The pool's cvars already let test players found companies for free.
        await server.WaitPost(() => server.CfgMan.SetCVar(ColonyCVars.CharterMembers, 1));
        var players = await server.AddDummySessions(2);
        await WaitFor(pair, () => companies.Loaded && colonies.Loaded && yard.Loaded
            && players.All(player => player.Status == SessionStatus.InGame && preferences.HavePreferencesLoaded(player)));
        await server.WaitPost(() =>
        {
            for (var i = 0; i < players.Length; i++)
            {
                var prefs = preferences.GetPreferences(players[i].UserId);
                preferences.SetProfile(players[i].UserId, prefs.SelectedCharacterIndex,
                    ((HumanoidCharacterProfile) prefs.SelectedCharacter).WithName($"Colonist {i}")).Wait();
            }
        });

        int alpha = 0, beta = 0;
        try
        {
            await Request(pair, companies, players[0], new(WFCompanyAction.Create) { Name = "Alpha Colonial", Tag = "ALPH" });
            await Request(pair, companies, players[1], new(WFCompanyAction.Create) { Name = "Beta Colonial", Tag = "BETA" });
            await server.WaitAssertion(() =>
            {
                alpha = companies.FindCompany("ALPH") ?? 0;
                beta = companies.FindCompany("BETA") ?? 0;
                Assert.That(alpha, Is.Not.Zero, "The first company wasn't made.");
                Assert.That(beta, Is.Not.Zero, "The second company wasn't made.");
            });

            var map = await pair.CreateTestMap();
            EntityUid platform = default;
            await server.WaitAssertion(() =>
            {
                var now = DateTime.UtcNow;
                colonies.AddPoints(world, WFColonySystem.CompanyKey(alpha), "Alpha Colonial", 2500);
                Assert.That(colonies.Tier(world), Is.EqualTo(WFWorldTier.Charter));
                colonies.CheckCharter(world, now, false);
                Assert.That(colonies.Governor(world), Is.EqualTo(alpha), "The only company should take the charter.");

                colonies.AddPoints(world, WFColonySystem.CompanyKey(beta), "Beta Colonial", 3500);
                Assert.That(colonies.Tier(world), Is.EqualTo(WFWorldTier.Dominion));
                for (var day = 1; day <= 2; day++)
                {
                    colonies.CheckCharter(world, now.AddDays(day), true);
                    Assert.That(colonies.Governor(world), Is.EqualTo(alpha), $"The charter passed after {day} checks.");
                }

                colonies.CheckCharter(world, now.AddDays(3), true);
                Assert.That(colonies.Governor(world), Is.EqualTo(beta), "A rival leading by a quarter for three checks should take the charter.");

                Assert.That(yard.Refusal(world, alpha), Is.Not.Null, "A company with no berth shouldn't lay down builds.");
                Assert.That(yard.Refusal(world, beta), Is.Null);

                // Berths: the governor, and nobody else, opens one to a company holding enough of the world.
                var rival = WFColonySystem.CompanyKey(alpha);
                Assert.That(colonies.SetBerth(world, players[0], rival, true), Is.False, "Only the governor opens berths.");
                Assert.That(colonies.SetBerth(world, players[1], rival, true), Is.True);
                Assert.That(yard.Refusal(world, alpha), Is.Null, "A company with an open berth may lay down builds.");
                Assert.That(colonies.SetBerth(world, players[1], rival, false), Is.True);
                Assert.That(yard.Refusal(world, alpha), Is.Not.Null);
                var shipyardHull = server.ProtoMan.EnumeratePrototypes<VesselPrototype>().First(vessel => vessel.Purchasable);
                Assert.That(yard.IsYardVessel(shipyardHull), Is.False, "A shipyard hull is never a yard design.");

                platform = yard.BuildPlatform(world, map.MapId, new Vector2(0f, 200f));
                Assert.That(yard.TryOrder(world, beta, "WFYardDesignPacem", out var reason), Is.True, reason);
                for (var i = 0; i < 3; i++)
                    Assert.That(yard.TryFeed(world, WFColonyYardSystem.ComponentsGood, beta, out _), Is.True);
                for (var i = 0; i < 5; i++)
                    Assert.That(yard.TryFeed(world, WFColonyYardSystem.MetalsGood, beta, out _), Is.True);
                Assert.That(yard.TryFeed(world, WFColonyYardSystem.ComponentsGood, beta, out _), Is.False, "A stage's crates are capped.");
            });

            // The build is saved: a fresh read from the database finds it where it was.
            await Run(pair, () => yard.ReloadForTest());
            await server.WaitAssertion(() =>
            {
                var job = yard.Jobs(world).Single();
                Assert.That((job.Stage, job.Components, job.Metals), Is.EqualTo((0, 3, 5)));
                Assert.That(job.CompanyId, Is.EqualTo(beta));

                yard.Work(world, platform, 3600f);
                Assert.That(yard.Jobs(world).Single().Stage, Is.EqualTo(1), "The cranes should finish a fed stage.");

                // The build stands in its berth as a frame: its deck goes down first, then its hull goes up.
                var mapSystem = server.System<SharedMapSystem>();
                var xforms = server.System<SharedTransformSystem>();
                var frame = yard.FrameOf(world, "WFYardDesignPacem");
                Assert.That(frame, Is.Not.Null, "A build should stand in its berth as a frame.");
                var frameGrid = entMan.GetComponent<MapGridComponent>(frame!.Value);
                var deck = mapSystem.GetAllTiles(frame.Value, frameGrid).Count();
                Assert.That(deck, Is.GreaterThan(1), "A third of the work should have laid some of the deck.");
                var berth = xforms.GetWorldPosition(frame.Value);
                Feed(yard, world, beta, 4, 6);
                yard.Work(world, platform, 3600f);
                Assert.That(yard.FrameOf(world, "WFYardDesignPacem"), Is.EqualTo(frame), "A frame stands until its build is done.");
                Assert.That(mapSystem.GetAllTiles(frame.Value, frameGrid).Count(), Is.GreaterThan(deck), "More work should lay more deck.");
                var plating = 0;
                var pieces = entMan.GetComponent<TransformComponent>(frame.Value).ChildEnumerator;
                while (pieces.MoveNext(out _))
                    plating++;
                Assert.That(plating, Is.Positive, "With the deck down, the hull should be going up.");
                Feed(yard, world, beta, 5, 3);
                yard.Work(world, platform, 3600f);
                Assert.That(entMan.Deleted(frame.Value), Is.True, "The frame should make way for the finished vessel.");

                Assert.That(yard.Jobs(world), Is.Empty, "The finished build should leave the berth.");
                Assert.That(companies.HasLicence(beta, "Pacem"), Is.True, "The first completion should grant the licence.");
                var hulls = new List<EntityUid>();
                var query = entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var xform))
                {
                    if (xform.MapID == map.MapId && uid != platform && uid != map.Grid.Owner)
                        hulls.Add(uid);
                }

                Assert.That(hulls, Has.Count.EqualTo(1), "The hull should spawn by the platform.");
                Assert.That((xforms.GetWorldPosition(hulls[0]) - berth).Length(), Is.LessThan(0.01f), "The vessel should stand where its frame stood.");
                foreach (var uid in hulls.Append(platform))
                    entMan.DeleteEntity(uid);
            });
        }
        finally
        {
            // Company tests expect a server with no companies of others' making.
            await server.WaitPost(() =>
            {
                foreach (var id in new[] { alpha, beta })
                {
                    if (id != 0)
                        companies.AdminDisband(id);
                }

                server.CfgMan.SetCVar(ColonyCVars.CharterMembers, ColonyCVars.CharterMembers.DefaultValue);
            });
            await Run(pair, () => companies.Flush());
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// At Colony tier the world's body becomes a colony station: a goat on an outpost works the Agriculture pen, an
    /// extractor over a uranium seam runs Extraction while someone is home, the outpost trades on the colony's shelf, and
    /// an egg sold at its exchange chute goes on that shelf. A starving goat works nothing, and an ore silo's Refining
    /// draws down the ore in it and stops empty.
    /// </summary>
    [Test]
    public async Task BuildingsWorkTheColony()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var colonies = server.System<WFColonySystem>();
        await Configure(server);
        // A raid would scare the goat off; this test is about the colony.
        await server.WaitPost(() => server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, false));
        var (layers, user) = await Setup(pair);
        EntityUid outpost = default;
        EntityUid body = default;
        EntityUid extractor = default;
        EntityUid goat = default;
        EntityUid silo = default;
        var world = string.Empty;
        var market = server.System<WFTradeMarketSystem>();
        var materials = server.System<SharedMaterialStorageSystem>();
        // The iron ore, in material units, that a crate of Metals takes, and what the silo starts with.
        var perCrate = 0.0;
        var ore = 0;

        try
        {
            await server.WaitAssertion(() =>
            {
                outpost = Found(server, layers[0], user);
                Assert.That(colonies.TryWorldOf(layers[0], out world, out var network), Is.True);
                var owner = entMan.GetComponent<WFOutpostComponent>(outpost).Owner;
                colonies.AddPoints(world, WFColonySystem.UserKey(owner), "Founder", 600);
                Assert.That(colonies.Tier(world), Is.GreaterThanOrEqualTo(WFWorldTier.Colony));
                body = colonies.ColonyBody(network);

                var console = entMan.GetComponent<WFOutpostComponent>(outpost).Console;
                var at = entMan.GetComponent<TransformComponent>(console).Coordinates;
                goat = entMan.SpawnEntity("MobGoat", at.Offset(new Vector2(1f, 0f)));
                entMan.RemoveComponent<HTNComponent>(goat);
                server.System<WFHusbandrySystem>().MakeLivestock(goat);

                // An extractor over a uranium seam, run off no grid so only someone being home gates it.
                extractor = entMan.SpawnEntity("WFColonyExtractor", at.Offset(new Vector2(-1f, 0f)));
                entMan.GetComponent<ApcPowerReceiverComponent>(extractor).NeedsPower = false;
                var xforms = server.System<SharedTransformSystem>();
                entMan.SpawnEntity("WFColonyDepositUranium", new EntityCoordinates(layers[0], xforms.GetWorldPosition(extractor)));
                Assert.That(colonies.OnDeposit(extractor, "Uranium"), Is.True);
                Assert.That(colonies.OnDeposit(extractor, "Volatile"), Is.False);

                // An ore silo holding enough iron ore for 0.15 crates of Metals.
                silo = entMan.SpawnEntity("MachineMaterialSilo", at.Offset(new Vector2(1f, 1f)));
                perCrate = market.Profile(body).MaterialCrateValue / server.ProtoMan.Index<MaterialPrototype>(RawIron).Price;
                ore = (int) (perCrate * 0.15);
                Assert.That(materials.TryChangeMaterialAmount(silo, RawIron, ore), Is.True);
                xforms.SetCoordinates(user, at.Offset(new Vector2(0f, 1f)));
            });

            // The power net settles every half second.
            await pair.RunTicksSync(pair.SecondsToTicks(1));
            await server.WaitAssertion(() =>
            {
                var outpostComp = entMan.GetComponent<WFOutpostComponent>(outpost);
                Assert.That(server.System<WFRaidDirectorSystem>().SomeoneHome((outpost, outpostComp, entMan.GetComponent<MapGridComponent>(outpost))),
                    Is.True, "The founder should be home on the outpost.");
                Assert.That(server.System<PowerReceiverSystem>().IsPowered(extractor), Is.True, "The extractor should run off no grid.");
                Assert.That(colonies.OnDeposit(extractor, "Uranium"), Is.True, "The extractor should still stand on the seam.");
                // The test server sits in the lobby, where the colonies don't tick on their own.
                colonies.Tick(10f);
                Assert.That(entMan.TryGetComponent<WFColonyComponent>(body, out var colony), Is.True, "The world's body should be a colony station.");
                Assert.That(colony!.Industries.GetValueOrDefault("WFTradeIndustryAgriculture")?.Values.Sum() ?? 0f, Is.GreaterThan(0f),
                    "The goat should work the Agriculture pen.");
                Assert.That(colony.Goods, Is.Empty, "What an animal yields is sold at the exchange, not made on its own.");

                // An egg sold at the exchange chute goes on the shelf as its worth of Supplies and scores for the seller.
                var at = entMan.GetComponent<TransformComponent>(outpostComp.Console).Coordinates;
                var chute = entMan.SpawnEntity(WFColonySystem.ExchangeChute, at.Offset(new Vector2(-1f, 1f)));
                var egg = entMan.SpawnEntity("FoodEgg", at);
                var supplies = market.Row(body, WFColonySystem.Supplies);
                market.SetVendorStock(body, WFColonySystem.Supplies, 2);
                var shelf = supplies.Stock;
                var founder = WFColonySystem.UserKey(outpostComp.Owner);
                var score = colonies.Points(world, founder);
                Assert.That(colonies.TrySellProduce(chute, (body, colony), egg, user, out var paid), Is.True, "The exchange should buy an egg.");
                Assert.That(paid, Is.Positive);
                Assert.That(supplies.Stock, Is.GreaterThan(shelf), "The egg should go on the shelf as Supplies.");
                Assert.That(colonies.Points(world, founder), Is.GreaterThan(score), "Produce sold should score for the seller's holder.");
                Assert.That(entMan.IsQueuedForDeletion(egg), Is.True, "The exchange keeps what it buys.");
                var wrench = entMan.SpawnEntity("Wrench", at);
                Assert.That(colonies.TrySellProduce(chute, (body, colony), wrench, user, out _), Is.False, "A wrench is no produce.");
                entMan.DeleteEntity(wrench);
                entMan.DeleteEntity(chute);
                Assert.That(colony.Industries.GetValueOrDefault("WFTradeIndustryExtraction")?.Values.Sum() ?? 0f, Is.GreaterThan(0f),
                    "The extractor on the seam should run Extraction while someone is home.");
                Assert.That(server.ProtoMan.Index(WFColonySystem.ColonyRole).Industries, Is.Empty,
                    "A colony's industries are its own, not every colony's.");
                Assert.That(entMan.GetComponent<WFTradeMarketLinkComponent>(outpost).Market, Is.EqualTo(body),
                    "The outpost should trade on the colony's shelf.");

                // A starving goat works nothing until it is fed.
                var hungers = server.System<HungerSystem>();
                var hunger = entMan.GetComponent<HungerComponent>(goat);
                hungers.SetHunger(goat, hunger.Thresholds[HungerThreshold.Starving], hunger);
                colony.NextScan = server.Timing.CurTime;
                colonies.Tick(1f);
                Assert.That(colony.Industries.ContainsKey(Agriculture), Is.False, "A starving goat should work nothing.");
                hungers.SetHunger(goat, hunger.Thresholds[HungerThreshold.Okay], hunger);
                colony.NextScan = server.Timing.CurTime;
                colonies.Tick(1f);
                Assert.That(colony.Industries.ContainsKey(Agriculture), Is.True, "A fed goat should work again.");

                // The silo refines the ore in it. The ledger makes the crates; the colony draws their worth of ore, takes
                // back what the ore didn't cover, and an empty silo stops.
                Assert.That(colony.Industries.ContainsKey(Refining), Is.True, "A silo holding ore should run Refining.");
                var row = market.Row(body, Metals);
                market.AddStock(body, Metals, 0.1f);
                var stock = row.Stock;
                var ran = new WFTradeIndustryRanEvent(Refining, new Dictionary<string, float> { [Metals] = 0.1f }, false);
                entMan.EventBus.RaiseLocalEvent(body, ref ran);
                Assert.That(materials.GetMaterialAmount(silo, RawIron), Is.EqualTo(ore - perCrate * 0.1).Within(2),
                    "A tenth of a crate should take a tenth of a crate's ore.");
                Assert.That(row.Stock, Is.EqualTo(stock).Within(0.0001f), "Metals the ore covered stay on the shelf.");

                market.AddStock(body, Metals, 0.1f);
                stock = row.Stock;
                entMan.EventBus.RaiseLocalEvent(body, ref ran);
                Assert.That(materials.GetMaterialAmount(silo, RawIron), Is.Zero, "The silo should be worked empty.");
                Assert.That(row.Stock, Is.EqualTo(stock - 0.05f).Within(0.005f), "Metals the ore didn't cover come back off the shelf.");
                colonies.Tick(1f);
                Assert.That(colony.Industries.ContainsKey(Refining), Is.False, "An empty silo should stop Refining at once.");
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, OutpostRaidCVars.Enabled.DefaultValue));
            await LeaveAndTeardown(pair, user, layers);
            await ClearWorld(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A yard platform laid in a world's orbit is saved as the world's, and after a restart it is placed again where it
    /// stood with its console, its chute and its four cranes linked to the console.
    /// </summary>
    [Test]
    public async Task YardPlatformSavesAndComesBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var yard = server.System<WFColonyYardSystem>();
        var xforms = server.System<SharedTransformSystem>();
        await PlanetFixture.EnableFeature(pair);
        var layers = await PlanetFixture.BuildStandalone(pair);
        var world = string.Empty;
        var origin = new Vector2(40f, -20f);

        try
        {
            await WaitFor(pair, () => yard.Loaded);
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<WFColonySystem>().TryWorldOf(layers[0], out world, out var network), Is.True);
                var orbit = entMan.GetComponent<WFPlanetNetworkComponent>(network).OrbitMap;
                var platform = yard.FoundYard(world, entMan.GetComponent<TransformComponent>(orbit).MapID, origin);
                Assert.That(yard.Platform(world), Is.EqualTo(platform));
            });

            await Run(pair, () => yard.Flush());
            await server.WaitPost(() => entMan.DeleteEntity(yard.Platform(world)!.Value));
            await Run(pair, () => yard.ReloadForTest());
            await server.WaitAssertion(() =>
            {
                Assert.That(yard.HasYard(world), Is.True, "The yard should come back from the database.");
                var platform = yard.PlaceForTest(world);
                Assert.That(platform, Is.Not.Null, "The saved platform should load.");
                Assert.That(Vector2.Distance(xforms.GetWorldPosition(platform!.Value), origin), Is.LessThan(0.01f), "The platform should stand where it was saved.");

                EntityUid? console = null;
                var cranes = new List<WFDrydockCraneComponent>();
                var chutes = 0;
                var children = entMan.GetComponent<TransformComponent>(platform.Value).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    if (entMan.TryGetComponent<WFColonyYardComponent>(child, out var yardConsole))
                    {
                        Assert.That(yardConsole.World, Is.EqualTo(world));
                        console = child;
                    }

                    if (entMan.TryGetComponent<WFDrydockCraneComponent>(child, out var crane))
                        cranes.Add(crane);
                    if (entMan.HasComponent<WFColonyYardChuteComponent>(child))
                        chutes++;
                }

                Assert.That(console, Is.Not.Null, "The platform should keep its console.");
                Assert.That(chutes, Is.EqualTo(1), "The platform should keep its chute.");
                Assert.That(cranes, Has.Count.EqualTo(4));
                Assert.That(cranes.Select(crane => crane.LinkedConsole), Is.All.EqualTo(console), "The cranes should work for the platform's console.");
                yard.Demolish(world);
            });

            await Run(pair, () => yard.Flush());
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A Colony world trades and pays: an hour home earns points; a goat's wool is owed to its holder only and paid into
    /// their bank; the exchange chute sells onto the colony's shelf and scores a crate hauled from off the world, its
    /// vendor sells off that shelf, and the Market app lists the colony. Short of Supplies, the colony is home to the
    /// Supplies contractor. What it is owed lands on a ready trade hub, and a raider blockade holds it back and raises
    /// DEFCON.
    /// </summary>
    [Test]
    public async Task ColonyTradesPaysAndImports()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var colonies = server.System<WFColonySystem>();
        var market = server.System<WFTradeMarketSystem>();
        var bank = server.System<BankSystem>();
        await Configure(server);
        // A raid would scare the goat off; this test is about the colony.
        await server.WaitPost(() => server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, false));
        var (layers, user) = await Setup(pair);
        var away = await pair.CreateTestMap();
        var world = string.Empty;
        var founder = string.Empty;
        EntityUid outpost = default;
        EntityUid body = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                outpost = Found(server, layers[0], user);
                Assert.That(colonies.TryWorldOf(layers[0], out world, out var network), Is.True);
                founder = WFColonySystem.UserKey(entMan.GetComponent<WFOutpostComponent>(outpost).Owner);
                colonies.AddPoints(world, founder, "Founder", 600);
                colonies.AddPoints(world, Bystander, "Bystander", 10);
                body = colonies.ColonyBody(network);

                DeployHub(server, user, outpost);
                var goat = entMan.SpawnEntity("MobGoat", Tile(server, outpost, 0, 2));
                entMan.RemoveComponent<HTNComponent>(goat);
                server.System<WFHusbandrySystem>().MakeLivestock(goat);
                entMan.EnsureComponent<BankAccountComponent>(user);
            });

            // The power net settles every half second.
            await pair.RunTicksSync(pair.SecondsToTicks(1));
            await server.WaitAssertion(() =>
            {
                var outpostComp = entMan.GetComponent<WFOutpostComponent>(outpost);
                Assert.That(server.System<WFOutpostSystem>().GetHubStatus((outpost, outpostComp), out _), Is.EqualTo(WFOutpostHubStatus.Ready));

                // The test server sits in the lobby, where the colonies don't tick on their own.
                var points = colonies.Points(world, founder);
                colonies.Tick(10 * 3600f);
                Assert.That(colonies.Points(world, founder), Is.GreaterThanOrEqualTo(points + 10 * server.CfgMan.GetCVar(ColonyCVars.HomePoints) - 0.01),
                    "Ten hours home should earn the founder ten hours' points.");

                var colony = entMan.GetComponent<WFColonyComponent>(body);
                // The ledger runs the goat's pen; the colony books what it made to the goat's holder.
                var made = new WFTradeIndustryRanEvent(Agriculture, new Dictionary<string, float> { ["TradeGoodFabric"] = 0.5f }, false);
                entMan.EventBus.RaiseLocalEvent(body, ref made);
                Assert.That(colony.Owed.Keys, Is.EquivalentTo(new[] { founder }), "Only the goat's holder should be owed for what it makes.");
                var owed = (int) Math.Floor(colony.Owed[founder]);
                Assert.That(owed, Is.Positive, "The goat should have made something worth paying for.");
                Assert.That(bank.TryGetBalance(user, out var balance), Is.True);
                colony.NextScan = server.Timing.CurTime;
                colonies.Tick(1f);
                Assert.That(colony.Owed[founder], Is.LessThan(1f), "The colony should pay what it owes.");
                Assert.That(bank.TryGetBalance(user, out var paid) && paid == balance + owed, Is.True, "The founder should be paid into their bank.");

                // The exchange: its chute and vendor on the outpost work the colony's one shelf.
                var chute = entMan.SpawnEntity(WFColonySystem.ExchangeChute, Tile(server, outpost, 2, 0));
                var vendor = entMan.SpawnEntity("WFColonyExchangeVendor", Tile(server, outpost, 2, 2));
                Assert.That(market.MarketOf(chute), Is.EqualTo(body));
                Assert.That(market.MarketOf(vendor), Is.EqualTo(body));
                market.SetVendorStock(body, WFColonySystem.Supplies, 10);
                var crate = entMan.SpawnEntity(WFColonySystem.Supplies, Tile(server, outpost, 0, 1));
                var origin = entMan.GetComponent<WFTradeOriginComponent>(crate);
                origin.Pending = false;
                origin.Point = new MapCoordinates(Vector2.Zero, away.MapId);
                points = colonies.Points(world, founder);
                market.Sell(chute, crate, user);
                Assert.That(market.VendorStock(body, WFColonySystem.Supplies), Is.EqualTo(11), "A sale should go onto the colony's shelf.");
                Assert.That(colonies.Points(world, founder), Is.EqualTo(points + 1).Within(0.001), "A crate hauled in should score for the seller.");
                Assert.That(entMan.GetComponent<VendingMachineComponent>(vendor).Inventory[WFColonySystem.Supplies].Amount, Is.EqualTo(11u),
                    "The vendor should show the colony's shelf.");
                var bought = entMan.SpawnEntity(WFColonySystem.Supplies, Tile(server, outpost, 0, 1));
                entMan.AddComponent<VendingMachinePurchaseComponent>(bought);
                Assert.That(market.VendorStock(body, WFColonySystem.Supplies), Is.EqualTo(10), "A purchase should come off the colony's shelf.");
                entMan.DeleteEntity(crate);
                entMan.DeleteEntity(bought);

                var pda = entMan.SpawnEntity(null, new EntityCoordinates(user, Vector2.Zero));
                var app = server.System<WFMarketCartridgeSystem>().BuildState(pda);
                Assert.That(app.Stations.Select(station => station.Name), Does.Contain(entMan.GetComponent<MetaDataComponent>(body).EntityName),
                    "The Market app should list the colony.");
                entMan.DeleteEntity(pda);

                // Short of Supplies, the colony posts as the Supplies contractor.
                var contracts = server.System<WFContractSystem>();
                market.SetVendorStock(body, WFColonySystem.Supplies, 0);
                Assert.That(contracts.ContractorHomes().GetValueOrDefault(SuppliesContractor), Is.EqualTo(body),
                    "A colony short of Supplies should be home to its contractor.");
                market.SetVendorStock(body, WFColonySystem.Supplies, (int) market.Profile(body).StockTarget);
                Assert.That(contracts.ContractorHomes().ContainsKey(SuppliesContractor), Is.False, "A supplied colony posts nothing.");

                // Imports: what the colony is owed lands on the hub, unless raiders blockade it.
                market.SetVendorStock(body, WFColonySystem.Supplies, 0);
                var row = market.Row(body, WFColonySystem.Supplies);
                row.Inbound = 3f;
                var raiders = Blockade(entMan, body);
                colony.NextImport = TimeSpan.Zero;
                colonies.Tick(1f);
                Assert.That(colony.Blockaded, Is.True, "Raiders on its stops should blockade the colony.");
                Assert.That(row.Inbound, Is.EqualTo(3f), "A blockade should hold the imports back.");
                Assert.That(Drops(entMan), Is.Empty);

                // DEFCON scores encounters by their prototype, which the stand-in raiders lack; the colony stays
                // blockaded until its next tick.
                entMan.DeleteEntity(raiders);
                var name = entMan.GetComponent<MetaDataComponent>(body).EntityName;
                Assert.That(DefconReasons(server).Any(reason => reason.Contains(name)), Is.True, "A blockaded colony should raise DEFCON.");
                colony.NextImport = TimeSpan.Zero;
                colonies.Tick(1f);
                Assert.That(colony.Blockaded, Is.False);
                Assert.That(row.Inbound, Is.LessThan(1f), "The import should unload what the colony is owed.");
                Assert.That(market.VendorStock(body, WFColonySystem.Supplies), Is.GreaterThanOrEqualTo(3));
                Assert.That(Drops(entMan), Is.EqualTo(new[] { WFColonySystem.ImportPod.Id }), "The import should come down on the hub.");
                Assert.That(DefconReasons(server).Any(reason => reason.Contains(name)), Is.False);
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, OutpostRaidCVars.Enabled.DefaultValue));
            await LeaveAndTeardown(pair, user, layers);
            await ClearWorld(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A Colony world on the sector map holds its cell as the colonies: the ADS leaves it alone, raids come more often, and
    /// a crewed ship parked there can't retake it. A blockading hold next door takes it while nobody is home, which
    /// blockades the colony and raises DEFCON; once the hold is broken the colony takes its cell back.
    /// </summary>
    [Test]
    public async Task ColonyHoldsItsCellUntilAHoldBlockadesIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var colonies = server.System<WFColonySystem>();
        var territory = server.System<WFSectorTerritorySystem>();
        var ticker = server.System<GameTicker>();
        var xforms = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();
        await Configure(server);
        await server.WaitPost(() => server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, false));
        await PlanetFixture.EnableFeature(pair);
        await server.WaitPost(() => server.CfgMan.SetCVar(OutpostCVars.Enabled, true));
        var (layers, body, bodyMap) = await PlanetFixture.BuildOwnedStack(pair);
        var user = await PrepareSite(pair, layers[0]);
        var sectorMap = MapId.Nullspace;
        var map = MapId.Nullspace;
        var world = string.Empty;
        EntityUid outpost = default;
        EntityUid? ship = null;

        try
        {
            await server.WaitAssertion(() =>
            {
                // The body's map stands in for the sector map.
                sectorMap = ticker.DefaultMap;
                map = entMan.GetComponent<MapComponent>(bodyMap).MapId;
                SetDefaultMap.Invoke(ticker, new object[] { map });
                Assert.That(territory.Enabled && territory.SectorMap == map, Is.True);
                // Out of the sector's protected core, where nothing is claimed.
                xforms.SetCoordinates(body, new EntityCoordinates(bodyMap, WFSectorHex.Centre(new WFSectorCell(4, -2), territory.CellSize)));

                outpost = Found(server, layers[0], user);
                Assert.That(colonies.TryWorldOf(layers[0], out world, out var network), Is.True);
                Assert.That(colonies.ColonyBody(network), Is.EqualTo(body));
                colonies.AddPoints(world, WFColonySystem.UserKey(entMan.GetComponent<WFOutpostComponent>(outpost).Owner), "Founder", 600);
                xforms.SetCoordinates(user, Tile(server, outpost, 0, 0));
                var cell = territory.CellOf(xforms.GetMapCoordinates(body));

                colonies.Tick(1f);
                Assert.That(territory.Owner(map, cell), Is.EqualTo(WFColonySystem.ColonyFaction), "A Colony world should hold its cell.");
                Assert.That(server.System<WFAdsCampaignSystem>().HasStation(map, cell), Is.True, "The ADS should leave a colony world alone.");
                var attention = new WFRaidAttentionScaleEvent(1f);
                entMan.EventBus.RaiseLocalEvent(outpost, ref attention);
                Assert.That(attention.Scale, Is.GreaterThan(1f), "A Colony world should draw more raids.");

                // A crewed ship parked in the cell, with nobody home, can't retake it.
                var grid = maps.CreateGridEntity(map);
                ship = grid.Owner;
                maps.SetTile(grid, Vector2i.Zero, new Tile(1));
                xforms.SetCoordinates(grid.Owner, new EntityCoordinates(bodyMap, xforms.GetWorldPosition(body) + new Vector2(300f, 0f)));
                Assert.That(territory.CellOf(xforms.GetMapCoordinates(grid.Owner)), Is.EqualTo(cell));
                xforms.SetCoordinates(user, new EntityCoordinates(grid.Owner, new Vector2(0.5f)));
                territory.CheckRetakes(map);
                Assert.That(territory.RetakeOf(map, cell), Is.Null, "A parked ship shouldn't start retaking a colony's cell.");
                colonies.Tick(1f);
                Assert.That(territory.Owner(map, cell), Is.EqualTo(WFColonySystem.ColonyFaction), "Nobody home shouldn't lose the cell by itself.");

                // A blockading hold next door takes the cell while nobody is home, and holding it blockades the colony.
                var next = WFSectorHex.Ring(cell, 1).First();
                Assert.That(territory.TryClaim(map, next, Hold, null), Is.True);
                colonies.Tick(1f);
                Assert.That(territory.Owner(map, cell), Is.Null, "A blockading neighbour should free the cell while nobody is home.");
                Assert.That(territory.TryClaim(map, cell, Hold, null), Is.True);
                colonies.Tick(1f);
                var colony = entMan.GetComponent<WFColonyComponent>(body);
                Assert.That(colony.Blockaded, Is.True, "A blockading hold on the colony's cell should blockade it.");
                var name = entMan.GetComponent<MetaDataComponent>(body).EntityName;
                Assert.That(DefconReasons(server).Any(reason => reason.Contains(name)), Is.True, "A blockaded colony should raise DEFCON.");

                // Someone home doesn't break a hold; once it is gone the colony takes its cell back.
                xforms.SetCoordinates(user, Tile(server, outpost, 0, 0));
                colonies.Tick(1f);
                Assert.That(territory.Owner(map, cell), Is.EqualTo(new Robust.Shared.Prototypes.ProtoId<WFSectorFactionPrototype>(Hold)));
                territory.Free(map, cell);
                territory.Free(map, next);
                colonies.Tick(1f);
                Assert.That(territory.Owner(map, cell), Is.EqualTo(WFColonySystem.ColonyFaction), "The colony should take its cell back.");
                Assert.That(colony.Blockaded, Is.False);
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (map != MapId.Nullspace)
                    territory.Clear(map);
                if (sectorMap != MapId.Nullspace)
                    SetDefaultMap.Invoke(ticker, new object[] { sectorMap });
                if (ship is { } uid)
                    entMan.DeleteEntity(uid);
                server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, OutpostRaidCVars.Enabled.DefaultValue);
            });
            await LeaveAndTeardown(pair, user, layers);
            await server.WaitPost(() => entMan.DeleteEntity(bodyMap));
            await ClearWorld(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The governor's company at Charter tier sets its world's batteries on campaign ships: they fire at a raider hull in
    /// orbit, never at a player ship. A newcomer founds there only with its permit or once the world is open, and the
    /// outpost lots wait for its outposts early in a round. At Dominion the yard kit deploys only in the world's orbit, for
    /// its governor, on clear space and once.
    /// </summary>
    [Test]
    public async Task GovernorEngagesBatteriesAndDeploysTheYard()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var colonies = server.System<WFColonySystem>();
        var yard = server.System<WFColonyYardSystem>();
        var companies = server.System<WFCompanySystem>();
        var defence = server.System<WFOrbitalDefenceSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();
        await Configure(server);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, false);
            server.CfgMan.SetCVar(ColonyCVars.CharterMembers, 1);
        });
        var (layers, user) = await Setup(pair);
        var player = pair.Player!;
        var stranger = (await server.AddDummySessions(1))[0];
        await WaitFor(pair, () => companies.Loaded && colonies.Loaded && yard.Loaded);
        var world = string.Empty;
        var company = 0;
        var orbit = MapId.Nullspace;
        EntityUid outpost = default;
        var ships = new List<EntityUid>();

        try
        {
            await Request(pair, companies, player, new(WFCompanyAction.Create) { Name = "Gamma Colonial", Tag = "GAMA" });
            await server.WaitAssertion(() =>
            {
                company = companies.FindCompany("GAMA") ?? 0;
                Assert.That(company, Is.Not.Zero, "The company wasn't made.");

                outpost = Found(server, layers[0], user);
                Assert.That(colonies.TryWorldOf(layers[0], out world, out var network), Is.True);
                orbit = entMan.GetComponent<TransformComponent>(entMan.GetComponent<WFPlanetNetworkComponent>(network).OrbitMap).MapID;
                colonies.AddPoints(world, WFColonySystem.CompanyKey(company), "Gamma Colonial", 2500);
                colonies.CheckCharter(world, DateTime.UtcNow, false);
                Assert.That(colonies.Governor(world), Is.EqualTo(company), "The company should take the charter.");

                foreach (var (id, tile) in new[] { ("WFOutpostSensorUplink", new Vector2i(0, 2)), ("WFOutpostDefenceBattery", new Vector2i(2, 2)) })
                {
                    var gizmo = entMan.SpawnEntity(id, Tile(server, outpost, tile.X, tile.Y));
                    server.System<SharedPowerReceiverSystem>().SetNeedsPower(gizmo, false);
                    entMan.GetComponent<ApcPowerReceiverComponent>(gizmo).Powered = true;
                }
            });

            await pair.RunTicksSync(pair.SecondsToTicks(1));
            await server.WaitAssertion(() =>
            {
                var raider = Ship(server, orbit, new Vector2(150f, 0f));
                var hauler = Ship(server, orbit, new Vector2(-150f, 0f));
                ships.Add(raider);
                ships.Add(hauler);
                var encounter = Blockade(entMan, EntityUid.Invalid);
                entMan.AddComponent<WFEncounterGridComponent>(raider).Encounter = encounter;
                ships.Add(encounter);

                Assert.That(defence.FireOnOrbit(), Is.Empty, "The batteries hold fire until the governor engages them.");
                Assert.That(colonies.SetDefence(world, stranger, true), Is.False, "Only the governor sets the batteries.");
                Assert.That(colonies.SetDefence(world, player, true), Is.True);
                Assert.That(defence.FireOnOrbit(), Is.EqualTo(new[] { raider }), "The batteries should fire at the raider and not the hauler.");
                // The encounter system retargets live encounters by their prototype, which the stand-in lacks.
                entMan.DeleteEntity(raider);
                entMan.DeleteEntity(encounter);
                Assert.That(defence.FireOnOrbit(), Is.Empty, "A player ship is never a target.");

                // Permits: a governed world is closed to newcomers until its governor lets them in or opens it.
                var outposts = server.System<WFOutpostSystem>();
                var newcomer = WFColonySystem.UserKey(stranger.UserId);
                Assert.That(outposts.MayFound(stranger.UserId, null, layers[0], out _), Is.False, "A newcomer needs the governor's permit.");
                Assert.That(outposts.MayFound(player.UserId, null, layers[0], out _), Is.True, "The governor's own people found freely.");
                Assert.That(colonies.SetPermit(world, stranger, newcomer, true), Is.False, "Only the governor grants permits.");
                Assert.That(colonies.SetPermit(world, player, newcomer, true), Is.True);
                Assert.That(outposts.MayFound(stranger.UserId, null, layers[0], out _), Is.True, "A permit lets a newcomer found.");
                Assert.That(colonies.SetPermit(world, player, newcomer, false), Is.True);
                Assert.That(outposts.MayFound(stranger.UserId, null, layers[0], out _), Is.False, "A withdrawn permit closes the world again.");
                Assert.That(colonies.SetOpen(world, player, true), Is.True);
                Assert.That(outposts.MayFound(stranger.UserId, null, layers[0], out _), Is.True, "An open world needs no permit.");
                Assert.That(colonies.SetOpen(world, player, false), Is.True);

                // Lots: early in a round the world's outpost lots wait for the governor's outposts. The pool's server
                // has no round, so the hold is set to cover any uptime, then to nothing.
                server.CfgMan.SetCVar(ColonyCVars.LotHold, 1e6f);
                var lots = new WFOutpostLotsEvent(stranger.UserId, layers[0]);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref lots);
                Assert.That(lots.SkipLots, Is.True, "A newcomer's drop should keep off the lots while they are held.");
                lots = new WFOutpostLotsEvent(player.UserId, layers[0]);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref lots);
                Assert.That(lots.SkipLots, Is.False, "The governor's drops take the lots.");
                server.CfgMan.SetCVar(ColonyCVars.LotHold, 0f);
                lots = new WFOutpostLotsEvent(stranger.UserId, layers[0]);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref lots);
                Assert.That(lots.SkipLots, Is.False, "Once the hold is over the lots are anyone's.");

                colonies.AddPoints(world, newcomer, "Newcomer", 1);
                Assert.That(outposts.MayFound(stranger.UserId, null, layers[0], out _), Is.True, "A holder already on the world never needs a permit.");

                // The kit: in orbit, the governor's, at Dominion, once, on clear space.
                var mob = entMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(300f, 300f), orbit));
                server.PlayerMan.SetAttachedEntity(stranger, mob);
                Assert.That(yard.MayIssueKit(world), Is.False, "No kit below Dominion.");
                xforms.SetCoordinates(user, new EntityCoordinates(MapUid(entMan, orbit), new Vector2(300f, 300f)));
                Assert.That(UseKit(server, user), Is.True, "Below Dominion the kit shouldn't deploy.");

                colonies.AddPoints(world, WFColonySystem.CompanyKey(company), "Gamma Colonial", 4000);
                Assert.That(colonies.Tier(world), Is.EqualTo(WFWorldTier.Dominion));
                Assert.That(yard.MayIssueKit(world), Is.True);
                Assert.That(UseKit(server, mob), Is.True, "Only the governor's company deploys the kit.");
                xforms.SetCoordinates(user, Tile(server, outpost, 0, 0));
                Assert.That(UseKit(server, user), Is.True, "The kit deploys only in orbit.");
                xforms.SetCoordinates(user, new EntityCoordinates(hauler, new Vector2(0.5f)));
                Assert.That(UseKit(server, user), Is.True, "The kit shouldn't deploy over another grid.");
                Assert.That(yard.HasYard(world), Is.False);

                xforms.SetCoordinates(user, new EntityCoordinates(MapUid(entMan, orbit), new Vector2(300f, 300f)));
                Assert.That(UseKit(server, user), Is.False, "The governor's kit should deploy in clear orbit.");
                Assert.That(yard.HasYard(world), Is.True);
                Assert.That(yard.MayIssueKit(world), Is.False, "A world with a yard gets no more kits.");
                Assert.That(UseKit(server, user), Is.True, "A second yard is refused.");
                entMan.DeleteEntity(mob);
            });

            // The client sees the platform go up, then drops its decals once out of sight, before the platform goes.
            await pair.RunTicksSync(pair.SecondsToTicks(1));
            await server.WaitPost(() => xforms.SetCoordinates(user, Tile(server, outpost, 0, 0)));
            await pair.RunTicksSync(pair.SecondsToTicks(1));
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (world.Length > 0)
                    yard.Demolish(world);
                foreach (var uid in ships)
                {
                    if (!entMan.Deleted(uid))
                        entMan.DeleteEntity(uid);
                }

                if (company != 0)
                    companies.AdminDisband(company);
                server.CfgMan.SetCVar(ColonyCVars.LotHold, ColonyCVars.LotHold.DefaultValue);
                server.CfgMan.SetCVar(ColonyCVars.CharterMembers, ColonyCVars.CharterMembers.DefaultValue);
                server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, OutpostRaidCVars.Enabled.DefaultValue);
            });
            await Run(pair, () => yard.Flush());
            await Run(pair, () => companies.Flush());
            await LeaveAndTeardown(pair, user, layers);
            await ClearWorld(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A round counts for an outpost once someone has been home long enough, and only once. Five rounds lower its reload
    /// markup, fifteen add two cryopods and a quarter to its producer cap, thirty lower the markup again and name it on the
    /// planet map; a sale takes it all away.
    /// </summary>
    [Test]
    public async Task HeritageCountsRoundsAndGrantsItsPerks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var colonies = server.System<WFColonySystem>();
        var outposts = server.System<WFOutpostSystem>();
        var ticker = server.System<GameTicker>();
        await Configure(server);
        await server.WaitPost(() => server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, false));
        var (layers, user) = await Setup(pair);
        var round = 0;
        var world = string.Empty;

        try
        {
            await server.WaitAssertion(() =>
            {
                // The dummy ticker never starts a round; heritage counts by round.
                round = ticker.RoundId;
                SetRoundId.Invoke(ticker, new object[] { round + 1 });
                var outpost = Found(server, layers[0], user);
                Assert.That(colonies.TryWorldOf(layers[0], out world, out var network), Is.True);
                server.System<SharedTransformSystem>().SetCoordinates(user, Tile(server, outpost, 0, 0));

                var minutes = server.CfgMan.GetCVar(ColonyCVars.HeritageMinutes);
                colonies.Tick(minutes * 30f);
                Assert.That(colonies.Heritage(outpost), Is.Zero, "Half the time home shouldn't count.");
                colonies.Tick(minutes * 30f + 1f);
                Assert.That(colonies.Heritage(outpost), Is.EqualTo(1), "A round home long enough should count.");
                colonies.Tick(minutes * 60f);
                Assert.That(colonies.Heritage(outpost), Is.EqualTo(1), "A round counts once.");

                var member = entMan.GetComponent<WFColonyMemberComponent>(outpost);
                Assert.That(Markup(entMan, outpost), Is.EqualTo(WFOutpostSystem.LoadMarkup));
                Assert.That(outposts.PodLimit(outpost), Is.EqualTo(WFOutpostSystem.MaxPods));
                member.Heritage = WFColonySystem.HeritageMarkup;
                Assert.That(Markup(entMan, outpost), Is.EqualTo(1.4f), "Five rounds should lower the reload markup.");
                Assert.That(outposts.PodLimit(outpost), Is.EqualTo(WFOutpostSystem.MaxPods));

                member.Heritage = WFColonySystem.HeritageSettled;
                Assert.That(outposts.PodLimit(outpost), Is.EqualTo(WFOutpostSystem.MaxPods + 2), "Fifteen rounds should add two cryopods.");
                var cap = new WFOutpostProducerCapEvent(100);
                entMan.EventBus.RaiseLocalEvent(outpost, ref cap);
                Assert.That(cap.Cap, Is.EqualTo(125).Within(0.001), "Fifteen rounds should add a quarter to the producer cap.");

                member.Heritage = WFColonySystem.HeritageLandmark;
                Assert.That(Markup(entMan, outpost), Is.EqualTo(1.3f), "Thirty rounds should lower the markup again.");
                var networkComp = entMan.GetComponent<WFPlanetNetworkComponent>(network);
                server.System<WFPlanetMapSystem>().Refresh(networkComp);
                colonies.Tick(1f);
                var labels = entMan.GetComponent<WFPlanetMapComponent>(networkComp.OrbitMap).Labels;
                Assert.That(labels.Select(label => label.Text), Is.EqualTo(new[] { entMan.GetComponent<MetaDataComponent>(outpost).EntityName }),
                    "A landmark should be named on the planet map.");

                var sold = new WFOutpostTransferredEvent();
                entMan.EventBus.RaiseLocalEvent(outpost, ref sold);
                Assert.That(colonies.Heritage(outpost), Is.Zero, "A sale should take the heritage away.");
                Assert.That(Markup(entMan, outpost), Is.EqualTo(WFOutpostSystem.LoadMarkup));
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                SetRoundId.Invoke(ticker, new object[] { round });
                server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, OutpostRaidCVars.Enabled.DefaultValue);
            });
            await LeaveAndTeardown(pair, user, layers);
            await ClearWorld(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    private const string Bystander = "c900009";
    private const string Agriculture = "WFTradeIndustryAgriculture";
    private const string Refining = "WFTradeIndustryRefining";
    private const string Metals = "TradeGoodMetals";
    private const string RawIron = "RawIron";
    private const string SuppliesContractor = "WFContractorColonySupplies";

    /// <summary>A sector faction that blockades, from the sector control tests' prototypes.</summary>
    private const string Hold = "WFTestSectorHolder";

    private static readonly Robust.Shared.Prototypes.ProtoId<WFDefconProfilePrototype> DefconProfile = "WFDefault";

    private static readonly MethodInfo SetDefaultMap = typeof(GameTicker).GetProperty(nameof(GameTicker.DefaultMap))!.GetSetMethod(true)!;
    private static readonly MethodInfo SetRoundId = typeof(SharedGameTicker).GetProperty(nameof(SharedGameTicker.RoundId))!.GetSetMethod(true)!;

    private static EntityCoordinates Tile(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid outpost, int x, int y)
    {
        var grid = server.EntMan.GetComponent<MapGridComponent>(outpost);
        return server.System<SharedMapSystem>().GridTileToLocal(outpost, grid, new Vector2i(x, y));
    }

    /// <summary>Deploys a trade hub east of the outpost's corner tile, powered off no grid: ready for drops.</summary>
    private static void DeployHub(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid user, EntityUid outpost)
    {
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        xforms.SetCoordinates(user, Tile(server, outpost, 0, 0));
        xforms.SetWorldRotation(user, Direction.East.ToAngle());
        var pack = entMan.SpawnEntity("WFOutpostTradeHubPack", entMan.GetComponent<TransformComponent>(user).Coordinates);
        Assert.That(server.System<WFOutpostSystem>().TryDeploy(user, (pack, entMan.GetComponent<WFDeployableComponent>(pack)), out var hub, out var reason),
            Is.True, $"The hub wasn't deployed: {reason}.");
        server.System<SharedPowerReceiverSystem>().SetNeedsPower(hub!.Value, false);
        entMan.GetComponent<ApcPowerReceiverComponent>(hub.Value).Powered = true;
    }

    /// <summary>A running raider encounter that blockades a station.</summary>
    private static EntityUid Blockade(IEntityManager entMan, EntityUid station)
    {
        var raiders = entMan.Spawn();
        var encounter = entMan.AddComponent<WFEncounterComponent>(raiders);
        encounter.Blockades = true;
        encounter.Begun = true;
        if (station.IsValid())
            encounter.Stops.Add(station);
        return raiders;
    }

    /// <summary>What each trade drop on its way down carries.</summary>
    private static List<string> Drops(IEntityManager entMan)
    {
        var drops = new List<string>();
        var query = entMan.EntityQueryEnumerator<WFTradeDropComponent>();
        while (query.MoveNext(out var drop))
        {
            if (drop.Product is { } product)
                drops.Add(product.Id);
        }

        return drops;
    }

    private static List<string> DefconReasons(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var gather = new WFDefconGatherEvent(server.ProtoMan.Index(DefconProfile), server.Timing.CurTime);
        server.EntMan.EventBus.RaiseEvent(EventSource.Local, gather);
        return gather.Contributions.Select(contribution => contribution.Reason).ToList();
    }

    /// <summary>A one-tile ship on a map.</summary>
    private static EntityUid Ship(RobustIntegrationTest.ServerIntegrationInstance server, MapId map, Vector2 position)
    {
        var maps = server.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(map);
        maps.SetTile(grid, Vector2i.Zero, new Tile(1));
        server.System<SharedTransformSystem>().SetCoordinates(grid.Owner, new EntityCoordinates(MapUid(server.EntMan, map), position));
        return grid.Owner;
    }

    private static EntityUid MapUid(IEntityManager entMan, MapId map) => entMan.System<SharedMapSystem>().GetMapOrInvalid(map);

    /// <summary>Uses a fresh yard kit in a mob's hand; true when it is still there, refused.</summary>
    private static bool UseKit(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid user)
    {
        var entMan = server.EntMan;
        var kit = entMan.SpawnEntity(WFColonySystem.YardKit, entMan.GetComponent<TransformComponent>(user).Coordinates);
        entMan.EventBus.RaiseLocalEvent(kit, new UseInHandEvent(user));
        var refused = !entMan.IsQueuedForDeletion(kit) && !entMan.Deleted(kit);
        if (refused)
            entMan.DeleteEntity(kit);
        return refused;
    }

    /// <summary>The reload markup an outpost's save is charged at.</summary>
    private static float Markup(IEntityManager entMan, EntityUid outpost)
    {
        var markup = new WFOutpostMarkupEvent(WFOutpostSystem.LoadMarkup);
        entMan.EventBus.RaiseLocalEvent(outpost, ref markup);
        return markup.Markup;
    }

    /// <summary>Takes a world's scores back to nothing; the pool reuses this server, and later tests expect it unsettled.</summary>
    private static async Task ClearWorld(TestPair pair, string world)
    {
        if (world.Length == 0)
            return;

        var colonies = pair.Server.System<WFColonySystem>();
        await pair.Server.WaitPost(() => colonies.ClearWorldForTest(world));
        await Run(pair, () => colonies.Flush());
    }

    private static void Feed(WFColonyYardSystem yard, string world, int company, int components, int metals)
    {
        for (var i = 0; i < components; i++)
            Assert.That(yard.TryFeed(world, WFColonyYardSystem.ComponentsGood, company, out _), Is.True);
        for (var i = 0; i < metals; i++)
            Assert.That(yard.TryFeed(world, WFColonyYardSystem.MetalsGood, company, out _), Is.True);
    }

    private static async Task Request(TestPair pair, WFCompanySystem companies, ICommonSession player, WFCompanyRequest request)
    {
        await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
        await Run(pair, () => companies.HandleRequest(request, player));
        await Run(pair, () => companies.Flush());
    }

    private static async Task Run(TestPair pair, System.Func<Task> start)
    {
        Task? task = null;
        await pair.Server.WaitPost(() => task = start());
        for (var i = 0; i < 600 && !task!.IsCompleted; i++)
        {
            await pair.RunTicksSync(1);
            await Task.Delay(5);
        }

        Assert.That(task!.IsCompleted, Is.True, "The task didn't finish.");
        await task;
    }

    private static async Task WaitFor(TestPair pair, System.Func<bool> condition)
    {
        var done = false;
        for (var i = 0; i < 600 && !done; i++)
        {
            await pair.Server.WaitPost(() => done = condition());
            if (!done)
            {
                await pair.RunTicksSync(1);
                await Task.Delay(10);
            }
        }

        Assert.That(done, Is.True, "Timed out waiting.");
    }
}
