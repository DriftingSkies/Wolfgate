#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client.Overlays;
using Content.Server.Body.Components;
using Content.Server.Chat.Systems;
using Content.Server._CE.ZLevels.Core;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._WF.Voice;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Buckle;
using Content.Shared.Access.Systems;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Overlays;
using Content.Shared.Players;
using Content.Shared.Vehicle.Components;
using Content.Server._WF.Drydock;
using Content.Server._WF.Planets.Flight;
using Content.Server.Power.Components;
using Content.Server._Mono.ShipRepair;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.Vehicles.Compat;
using Content.Shared._WF.Planets.Flight;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Player reports from the 2026-11-07 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1107bTest
{
    /// <summary>Report 614: the MMC liaison can use the cargo bounty console, an ordinary crewman cannot.</summary>
    [Test]
    public async Task LiaisonUsesBountyConsole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var access = server.System<AccessReaderSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var console = em.SpawnEntity("ComputerCargoBounty", map.GridCoords);
            var liaison = SpawnWithCard(server, map.GridCoords, "MieyoLiason");
            var crew = SpawnWithCard(server, map.GridCoords, "Maintenance");

            Assert.That(access.IsAllowed(liaison, console), Is.True, "The liaison is denied.");
            Assert.That(access.IsAllowed(crew, console), Is.False, "Anyone can skip bounties.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 603: a door opened by a vehicle logs its rider by name.</summary>
    [Test]
    public async Task VehicleAccessLogsRider()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var access = server.System<AccessReaderSystem>();
        var idCards = server.System<SharedIdCardSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var door = em.SpawnEntity("Airlock", map.GridCoords);
            var rider = SpawnWithCard(server, map.GridCoords, "Maintenance");
            Assert.That(server.System<InventorySystem>().TryGetSlotEntity(rider, "id", out var card), Is.True);
            Assert.That(idCards.TryChangeFullName(card!.Value, "Testy Rider"), Is.True);

            var vehicle = em.SpawnEntity("VehicleATV", map.GridCoords);
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(rider, rider, vehicle), Is.True, "The rider did not mount.");
            Assert.That(em.GetComponent<VehicleComponent>(vehicle).Rider, Is.EqualTo(rider));

            var reader = em.GetComponent<AccessReaderComponent>(door);
            reader.AccessLog.Clear();
            access.LogAccess((door, reader), vehicle);

            Assert.That(reader.AccessLog.Count, Is.EqualTo(1));
            Assert.That(reader.AccessLog.Last().Accessor, Does.Contain("Testy Rider"), "The log does not name the rider.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 602: sign language sends no voice, spoken language does.</summary>
    [Test]
    public async Task SignLanguageSendsNoVoice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var voices = pair.Client.EntMan.System<Content.Client._WF.Voice.SpeechVoiceSystem>();
        var map = await pair.CreateTestMap();
        var talker = EntityUid.Invalid;
        const string line = "this line runs long enough to keep playing for the whole of the test";

        var minds = em.System<SharedMindSystem>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        await server.WaitPost(() =>
        {
            minds.WipeMind(session.ContentData()?.Mind);
            var listener = em.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, listener);
            talker = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));
            var comp = em.EnsureComponent<SpeechVoiceComponent>(talker);
            comp.Voice = new SpeechVoice { Speed = SpeechVoice.MaxSpeed };
            em.Dirty(talker, comp);
        });
        await pair.RunTicksSync(5);

        void Speak(string language)
        {
            var proto = server.ProtoMan.Index<LanguagePrototype>(language);
            em.EventBus.RaiseLocalEvent(talker, new EntitySpokeEvent(talker, line, null, false, proto), true);
        }

        var start = voices.ActiveLines;
        await server.WaitPost(() => Speak("Sign"));
        await pair.RunTicksSync(5);
        Assert.That(voices.ActiveLines, Is.EqualTo(start), "Signing made a voice noise.");

        await server.WaitPost(() => Speak("TauCetiBasic"));
        await pair.RunTicksSync(5);
        Assert.That(voices.ActiveLines, Is.EqualTo(start + 1), "Speech lost its voice.");

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 595: a Synth's heart metabolises cryogenic reagents like a human's.</summary>
    [Test]
    public async Task SynthHeartMetabolisesCryogenics()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "OrganHumanHeart", "OrganSynthHeart" })
            {
                var proto = server.ProtoMan.Index<EntityPrototype>(id);
                Assert.That(proto.TryGetComponent<MetabolizerComponent>(out var metabolizer, factory), Is.True);
                Assert.That(metabolizer!.MetabolismGroups!.Select(g => g.Id.Id), Does.Contain("Cryogenic"), $"{id} cannot process cryoxadone.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 589: taking off a night-vision helmet clears the wearer's overlay.</summary>
    [Test]
    public async Task NightVisionClearsOnUnequip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var em = server.EntMan;
        var inventory = em.System<InventorySystem>();
        var minds = em.System<SharedMindSystem>();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);
        var map = await pair.CreateTestMap();
        var wearer = EntityUid.Invalid;
        var helmet = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            minds.WipeMind(session.ContentData()?.Mind);
            wearer = em.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, wearer);
            helmet = em.SpawnEntity("ClothingHelmetHardsuitMieyoCombat", map.GridCoords);
            var nv = em.GetComponent<NightVisionComponent>(helmet);
            nv.Enabled = true;
            em.Dirty(helmet, nv);
            Assert.That(inventory.TryEquip(wearer, helmet, "head", force: true), Is.True);
        });
        await pair.RunTicksSync(10);

        bool Overlay() => client.ResolveDependency<IOverlayManager>().HasOverlay<NightVisionOverlay>();

        await client.WaitAssertion(() => Assert.That(Overlay(), Is.True, "The helmet gave no overlay."));

        await server.WaitPost(() => Assert.That(inventory.TryUnequip(wearer, "head", force: true), Is.True));
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() => Assert.That(Overlay(), Is.False, "The overlay stuck after the helmet came off."));

        await pair.CleanReturnAsync();
    }

    /// <summary>Reports 606, 594, 591: things standing on a hull that the ground layer still owns lift off with it.</summary>
    [TestCase("stale")]
    [TestCase("walked")]
    public async Task LiftoffTakesStandersAndCargo(string how)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var levels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-2, -2), new Vector2i(18, 18));
        var mapId = em.GetComponent<MapComponent>(ground).MapId;

        var hull = await BuildHull(pair, mapId);
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 3);
        var pilot = await HoldVertical(pair, hull, ShuttleButtons.None);
        var console = FindShuttleConsole(em, hull);

        var crate = EntityUid.Invalid;
        var mob = EntityUid.Invalid;
        var anchoredOutside = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            if (how == "stale")
            {
                crate = em.SpawnEntity("CrateGeneric", new EntityCoordinates(hull, 5.5f, 5.5f));
                mob = em.SpawnEntity("MobHuman", new EntityCoordinates(hull, 6.5f, 6.5f));
                xforms.SetParent(crate, ground);
                xforms.SetParent(mob, ground);
            }
            else
            {
                crate = em.SpawnEntity("CrateGeneric", new MapCoordinates(new Vector2(-1.5f, 5.5f), mapId));
                mob = em.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(-1.5f, 6.5f), mapId));
                xforms.SetWorldPosition(crate, new Vector2(5.5f, 5.5f));
                xforms.SetWorldPosition(mob, new Vector2(6.5f, 6.5f));
            }

            anchoredOutside = em.SpawnEntity("CrateGeneric", new MapCoordinates(new Vector2(-1.5f, -1.5f), mapId));
        });
        await server.WaitRunTicks(2);

        var before = $"crate parent={em.GetComponent<TransformComponent>(crate).ParentUid} mob parent={em.GetComponent<TransformComponent>(mob).ParentUid} hull={hull} ground={ground}";

        await server.WaitAssertion(() =>
            Assert.That(levels.WfTryBeginLiftoff(hull, console, pilot, out var reason), Is.True, $"Liftoff refused: {reason}"));

        var air = layers[1];
        var reached = false;
        for (var second = 0; second < 45 && !reached; second++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitPost(() => reached = em.GetComponent<TransformComponent>(hull).MapUid == air);
        }

        Assert.That(reached, Is.True, "The hull never lifted off.");
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var hullMap = em.GetComponent<TransformComponent>(hull).MapUid;
            var crateXform = em.GetComponent<TransformComponent>(crate);
            var mobXform = em.GetComponent<TransformComponent>(mob);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(crateXform.MapUid, Is.EqualTo(hullMap), $"The crate stayed on the ground layer. {before}");
                Assert.That(mobXform.MapUid, Is.EqualTo(hullMap), $"The player stayed on the ground layer. {before}");
                Assert.That(crateXform.GridUid, Is.EqualTo(hull), "The crate is not riding the hull.");
                Assert.That(mobXform.GridUid, Is.EqualTo(hull), "The player is not riding the hull.");
                Assert.That(em.GetComponent<TransformComponent>(anchoredOutside).MapUid, Is.EqualTo(ground),
                    "A crate off the hull was dragged along.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 586: a body pulled into a vehicle lands on the cabin floor, never on the void ring.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task PulledBodyAvoidsVoidRing(bool floorBeside)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();
        var handler = server.System<VehiclePullingSystem>();
        var pulling = server.System<PullingSystem>();
        var floor = new Tile(tiles["RMCFloorVehicleInteriorFloor0"].TileId);
        var hollow = new Tile(tiles["RMCVoid"].TileId);

        MapId mapId = default;
        EntityUid grid = default, puller = default, body = default;
        await server.WaitPost(() =>
        {
            var map = maps.CreateMap(out mapId);
            var gridEnt = maps.CreateGridEntity(mapId);
            grid = gridEnt.Owner;
            for (var x = -2; x <= 2; x++)
            for (var y = -2; y <= 2; y++)
                maps.SetTile(grid, gridEnt.Comp, new Vector2i(x, y), hollow);
            maps.SetTile(grid, gridEnt.Comp, Vector2i.Zero, floor);
            if (floorBeside)
                maps.SetTile(grid, gridEnt.Comp, new Vector2i(-1, 0), floor);

            puller = em.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(20, 20), mapId));
            body = em.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(20, 20), mapId));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(pulling.TryStartPull(puller, body), Is.True, "Precondition: the pull did not start.");
            handler.HandlePulling(puller, new MapCoordinates(new Vector2(0.5f, 0.5f), mapId));

            var tile = maps.CoordinatesToTile(grid, em.GetComponent<MapGridComponent>(grid), em.GetComponent<TransformComponent>(body).Coordinates);
            var expected = floorBeside ? new Vector2i(-1, 0) : Vector2i.Zero;
            Assert.That(tile, Is.EqualTo(expected), "The pulled body was put on the wrong tile.");
        });

        await server.WaitPost(() => maps.DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 601: a drydock repair clears a crash-faulted APC.</summary>
    [Test]
    public async Task DrydockRepairClearsApcFault()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var console = new WFDrydockConsoleComponent { Berth = "T" };

        await server.WaitAssertion(() =>
        {
            var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
                server.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), plating);

            var apc = Drydock.DrydockTest.SpawnAnchored(pair, map.Grid.Owner, "APCBasic", new Vector2i(1, 1));
            server.System<ShipRepairSystem>().GenerateRepairData(map.Grid.Owner);
            em.AddComponent<WFCrashApcFaultComponent>(apc);
            Assert.That(em.GetComponent<TransformComponent>(apc).Anchored, Is.True, "The APC is not anchored.");

            var items = drydock.BuildRepairItems(map.Grid.Owner, console);
            Assert.That(items.Count(i => i.Kind == WFDrydockWorkKind.Heal), Is.EqualTo(1), "The faulted APC is not on the repair quote.");

            foreach (var item in items)
                drydock.ApplyItem(map.Grid.Owner, item);

            Assert.That(em.HasComponent<WFCrashApcFaultComponent>(apc), Is.False, "The fault survived the repair.");
            Assert.That(drydock.BuildRepairItems(map.Grid.Owner, console), Is.Empty, "Work is left after the repair.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Reports 598, 604: every mothroach displacement state has four directions.</summary>
    [Test]
    public async Task MothroachDisplacementStatesExist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan.Index<EntityPrototype>("MobMothroach");
            Assert.That(proto.TryGetComponent<InventoryComponent>(out var inventory, server.EntMan.ComponentFactory), Is.True);
            var resources = server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();
            Assert.That(inventory!.Displacements, Is.Not.Empty);

            foreach (var (slot, data) in inventory.Displacements)
            {
                var spec = data.SizeMaps[32];
                var rsi = new Robust.Shared.Utility.ResPath("/Textures") / spec.RsiPath!;
                Assert.That(resources.ContentFileExists(rsi / "meta.json"), Is.True, $"{slot}: no displacement rsi.");
                using var stream = resources.ContentFileRead(rsi / "meta.json");
                using var reader = new System.IO.StreamReader(stream);
                var meta = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());
                var state = meta.RootElement.GetProperty("states").EnumerateArray()
                    .FirstOrDefault(s => s.GetProperty("name").GetString() == spec.State);
                Assert.That(state.ValueKind, Is.Not.EqualTo(System.Text.Json.JsonValueKind.Undefined), $"{slot}: state {spec.State} is missing.");
                Assert.That(state.GetProperty("directions").GetInt32(), Is.EqualTo(4), $"{slot}: state {spec.State} lacks directions.");
            }
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnWithCard(RobustIntegrationTest.ServerIntegrationInstance server, EntityCoordinates coords, string tag)
    {
        var mob = server.EntMan.SpawnEntity("MobHuman", coords);
        var inventory = server.System<InventorySystem>();
        Assert.That(inventory.SpawnItemInSlot(mob, "id", "PassengerIDCard", force: true), Is.True);
        Assert.That(inventory.TryGetSlotEntity(mob, "id", out var card), Is.True);
        Assert.That(server.System<SharedAccessSystem>().TrySetTags(card!.Value, new[] { new ProtoId<AccessLevelPrototype>(tag) }), Is.True);
        return mob;
    }

    /// <summary>#599/#593: TSF contractors are paid no Federation Military Credits by encounters, campaigns or the trade war.</summary>
    [Test]
    public async Task TsfContractorsGetNoFederationCredits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            const string contractor = "TSFCivilian";
            const string credit = "FederationMilitaryCredit1";
            foreach (var encounter in protos.EnumeratePrototypes<Content.Shared._WF.Encounters.WFEncounterPrototype>())
            {
                foreach (var reward in encounter.Rewards)
                {
                    if (reward.CreditEntity?.Id == credit)
                        Assert.That(reward.CreditCompanies, Does.Not.Contain(contractor), $"{encounter.ID} pays contractors FMC.");
                }
            }

            foreach (var market in protos.EnumeratePrototypes<Content.Shared._WF.TradeGoods.WFTradeMarketPrototype>())
            {
                foreach (var navy in market.Navies)
                {
                    if (navy.CreditEntity?.Id == credit && navy.Companies.Any(c => c == contractor))
                        Assert.That(navy.Unpaid.Any(c => c == contractor), Is.True, $"{market.ID} pays contractors FMC.");
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>#588: an atmospheric technician's ID opens air alarms.</summary>
    [Test]
    public async Task AtmosTechHasAtmosphericsAccess()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var job = protos.Index<Content.Shared.Roles.JobPrototype>("AtmosphericTechnician");
            Assert.That(job.Access.Any(a => a == "Atmospherics"), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>#617: a dose of vitae in a corpse heals it and brings it back.</summary>
    [Test]
    public async Task VitaeWorksOnTheDead()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var mobState = entMan.System<Content.Shared.Mobs.Systems.MobStateSystem>();

        EntityUid body = default;
        await server.WaitAssertion(() => body = entMan.SpawnEntity("MobHuman", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var blunt = server.ProtoMan.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Blunt");
            entMan.System<Content.Shared.Damage.DamageableSystem>()
                .TryChangeDamage(body, new Content.Shared.Damage.DamageSpecifier(blunt, 300), true);
            mobState.ChangeMobState(body, Content.Shared.Mobs.MobState.Dead);
            Assert.That(mobState.IsDead(body), Is.True, "Test setup: the body should be dead.");
            Assert.That(entMan.System<Content.Server.Body.Systems.BloodstreamSystem>()
                .TryAddToChemicals(body, new Content.Shared.Chemistry.Components.Solution("WFVitae", 10)), Is.True);
        });
        await pair.RunSeconds(6);

        await server.WaitAssertion(() =>
        {
            var blood = entMan.GetComponent<Content.Server.Body.Components.BloodstreamComponent>(body);
            var left = entMan.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>()
                .TryGetSolution(body, blood.ChemicalSolutionName, out _, out var chem) ? chem.GetTotalPrototypeQuantity("WFVitae") : -1;
            var dmg = entMan.GetComponent<Content.Shared.Damage.DamageableComponent>(body).TotalDamage;
            Assert.That(mobState.IsDead(body), Is.False, $"Vitae did nothing for the corpse (vitae left {left}, damage {dmg}).");
        });
        await pair.CleanReturnAsync();
    }
}
