#nullable enable
using System.Linq;
using Content.Server._Mono.AmmoLoader;
using Content.Server._Mono.StationEvents;
using Content.Server.StationEvents.Components;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Wires;
using Content.Shared._WF.Construction;
using Content.Shared.Construction;
using Robust.Shared.Configuration;
using Content.Server._NF.Shipyard.Components;
using Content.Shared._CE.ZLevels.Flight;
using Content.Shared._CE.ZLevels.Flight.Components;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Planets;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Server.Station.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared.Maps;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Player reports from the 2026-11-08 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1108Test
{
    private const string ExpeditionMap = "WFFeedback1108ExpeditionMap";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: gameMap
  id: {ExpeditionMap}
  minPlayers: 0
  mapName: {ExpeditionMap}
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: {ExpeditionMap}
      stationProto: StandardNanotrasenStation
      components:
        - type: SalvageExpeditionData
";

    /// <summary>
    /// Report 641: a generated expedition claimed from a ship that can't jump off a planet is refused, instead of
    /// leaving the console claimed at 00:00 with the ship never arriving.
    /// </summary>
    [Test]
    public async Task ExpeditionRefusedBelowOrbit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var proto = server.ProtoMan;
        var maps = em.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        var station = EntityUid.Invalid;
        var console = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.PlanetExpeditions, false);
            var shipMap = maps.CreateMap(out var shipMapId);
            em.EnsureComponent<WFPlanetLayerComponent>(shipMap);
            var ship = maps.CreateGridEntity(shipMapId);
            maps.SetTile(ship.Owner, ship.Comp, Vector2i.Zero, new Tile(tileDefs["FloorSteel"].TileId));
            console = em.SpawnEntity("ComputerSalvageExpedition", new EntityCoordinates(ship.Owner, 0.5f, 0.5f));
            station = em.System<StationSystem>().InitializeNewStation(proto.Index<GameMapPrototype>(ExpeditionMap).Stations["Station"], new[] { ship.Owner }, ExpeditionMap);
        });

        await server.WaitPost(() => em.GetComponent<SalvageExpeditionDataComponent>(station).NextOffer = TimeSpan.Zero);
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var data = em.GetComponent<SalvageExpeditionDataComponent>(station);
            Assert.That(data.Missions, Is.Not.Empty, "No generated offers.");
            em.EventBus.RaiseLocalEvent(console, new ClaimSalvageMessage { Index = data.Missions.Keys.First() });
            Assert.That(data.Claimed, Is.False, "A ship on a planet's surface claimed a jump it can't make.");
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.PlanetExpeditions, true));
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 639: a judge's voucher ship flies under Caelestinus, not unaffiliated.</summary>
    [Test]
    public async Task JudgeVoucherShipIsCaelestinus()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var voucher = protos.Index<EntityPrototype>("WFShipVoucherJudge");
            Assert.That(voucher.TryGetComponent<ShipyardVoucherComponent>(out var comp, server.EntMan.ComponentFactory), Is.True);
            Assert.That(comp!.CompanyName, Is.EqualTo("Colonial"));
            Assert.That(protos.HasIndex<CompanyPrototype>(comp.CompanyName!), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 625: a screwdriver used on a powered material reclaimer is not fed into it.</summary>
    [Test]
    public async Task ReclaimerKeepsScrewdriver()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        var reclaimer = EntityUid.Invalid;
        var screwdriver = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            reclaimer = em.SpawnEntity("MaterialReclaimer", map.GridCoords);
            var comp = em.GetComponent<MaterialReclaimerComponent>(reclaimer);
#pragma warning disable RA0002 // no power net here; the reclaimer only takes items while powered
            comp.Powered = true;
            comp.Enabled = true;
#pragma warning restore RA0002

            screwdriver = em.SpawnEntity("Screwdriver", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(user, screwdriver), Is.True);
            em.EventBus.RaiseLocalEvent(reclaimer, new InteractUsingEvent(user, screwdriver, reclaimer, map.GridCoords));
        });

        // Long enough for the screwdriver's do-after to open the panel.
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(screwdriver) || em.IsQueuedForDeletion(screwdriver), Is.False, "The reclaimer ate the screwdriver.");
            Assert.That(em.HasComponent<ActiveMaterialReclaimerComponent>(reclaimer), Is.False);
            Assert.That(em.GetComponent<WiresPanelComponent>(reclaimer).Open, Is.True, "The screwdriver didn't open the panel.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 627: the AC-35's infinite magazine shows as loaded to the ammo loader, so it can be unloaded.</summary>
    [Test]
    public async Task LoaderSeesInfiniteMagazine()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var gun = em.SpawnEntity("WeaponTurretAC-35", map.GridCoords);
            Assert.That(em.System<AmmoLoaderSystem>().TryGetTurretAmmoState(gun, out var loaded, out var count, out var capacity, out _), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(loaded?.Id, Is.EqualTo("Magazine35mm"));
                Assert.That(count, Is.EqualTo(capacity));
                Assert.That(count, Is.GreaterThan(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 618: an Avali can't take off in a planet's orbit layer, and stops flying on reaching one on foot or aboard a ship.</summary>
    [Test]
    public async Task NoFlightInOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var flight = em.System<CESharedZFlightSystem>();
        var transform = em.System<SharedTransformSystem>();

        EntityUid orbit = default;
        await server.WaitPost(() =>
        {
            orbit = em.System<SharedMapSystem>().CreateMap(out _);
            em.EnsureComponent<WFOrbitLayerComponent>(orbit);
        });

        var walker = EntityUid.Invalid;
        var passenger = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            var grounded = em.SpawnEntity("MobAvali", new EntityCoordinates(orbit, 0, 0));
            Assert.That(flight.TryActivateFlight(grounded), Is.False, "The Avali took off in vacuum.");

            walker = em.SpawnEntity("MobAvali", map.GridCoords);
            passenger = em.SpawnEntity("MobAvali", map.GridCoords);
            Assert.That(flight.TryActivateFlight(walker), Is.True);
            Assert.That(flight.TryActivateFlight(passenger), Is.True);
            transform.SetCoordinates(walker, new EntityCoordinates(orbit, 2, 2));
            // The ship hops into orbit with the passenger still parented to it.
            transform.SetMapCoordinates(map.Grid, new MapCoordinates(new System.Numerics.Vector2(40, 40), em.GetComponent<TransformComponent>(orbit).MapID));
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(passenger).ParentUid, Is.EqualTo(map.Grid.Owner));
            Assert.That(em.GetComponent<CEZFlyerComponent>(walker).Active, Is.False, "The Avali kept flying into orbit.");
            Assert.That(em.GetComponent<CEZFlyerComponent>(passenger).Active, Is.False, "The Avali aboard the ship kept flying in orbit.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 643: a capped event stops extending at its cap and warns again before the real end.</summary>
    [Test]
    public async Task ExtensionsStopAtTheCap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        var rule = EntityUid.Invalid;
        var firstEnd = TimeSpan.Zero;
        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(pair.Player!, em.SpawnEntity("MobHuman", map.GridCoords));
            var watched = em.SpawnEntity(null, map.GridCoords);
            rule = em.SpawnEntity(null, MapCoordinates.Nullspace);
            var ev = em.AddComponent<StationEventComponent>(rule);
            ev.Duration = TimeSpan.FromSeconds(16);
            firstEnd = server.Timing.CurTime + TimeSpan.FromSeconds(1);
            ev.EndTime = firstEnd;
            ev.WarningAnnounced = true;
            ev.WarningDurationLeft = 30;
            Assert.That(em.System<AutoExtendRuleSystem>().AutoExtend(rule, watched), Is.True);
            em.GetComponent<AutoExtendRuleComponent>(rule).MaxExtensions = 2;
        });

        // Two seconds a step, a check every second: well past two extensions.
        await pair.RunSeconds(12);

        await server.WaitAssertion(() =>
        {
            var extend = em.GetComponent<AutoExtendRuleComponent>(rule);
            var ev = em.GetComponent<StationEventComponent>(rule);
            Assert.Multiple(() =>
            {
                Assert.That(extend.Extensions, Is.EqualTo(2));
                Assert.That(ev.EndTime, Is.GreaterThanOrEqualTo(firstEnd + extend.ExtendBy * 2));
                // The final extension left the whole 30 s warning window; at most 12 s of it have passed.
                Assert.That(ev.EndTime - server.Timing.CurTime, Is.GreaterThan(TimeSpan.FromSeconds(17)));
                Assert.That(ev.WarningAnnounced, Is.False, "The final extension didn't re-arm the departure warning.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Reports 648 and 649: a kit used on a machine comes back out when the machine is taken apart.</summary>
    [Test]
    public async Task KitsComeBackOnDeconstruction()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var lathe = em.SpawnEntity("Autolathe", map.GridCoords);
            var kit = em.SpawnEntity("LatheUpgradeKitHyper", map.GridCoords);
            em.System<WFKitRefundSystem>().RecordKit(lathe, kit);
            em.DeleteEntity(kit);

            em.EventBus.RaiseLocalEvent(lathe, new MachineDeconstructedEvent());
            var back = em.System<EntityLookupSystem>().GetEntitiesInRange(map.GridCoords, 1f)
                .Count(e => em.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "LatheUpgradeKitHyper");
            Assert.That(back, Is.EqualTo(1), "The upgrade kit wasn't given back.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 630: Synth and IPC names keep punctuation under restricted names; other species lose it.</summary>
    [Test]
    public async Task SynthNamesKeepPunctuation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var restricted = cfg.GetCVar(CCVars.RestrictedNames);
        const string name = "AM-B2 \"Nick\"";

        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(CCVars.RestrictedNames, true);
            var session = server.PlayerMan.Sessions.First();
            string Valid(string species)
            {
                var profile = HumanoidCharacterProfile.DefaultWithSpecies(species).WithName(name);
                profile.EnsureValid(session, server.InstanceDependencyCollection);
                return profile.Name;
            }

            Assert.Multiple(() =>
            {
                Assert.That(Valid("Synth"), Is.EqualTo(name));
                Assert.That(Valid("IPC"), Is.EqualTo(name));
                Assert.That(Valid("Human"), Is.EqualTo("AM-B2 Nick"));
            });
            cfg.SetCVar(CCVars.RestrictedNames, restricted);
        });

        await pair.CleanReturnAsync();
    }
}
