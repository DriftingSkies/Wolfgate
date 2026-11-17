#nullable enable
using Content.Server._NF.SectorServices;
using Content.Server._WG.ShipAccess;
using Content.Server.Silicons.Borgs;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Shared._WG.Cyborgs;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Access.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Station.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>
/// A cyborg's issued ID card gets a crew record, and a crew ship's doors admit it while the card is stowed.
/// </summary>
[TestFixture]
public sealed class BorgShipAccessTest
{
    private const string Borg = "BorgChassisEngineer";
    private const string Cell = "PowerCellHigh";
    private const string Door = "AirlockShuttle";

    [Test]
    public async Task CrewDoorAdmitsStowedCardTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var access = entMan.System<WGShipAccessServerSystem>();
        var credentials = entMan.System<WGShipAccessSystem>();
        var cards = entMan.System<WGBorgIdCardSystem>();
        var readers = entMan.System<AccessReaderSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var minds = entMan.System<SharedMindSystem>();

        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var borg = EntityUid.Invalid;
        var stranger = EntityUid.Invalid;
        var door = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var gridUid = map.Grid.Owner;
            entMan.System<SharedMapSystem>().SetTile(gridUid, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);

            // The record goes on the sector records service, which a station hosts.
            var station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.EnsureComponent<StationRecordsComponent>(station);
            entMan.EnsureComponent<StationSectorServiceHostComponent>(station);
            entMan.System<StationSystem>().AddGridToStation(station, gridUid);

            door = entMan.SpawnEntity(Door, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            borg = entMan.SpawnEntity(Borg, new EntityCoordinates(gridUid, 1.5f, 0.5f));
            stranger = entMan.SpawnEntity(Borg, new EntityCoordinates(gridUid, 1.5f, 0.5f));

            var cell = entMan.SpawnEntity(Cell, new EntityCoordinates(gridUid, 1.5f, 0.5f));
            containers.Insert(cell, containers.GetContainer(borg, "cell_slot"), force: true);

            minds.WipeMind(session.ContentData()?.Mind);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, borg);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(borg), "The cyborg should be the player's body.");
            Assert.That(cards.TryGetCard(borg, out var card), "The cyborg was issued no ID card.");
            entMan.System<BorgSystem>().UnselectModule(borg);
            Assert.That(hands.IsHolding(borg, card), Is.False, "Precondition: the card is stowed in its module.");
            Assert.That(credentials.TryGetWornCard(borg, out var worn) && worn == card,
                "The issued card should count as the cyborg's own.");
            Assert.That(credentials.TryGetKey(card, out var key), "A played cyborg's card should carry a crew record.");

            var ship = new Entity<WGShipAccessComponent>(map.Grid.Owner, entMan.EnsureComponent<WGShipAccessComponent>(map.Grid.Owner));
            access.SetCrew(ship, 1, "Crew", new[] { key });
            access.SetDoorRule(ship, door, WGDoorAccessRule.Crew);

            Assert.That(readers.IsAllowed(borg, door), Is.True, "A crew door should admit a crew cyborg by its stowed card.");
            Assert.That(readers.IsAllowed(stranger, door), Is.False, "A cyborg outside the crew should stay locked out.");
        });

        // A client still attached to a cyborg throws while the pair's cleanup deletes it.
        await server.WaitPost(() => minds.WipeMind(session.ContentData()?.Mind));
        await pair.RunTicksSync(5);

        await pair.CleanReturnAsync();
    }
}
