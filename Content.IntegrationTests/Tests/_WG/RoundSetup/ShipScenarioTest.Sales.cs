using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._NF.SectorServices;
using Content.Server._WG.RoundSetup;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.RoundSetup;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Content.Shared.Station.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.RoundSetup;

public sealed partial class ShipScenarioTest
{
    /// <summary>Roundstart purchases initialize the same sale service as console purchases.</summary>
    [Test]
    public async Task CrewPurchaseCanSellAtDockWithoutAnotherShipyardPurchase()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var em = server.EntMan;
        var player = pair.Player!;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var slot = prefs.GetPreferences(player.UserId).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) prefs.GetPreferences(player.UserId).SelectedCharacter!;
        await SetProfile(pair, slot, original.WithoutAllTraitPreferences().WithBankBalance(10000));
        try
        {
            await server.WaitAssertion(() =>
            {
                var ships = server.System<WGShipScenarioSystem>();
                var shipyard = server.System<ShipyardSystem>();
                typeof(ShipyardSystem).GetProperty(nameof(ShipyardSystem.ShipyardMap))!.SetValue(shipyard, null);
                var vessel = server.ResolveDependency<IPrototypeManager>().Index<VesselPrototype>(Vessel);
                Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(server.System<GameTicker>().DefaultMap, vessel.ShuttlePath,
                    out var dock, offset: new Vector2(800, 800)), Is.True);
                var central = dock!.Value.Owner;
                var station = em.SpawnEntity(null, MapCoordinates.Nullspace);
                em.EnsureComponent<StationDataComponent>(station);
                em.EnsureComponent<StationSectorServiceHostComponent>(station);
                server.System<StationSystem>().AddGridToStation(station, central);
                Assert.That(ships.TryPrepare(19250, Vessel, out _, out var reason, WGShipStartLocation.CentralDock), Is.True, reason);
                Assert.That(ships.TryGetSpawn(19250, out var spawn), Is.True);
                Assert.That(ships.TryCharge(19250, player, out reason), Is.True, reason);
                var body = player.AttachedEntity!.Value;
                server.System<SharedTransformSystem>().SetCoordinates(body, spawn);
                Assert.That(ships.CompletePurchase(19250, player), Is.True);
                Assert.That(shipyard.ShipyardMap, Is.Not.Null);
                var console = em.SpawnEntity(null, new EntityCoordinates(central, Vector2.Zero));
                var occupied = shipyard.TrySellShuttle(station, spawn.EntityId, console, out _);
                Assert.That(occupied.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.OrganicsAboard));
                server.System<SharedTransformSystem>().SetCoordinates(body, new EntityCoordinates(central, Vector2.Zero));
                foreach (var (_, xform) in em.EntityQuery<MobStateComponent, TransformComponent>().ToArray())
                    if (xform.GridUid == spawn.EntityId) em.DeleteEntity(xform.Uid());
                var sale = shipyard.TrySellShuttle(station, spawn.EntityId, console, out var value);
                Assert.That(sale.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.Success));
                Assert.That(value, Is.Positive);
                ships.RemoveScenario(19250);
                Assert.That(Balance(pair, slot), Is.EqualTo(10000 - Price), "Selling does not refund the original crew purchase.");
            });
        }
        finally
        {
            await SetProfile(pair, slot, original);
            await ReturnToLobby(pair);
        }
        await pair.CleanReturnAsync();
    }

    /// <summary>T0 and T1 faction ships are available without enabling T2 or the opposing faction.</summary>
    [TestCase(WGCrewFaction.TSF, "Shiv", "Calypso", "Fujian", "Bazalt")]
    [TestCase(WGCrewFaction.PDV, "Bazalt", "Scorpion", "Neptune", "Shiv")]
    public async Task FactionCatalogIncludesTierZeroAndOne(WGCrewFaction faction, string zero, string one, string two, string other)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        await pair.Server.WaitAssertion(() =>
        {
            var ships = pair.Server.System<WGShipScenarioSystem>();
            var catalog = ships.GetVessels(faction).Select(ship => ship.Id).ToArray();
            Assert.That(catalog, Does.Contain(zero));
            Assert.That(catalog, Does.Contain(one));
            Assert.That(catalog, Does.Not.Contain(two));
            Assert.That(catalog, Does.Not.Contain(other));
            Assert.That(ships.IsVesselAvailable(zero, faction), Is.True);
            Assert.That(ships.IsVesselAvailable(two, faction), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
