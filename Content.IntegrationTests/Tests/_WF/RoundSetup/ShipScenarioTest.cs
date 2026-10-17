using System.Linq;
using System.Numerics;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Station.Components;
using Robust.Shared.EntitySerialization.Systems;
using Content.Client.Lobby;
using Content.IntegrationTests.Pair;
using Content.Server._NF.SectorServices;
using Content.Server._WF.RoundSetup;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>Purchased starts validate the catalog, share one hull and charge or refund the host exactly once.</summary>
[TestFixture]
[TestOf(typeof(WFShipScenarioSystem))]
public sealed partial class ShipScenarioTest
{
    private const string Vessel = "WFRoundSetupPurchaseTestVessel";
    private const int Price = 1000;

    [TestPrototypes]
    private const string Prototypes = @"
- type: vessel
  id: WFRoundSetupPurchaseTestVessel
  parent: Guppy
  price: 1000

- type: vessel
  id: WFRoundSetupPurchaseTestVoucherOnly
  parent: WFRoundSetupPurchaseTestVessel
  purchasable: false

- type: vessel
  id: WFRoundSetupPurchaseTestRestricted
  parent: WFRoundSetupPurchaseTestVessel
  access: Mercenary

- type: vessel
  id: WFRoundSetupPurchaseTestFaction
  parent: WFRoundSetupPurchaseTestVessel
  group: Security

- type: vessel
  id: WFRoundSetupPurchaseTestCustom
  parent: WFRoundSetupPurchaseTestVessel
  group: Custom

- type: vessel
  id: WFRoundSetupPurchaseTestFree
  parent: WFRoundSetupPurchaseTestVessel
  price: 0

- type: vessel
  id: WFRoundSetupPurchaseTestLimited
  parent: WFRoundSetupPurchaseTestVessel
  limit: 1

- type: vessel
  id: WFRoundSetupPurchaseTestViperVoucherOnly
  parent: WFRoundSetupPurchaseTestVessel
  group: Viper
  purchasable: false
";

    /// <summary>Only ordinary paid civilian listings can be selected, including when requests forge a vessel ID.</summary>
    [Test]
    public async Task CatalogExcludesRestrictedAndUnpurchasableShips()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        await pair.Server.WaitAssertion(() =>
        {
            var system = pair.Server.System<WFShipScenarioSystem>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var catalog = system.GetVessels();
            Assert.That(catalog.Select(entry => entry.Id), Does.Contain(Vessel));
            Assert.That(catalog.Single(entry => entry.Id == Vessel).Price, Is.EqualTo(Price));
            foreach (var entry in catalog)
            {
                var vessel = prototypes.Index<VesselPrototype>(entry.Id);
                Assert.Multiple(() =>
                {
                    Assert.That(vessel.Abstract, Is.False, entry.Id);
                    Assert.That(vessel.Purchasable, Is.True, entry.Id);
                    Assert.That(vessel.Price, Is.Positive, entry.Id);
                    Assert.That(vessel.Group, Is.AnyOf(ShipyardConsoleUiKey.Shipyard, ShipyardConsoleUiKey.Expedition, ShipyardConsoleUiKey.Medical, ShipyardConsoleUiKey.Scrap, ShipyardConsoleUiKey.Sr), entry.Id);
                    Assert.That(vessel.Access, Is.Null.Or.Empty, entry.Id);
                });
            }

            Assert.That(system.GetVessels(job: "Mercenary").Select(entry => entry.Id), Does.Contain("WFRoundSetupPurchaseTestRestricted"));
            Assert.That(system.IsVesselAvailable("WFRoundSetupPurchaseTestRestricted", job: "NonexistentJob"), Is.False);
            Assert.That(catalog.Select(entry => prototypes.Index<VesselPrototype>(entry.Id).Group),
                Is.SupersetOf(new[] { ShipyardConsoleUiKey.Shipyard, ShipyardConsoleUiKey.Medical, ShipyardConsoleUiKey.Expedition, ShipyardConsoleUiKey.Scrap }));
            string[] rejected =
            {
                null, string.Empty, "WFUnknownPurchaseVessel", "BaseVessel",
                "WFRoundSetupPurchaseTestVoucherOnly", "WFRoundSetupPurchaseTestRestricted",
                "WFRoundSetupPurchaseTestFaction", "WFRoundSetupPurchaseTestCustom", "WFRoundSetupPurchaseTestFree",
            };
            foreach (var id in rejected)
            {
                Assert.That(system.IsVesselAvailable(id), Is.False, id);
                Assert.That(system.TryPrepare(19100, id, out _, out var reason), Is.False, id);
                Assert.That(reason, Is.Not.Empty);
            }
            Assert.That(system.TryGetSpawn(19100, out _), Is.False);
            system.RemoveScenario(19100);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// VG, USSP and the Underground offer their shipyard's purchasable ships and those on their vouchers, and nothing
    /// from another shipyard. A voucher-only ship that no voucher lists is never sold.
    /// </summary>
    [TestCase(WFCrewFaction.VG, ShipyardConsoleUiKey.Viper, "Leviathan")]
    [TestCase(WFCrewFaction.USSP, ShipyardConsoleUiKey.Ussp, "Buran")]
    [TestCase(WFCrewFaction.Underground, ShipyardConsoleUiKey.Syndicate, "Mudskipper")]
    public async Task FactionShipyardCatalog(WFCrewFaction faction, ShipyardConsoleUiKey group, string flagship)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        await pair.Server.WaitAssertion(() =>
        {
            var system = pair.Server.System<WFShipScenarioSystem>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var catalog = system.GetVessels(faction).Select(entry => entry.Id).ToList();
            var purchasable = prototypes.EnumeratePrototypes<VesselPrototype>()
                .Where(vessel => vessel.Group == group && vessel.Price > 0 && vessel.Purchasable).Select(vessel => vessel.ID);
            Assert.Multiple(() =>
            {
                Assert.That(catalog, Is.SupersetOf(purchasable));
                Assert.That(catalog.Select(id => prototypes.Index<VesselPrototype>(id).Group), Has.All.EqualTo(group));
                Assert.That(catalog, Does.Not.Contain("WFRoundSetupPurchaseTestViperVoucherOnly"));
                Assert.That(catalog, Does.Contain(flagship));
                Assert.That(system.IsVesselAvailable(flagship, faction), Is.True);
                Assert.That(system.IsVesselAvailable("Calypso", faction), Is.False, "a TSF ship in another faction's catalog.");
                Assert.That(system.IsVesselAvailable(flagship, WFCrewFaction.TSF), Is.False);
            });
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Preparation and refused purchases cannot spend funds, including frozen Ironman accounts.</summary>
    [Test]
    public async Task FundsTraitsAndActiveVesselLimitsRejectWithoutCharging()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var player = pair.Player!;
        var saved = preferences.GetPreferences(player.UserId);
        var slot = saved.SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) saved.Characters[slot];
        var system = server.System<WFShipScenarioSystem>();
        await StartEmptyRound(pair, initializeServices: false);
        await SetProfile(pair, slot, original.WithoutAllTraitPreferences().WithBankBalance(10000));
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SectorServiceSystem>().GetServiceEntity().Valid, Is.False);
            Assert.That(system.TryPrepare(19107, Vessel, out _, out var reason), Is.True, reason);
            Assert.That(system.TryCharge(19107, player, out reason), Is.False,
                "A map without ship registration services must reject the purchase before charging.");
            Assert.That(reason, Is.Not.Empty);
            Assert.That(Balance(pair, slot), Is.EqualTo(10000));
            system.Rollback(19107);
            EnsureSectorService(pair);
        });

        await SetProfile(pair, slot, original.WithoutAllTraitPreferences().WithBankBalance(Price - 1));
        await server.WaitAssertion(() =>
        {
            Assert.That(system.TryCharge(19101, player, out _), Is.False, "A charge requires a prepared ship.");
            Assert.That(system.TryPrepare(19101, Vessel, out _, out var reason), Is.True, reason);
            Assert.That(system.GetBalance(player), Is.EqualTo(Price - 1), "Preparation must not charge the host.");
            Assert.That(system.TryCharge(19101, player, out reason), Is.False);
            Assert.That(reason, Is.Not.Empty);
            Assert.That(Balance(pair, slot), Is.EqualTo(Price - 1));
            system.Rollback(19101);
            Assert.That(Balance(pair, slot), Is.EqualTo(Price - 1), "A rejected payment must not be refunded.");
        });

        await SetProfile(pair, slot, original.WithoutAllTraitPreferences()
            .WithTraitPreference("Ironman", server.ResolveDependency<IPrototypeManager>()).WithBankBalance(10000));
        await server.WaitAssertion(() =>
        {
            Assert.That(((HumanoidCharacterProfile) preferences.GetPreferences(player.UserId).Characters[slot])
                .TraitPreferences.Any(trait => trait.Id == "Ironman"), Is.True);
            Assert.That(system.TryPrepare(19102, Vessel, out _, out var reason), Is.True, reason);
            Assert.That(system.TryCharge(19102, player, out reason), Is.False);
            Assert.That(reason, Is.Not.Empty);
            Assert.That(Balance(pair, slot), Is.EqualTo(10000));
            system.Rollback(19102);
            Assert.That(Balance(pair, slot), Is.EqualTo(10000));
        });

        await SetProfile(pair, slot, original.WithoutAllTraitPreferences().WithBankBalance(10000));
        await server.WaitAssertion(() =>
        {
            var active = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            server.EntMan.EnsureComponent<VesselComponent>(active).VesselId = "WFRoundSetupPurchaseTestLimited";
            server.EntMan.EnsureComponent<ShipActivityComponent>(active);
            Assert.That(system.TryPrepare(19106, "WFRoundSetupPurchaseTestLimited", out _, out var reason), Is.True, reason);
            Assert.That(system.TryCharge(19106, player, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("shipyard-console-limited"));
            Assert.That(Balance(pair, slot), Is.EqualTo(10000), "An active vessel limit must run before payment.");
            server.EntMan.DeleteEntity(active);
            Assert.That(system.TryCharge(19106, player, out reason), Is.True, reason);
            Assert.That(Balance(pair, slot), Is.EqualTo(10000 - Price));
            system.Rollback(19106);
            Assert.That(Balance(pair, slot), Is.EqualTo(10000));
        });

        await SetProfile(pair, slot, original);
        await ReturnToLobby(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>Retries cannot double-charge, and cancellation refunds the paid character's current balance.</summary>
    [Test]
    public async Task CancellationRefundsOriginalSlotExactlyOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var clientPreferences = pair.Client.ResolveDependency<IClientPreferencesManager>();
        var player = pair.Player!;
        var saved = preferences.GetPreferences(player.UserId);
        var slot = saved.SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) saved.Characters[slot];
        var alternate = -1;
        await pair.Client.WaitAssertion(() =>
        {
            var previous = clientPreferences.Preferences!.Characters.Keys.ToHashSet();
            clientPreferences.CreateCharacter(HumanoidCharacterProfile.DefaultWithSpecies().WithName("Scenario Refund Alternate"));
            alternate = clientPreferences.Preferences.Characters.Keys.Single(key => !previous.Contains(key));
        });
        await PoolManager.WaitUntil(server, () => preferences.GetPreferences(player.UserId).Characters.ContainsKey(alternate), maxTicks: 120);
        await StartEmptyRound(pair);
        await SetProfile(pair, slot, original.WithoutAllTraitPreferences().WithBankBalance(10000));
        var system = server.System<WFShipScenarioSystem>();
        var station = EntityUid.Invalid;
        var hull = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            Assert.That(system.TryPrepare(19103, Vessel, out station, out var reason), Is.True, reason);
            Assert.That(system.TryGetSpawn(19103, out var spawn), Is.True);
            hull = spawn.EntityId;
            Assert.That(system.TryCharge(19103, player, out reason), Is.True, reason);
            Assert.That(Balance(pair, slot), Is.EqualTo(10000 - Price));
            Assert.That(system.TryCharge(19103, player, out reason), Is.True, reason);
            Assert.That(Balance(pair, slot), Is.EqualTo(10000 - Price), "A launch retry cannot debit twice.");
        });

        var charged = (HumanoidCharacterProfile) preferences.GetPreferences(player.UserId).Characters[slot];
        await SetProfile(pair, slot, charged.WithBankBalance(10000 - Price + 250));
        var other = (HumanoidCharacterProfile) preferences.GetPreferences(player.UserId).Characters[alternate];
        await SetProfile(pair, alternate, other.WithBankBalance(7000));
        await server.WaitAssertion(() =>
        {
            Assert.That(preferences.GetPreferences(player.UserId).SelectedCharacterIndex, Is.EqualTo(alternate));
            system.Rollback(19103);
            Assert.That(Balance(pair, slot), Is.EqualTo(10250), "Refund must preserve money earned after purchase.");
            Assert.That(Balance(pair, alternate), Is.EqualTo(7000), "Changing character cannot redirect a refund.");
            system.Rollback(19103);
            system.RemoveScenario(19103);
            Assert.That(Balance(pair, slot), Is.EqualTo(10250), "A repeated rollback cannot credit again.");
            Assert.That(system.TryGetSpawn(19103, out _), Is.False);
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.EntityExists(hull), Is.False);
            Assert.That(server.EntMan.EntityExists(station), Is.False);
        });

        await SetProfile(pair, slot, original);
        await pair.Client.WaitPost(() => clientPreferences.DeleteCharacter(alternate));
        await PoolManager.WaitUntil(server, () => !preferences.GetPreferences(player.UserId).Characters.ContainsKey(alternate), maxTicks: 120);
        await ReturnToLobby(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>A disconnected buyer is refunded once after reconnecting, even across round cleanup.</summary>
    [Test]
    public async Task DeferredRefundSurvivesRoundRestartAndReconnect()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var player = pair.Player!;
        var user = player.UserId;
        var userName = player.Name;
        var slot = preferences.GetPreferences(user).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) preferences.GetPreferences(user).Characters[slot];
        var ships = server.System<WFShipScenarioSystem>();
        var net = pair.Client.ResolveDependency<IClientNetManager>();
        await StartEmptyRound(pair);
        await SetProfile(pair, slot, original.WithoutAllTraitPreferences().WithBankBalance(10000));
        var hull = EntityUid.Invalid;
        var station = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            Assert.That(ships.TryPrepare(19130, Vessel, out station, out var reason), Is.True, reason);
            Assert.That(ships.TryGetSpawn(19130, out var spawn), Is.True);
            hull = spawn.EntityId;
            Assert.That(ships.TryCharge(19130, player, out reason), Is.True, reason);
        });
        await SetProfile(pair, slot, (HumanoidCharacterProfile) preferences.GetPreferences(user).Characters[slot]);
        await pair.Client.WaitPost(() => net.ClientDisconnect("Pending refund test"));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(preferences.GetPreferencesOrNull(user), Is.Null);
            ships.Rollback(19130);
            ships.Rollback(19130);
            Assert.That(ships.TryGetSpawn(19130, out _), Is.False, "A deferred refund must not retain a dead scenario.");
            Assert.That(server.EntMan.EntityExists(hull), Is.False);
            Assert.That(server.EntMan.EntityExists(station), Is.False);
            server.System<GameTicker>().RestartRound();
        });
        await pair.RunTicksSync(10);
        pair.Client.SetConnectTarget(server);
        await pair.Client.WaitPost(() => net.ClientConnect(null!, 0, userName));
        await pair.RunTicksSync(180);
        await server.WaitAssertion(() => Assert.That(Balance(pair, slot), Is.EqualTo(10000)));
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await server.WaitAssertion(() =>
        {
            ships.RemoveScenario(19130);
            Assert.That(Balance(pair, slot), Is.EqualTo(10000), "Retries and repeated cancellation must not duplicate a refund.");
        });
        await SetProfile(pair, slot, original);
        await pair.CleanReturnAsync();
    }

    /// <summary>Committed starts share one owned hull and never replace a destroyed paid ship for free.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task SharedPaidShipSurvivesLobbyRemovalAndCannotBeReplaced(bool restricted)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var em = server.EntMan;
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var player = pair.Player!;
        var saved = preferences.GetPreferences(player.UserId);
        var slot = saved.SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) saved.Characters[slot];
        await SetProfile(pair, slot, original.WithoutAllTraitPreferences().WithBankBalance(10000));
        var system = server.System<WFShipScenarioSystem>();
        var vesselId = restricted ? "WFRoundSetupPurchaseTestRestricted" : Vessel;
        await server.WaitAssertion(() =>
        {
            Assert.That(player.AttachedEntity, Is.Not.Null);
            var serviceHost = EnsureSectorService(pair);
            foreach (var lobby in new[] { 19104, 19105 })
            {
                Assert.That(system.TryPrepare(lobby, vesselId, out var station, out var reason, job: "Mercenary"), Is.True, reason);
                Assert.That(system.TryGetSpawn(lobby, out var first), Is.True);
                Assert.That(system.TryGetSpawn(lobby, out var second), Is.True);
                Assert.That(second.EntityId, Is.EqualTo(first.EntityId));
                Assert.That(system.TryGetSpawn(lobby + 100, out _), Is.False);
                Assert.That(em.HasComponent<StationSpawningComponent>(station), Is.True);
                Assert.That(em.HasComponent<StationJobsComponent>(station), Is.True);
                var turf = server.System<TurfSystem>();
                Assert.That(turf.TryGetTileRef(first, out var tile), Is.True);
                Assert.That(turf.IsSpace(tile!.Value), Is.False);
                Assert.That(turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask), Is.False);
                Assert.That(system.TryCharge(lobby, player, out reason), Is.True, reason);
                var body = player.AttachedEntity!.Value;
                var before = em.GetComponent<TransformComponent>(body).Coordinates;
                var transform = server.System<SharedTransformSystem>();
                transform.SetCoordinates(body, first);
                Assert.That(system.CompletePurchase(lobby, player), Is.True);
                Assert.That(system.TryPrepare(lobby, vesselId, out var sameStation, out reason), Is.True,
                    "Late arrivals use the purchased hull without rechecking the successor leader’s purchase access.");
                Assert.That(sameStation, Is.EqualTo(station));
                Assert.That(server.System<SharedIdCardSystem>().TryFindIdCard(body, out var ownerCard), Is.True);
                Assert.That(em.HasComponent<ShuttleDeedComponent>(ownerCard.Owner), Is.True);
                transform.SetCoordinates(body, before);
                Assert.That(em.GetComponent<ShipOwnershipComponent>(first.EntityId).OwnerUserId, Is.EqualTo(player.UserId));
                Assert.That(em.HasComponent<ShuttleDeedComponent>(first.EntityId), Is.True);

                if (lobby == 19104)
                {
                    system.RemoveScenario(lobby);
                    Assert.That(system.TryGetSpawn(lobby, out _), Is.False);
                    Assert.That(em.EntityExists(first.EntityId), Is.True, "Closing a launched lobby must preserve its ship.");
                    Assert.That(em.EntityExists(station), Is.True);
                }
                em.DeleteEntity(first.EntityId);
                Assert.That(em.HasComponent<ShuttleDeedComponent>(ownerCard.Owner), Is.False,
                    "Hull destruction must revoke the surviving owner's networked deed, including after lobby removal.");
                if (em.EntityExists(station))
                    em.DeleteEntity(station);
                if (lobby == 19105)
                {
                    Assert.That(system.TryPrepare(lobby, vesselId, out _, out reason), Is.False,
                        "A destroyed purchased ship must not yield a replacement hull or loot.");
                    Assert.That(system.TryGetSpawn(lobby, out _), Is.False);
                    system.RemoveScenario(lobby);
                }
            }
            Assert.That(Balance(pair, slot), Is.EqualTo(10000 - 2 * Price), "Used ships must not refund when removed or destroyed.");
            if (serviceHost is { } host)
                em.DeleteEntity(host);
        });
        await SetProfile(pair, slot, original);
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }

    /// <summary>Station starts dock when possible and otherwise use clear nearby space without charging for preparation.</summary>
    [Test]
    public async Task CentralStartDocksOrSpawnsNearbyBeforePayment()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        await StartEmptyRound(pair, initializeServices: false);
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var ships = server.System<WFShipScenarioSystem>();
            var docks = server.System<DockingSystem>();
            var ticker = server.System<GameTicker>();
            var balance = ships.GetBalance(pair.Player!);
            Assert.That(ships.TryPrepare(19120, Vessel, out _, out var reason, WFShipStartLocation.CentralDock), Is.False);
            Assert.That(reason, Is.EqualTo("wf-round-setup-ship-no-station-space"));
            Assert.That(ships.TryGetSpawn(19120, out _), Is.False);
            Assert.That(ships.GetBalance(pair.Player!), Is.EqualTo(balance));

            var vessel = server.ResolveDependency<IPrototypeManager>().Index<VesselPrototype>(Vessel);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(ticker.DefaultMap, vessel.ShuttlePath,
                out var loaded, offset: new Vector2(800, 800)), Is.True);
            var central = loaded!.Value.Owner;
            var station = em.SpawnEntity(null, MapCoordinates.Nullspace);
            em.EnsureComponent<StationDataComponent>(station);
            em.EnsureComponent<StationSectorServiceHostComponent>(station);
            server.System<StationSystem>().AddGridToStation(station, central);
            Assert.That(ships.TryPrepare(19120, Vessel, out _, out reason, WFShipStartLocation.CentralDock), Is.True, reason);
            Assert.That(ships.TryGetSpawn(19120, out var spawn), Is.True);
            var hull = spawn.EntityId;
            Assert.That(docks.GetDocks(hull).Any(dock => dock.Comp.DockedWith is { } other &&
                em.GetComponent<TransformComponent>(other).GridUid == central), Is.True, "The ship must actually be attached to CC.");
            Assert.That(ships.GetBalance(pair.Player!), Is.EqualTo(balance), "Docking preparation cannot charge the player.");
            ships.Rollback(19120);
            foreach (var dock in docks.GetDocks(central).ToArray())
                em.DeleteEntity(dock.Owner);
            Assert.That(ships.TryPrepare(19121, Vessel, out _, out reason, WFShipStartLocation.CentralDock), Is.True, reason);
            Assert.That(ships.TryGetSpawn(19121, out spawn), Is.True);
            var transform = server.System<SharedTransformSystem>();
            var center = transform.GetWorldPosition(central);
            Assert.That(Vector2.Distance(transform.GetWorldPosition(spawn.EntityId), center), Is.LessThan(1200f));
            Assert.That(ships.GetBalance(pair.Player!), Is.EqualTo(balance));
            Assert.That(docks.GetDocks(spawn.EntityId).All(dock => !dock.Comp.Docked), Is.True);
            ships.Rollback(19121);
            em.DeleteEntity(central);
            em.DeleteEntity(station);
        });
        await ReturnToLobby(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>Loads the sector through the normal round pipeline while keeping the test player unspawned.</summary>
    private static async Task StartEmptyRound(TestPair pair, bool initializeServices = true)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            ticker.StartRound(force: true);
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            Assert.That(pair.Player!.AttachedEntity, Is.Null);
            if (initializeServices)
                EnsureSectorService(pair);
        });
        await pair.RunTicksSync(2);
    }

    /// <summary>Provides the same sector record services as ordinary shipyard stations.</summary>
    private static EntityUid? EnsureSectorService(TestPair pair)
    {
        if (pair.Server.System<SectorServiceSystem>().GetServiceEntity().Valid)
            return null;
        var host = pair.Server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
        pair.Server.EntMan.AddComponent<StationSectorServiceHostComponent>(host);
        Assert.That(pair.Server.System<SectorServiceSystem>().GetServiceEntity().Valid, Is.True);
        return host;
    }
    private static async Task ReturnToLobby(TestPair pair)
    {
        await pair.Server.WaitPost(() => pair.Server.System<GameTicker>().RestartRound());
        await pair.RunTicksSync(2);
    }

    private static Task SetProfile(TestPair pair, int slot, HumanoidCharacterProfile profile)
    {
        return pair.Server.WaitPost(() => pair.Server.ResolveDependency<IServerPreferencesManager>()
            .SetProfile(pair.Player!.UserId, slot, profile).Wait());
    }

    private static int Balance(TestPair pair, int slot)
    {
        return ((HumanoidCharacterProfile) pair.Server.ResolveDependency<IServerPreferencesManager>()
            .GetPreferences(pair.Player!.UserId).Characters[slot]).BankBalance;
    }
}
