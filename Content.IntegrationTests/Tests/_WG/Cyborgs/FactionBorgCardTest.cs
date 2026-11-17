#nullable enable
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.Cyborgs;
using Content.Shared._NF.Shipyard;
using Content.Shared._WG.Cyborgs;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>A faction cyborg's issued card carries its faction's access, so the faction shipyard sells to it.</summary>
[TestFixture]
[TestOf(typeof(WGBorgIdCardServerSystem))]
public sealed class FactionBorgCardTest
{
    [TestCase("TsfBorg", "PlayerBorgTSF", "ComputerShipyardNfsd", ShipyardConsoleUiKey.Security, "Shiv", "Tsfmc")]
    [TestCase("PdvBorg", "PlayerBorgPDV", "ComputerShipyardBlackMarket", ShipyardConsoleUiKey.BlackMarket, "Kynda", "PDV")]
    public async Task IssuedCardBuysFactionShipTest(string job, string chassis, string consoleProto, ShipyardConsoleUiKey key, string vessel, string tag)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var session = pair.Player!;

        await server.WaitAssertion(() =>
        {
            var shipyard = entMan.System<ShipyardSystem>();
            var console = entMan.SpawnEntity(consoleProto, map.GridCoords);
            var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);

            // A blank card is refused, so the gate this test passes is real.
            var blank = entMan.SpawnEntity("WGBorgIdCard", map.GridCoords);
            Assert.That(shipyard.GetAvailableShuttles(console, key, targetId: blank).available, Does.Not.Contain(vessel),
                $"{vessel} should need {tag} access.");

            var borg = entMan.SpawnEntity(chassis, map.GridCoords);
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
            entMan.EventBus.RaiseLocalEvent(borg, new PlayerSpawnCompleteEvent(borg, session, job, false, true, 1, station, profile), true);

            Assert.That(entMan.System<WGBorgIdCardSystem>().TryGetCard(borg, out var card), Is.True, $"{job} was issued no ID card.");
            Assert.That(entMan.GetComponent<AccessComponent>(card).Tags, Does.Contain(new ProtoId<AccessLevelPrototype>(tag)),
                $"{job}'s card lacks {tag} access.");
            Assert.That(shipyard.GetAvailableShuttles(console, key, targetId: card).available, Does.Contain(vessel),
                $"{job}'s card was refused {vessel}.");

            entMan.DeleteEntity(borg);
            entMan.DeleteEntity(blank);
            entMan.DeleteEntity(console);
            entMan.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }
}
