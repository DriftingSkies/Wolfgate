#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.ShipAccess;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.StationRecords;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>
/// An outpost takes ship access, registered to its owner: a card on its allow list opens its locked doors but not its
/// console, and is listed again, under its character's new card, when the outpost is loaded in a later round.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostAccessTest
{
    private const string CrewName = "Cora Crew";

    [Test]
    public async Task CrewKeepAccessThroughSaves()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var shipAccess = server.System<WFShipAccessServerSystem>();
        var readers = server.System<AccessReaderSystem>();
        var maps = server.System<SharedMapSystem>();
        var db = server.ResolveDependency<IServerDbManager>();
        var ticker = server.System<GameTicker>();
        var owner = pair.Player!.UserId;
        // The crew member's player has been here and is away: their mind keeps their account and character, as for a
        // disconnected player.
        var crew = new NetUserId(Guid.NewGuid());
        await server.WaitPost(() =>
        {
            server.PlayerMan.CreateAndAddSession(crew, "cora");
            server.PlayerMan.RemoveSession(crew);
        });

        var funding = Task.CompletedTask;
        EntityUid crewBody = default;
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            // Stations live in nullspace, off the outpost's map.
            station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            server.CfgMan.SetCVar(OutpostCVars.LoadDelay, 1f);
            var prefs = server.ResolveDependency<IServerPreferencesManager>();
            var p = prefs.GetPreferences(owner);
            funding = prefs.SetProfile(owner, p.SelectedCharacterIndex, ((HumanoidCharacterProfile) p.SelectedCharacter).WithBankBalance(1_000_000));
            entMan.EnsureComponent<BankAccountComponent>(user);

            crewBody = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, -2.5f, 0.5f));
            entMan.System<MetaDataSystem>().SetEntityName(crewBody, CrewName);
            var minds = server.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(crew, CrewName), crewBody);
        });
        await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);
        await pair.RunTicksSync(5);

        // A card keyed to a crew record, as the station records give a spawning player.
        EntityUid GiveCard(EntityUid person, uint record)
        {
            var card = entMan.SpawnEntity("PassengerIDCard", entMan.GetComponent<TransformComponent>(person).Coordinates);
            entMan.EnsureComponent<StationRecordKeyStorageComponent>(card);
            entMan.System<StationRecordKeyStorageSystem>().AssignKey(card, new StationRecordKey(record, station));
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(person, card), "Precondition: the card is picked up.");
            return card;
        }

        async Task Settle()
        {
            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(owner), maxTicks: 600);
        }

        Entity<WFOutpostComponent> Found()
        {
            var site = OutpostFoundingTest.Site;
            Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
            return (grid!.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value));
        }

        bool ConsoleOpens(EntityUid console, EntityUid who)
        {
            var ev = new ActivatableUIOpenAttemptEvent(who);
            entMan.EventBus.RaiseLocalEvent(console, ev);
            return !ev.Cancelled;
        }

        EntityUid Door(EntityUid grid)
        {
            return OnGrid(entMan, grid).Single(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "AirlockShuttle");
        }

        try
        {
            EntityUid first = default;
            EntityUid stranger = default;
            EntityUid oldCard = default;
            await server.WaitAssertion(() =>
            {
                var outpost = Found();
                first = outpost.Owner;
                var access = entMan.GetComponent<WFShipAccessComponent>(first);
                Assert.That(access.OwnerUsers, Is.EquivalentTo(new[] { owner }), "The outpost is registered to its owner's account.");

                var grid = entMan.GetComponent<MapGridComponent>(first);
                entMan.SpawnEntity("AirlockShuttle", maps.GridTileToLocal(first, grid, new Vector2i(0, 0)));
                oldCard = GiveCard(crewBody, 11);
                stranger = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, -2.5f, 1.5f));
                GiveCard(stranger, 12);
                shipAccess.SetLocked((first, access), true);
                Assert.That(shipAccess.TryAddCard((first, access), oldCard, CrewName), "The crew card is listed.");
            });
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var door = Door(first);
                Assert.That(readers.IsAllowed(crewBody, door), Is.True, "The listed card opens the locked outpost.");
                Assert.That(readers.IsAllowed(stranger, door), Is.False, "A stranger's card doesn't.");
                var console = entMan.GetComponent<WFOutpostComponent>(first).Console;
                Assert.That(ConsoleOpens(console, user), Is.True, "The owner uses the console.");
                Assert.That(ConsoleOpens(console, crewBody), Is.False, "Crew don't.");
                Assert.That(outposts.TrySave(user, (first, entMan.GetComponent<WFOutpostComponent>(first)), 0, out var reason), Is.True,
                    $"Saving was refused: {reason}.");
                var grant = entMan.GetComponent<WFOutpostComponent>(first).Access.Single();
                Assert.That(grant.User, Is.EqualTo(crew.ToString()));
                Assert.That(grant.Character, Is.EqualTo(CrewName));
            });
            await Settle();

            // A new round: the old outpost and card are gone, and the crew member's character has a new crew record.
            var row = await db.GetOutpostSaveDataAsync(owner, 0);
            Assert.That(row, Is.Not.Null, "The save is stored.");
            await db.SetOutpostSaveAsync(owner, row!.Value.Save with { SavedRound = ticker.RoundId - 1000, LoadedRound = ticker.RoundId - 1000 }, row.Value.Data);
            await server.WaitPost(() =>
            {
                entMan.DeleteEntity(first);
                entMan.DeleteEntity(oldCard);
                outposts.ForgetSlots(owner);
                outposts.GetSlots(owner);
            });
            await Settle();

            await server.WaitAssertion(() =>
            {
                var outpost = Found();
                Assert.That(outposts.TryStartLoad(user, outpost, 0, out var reason), Is.True, $"The load was refused: {reason}.");
                GiveCard(crewBody, 21);
            });
            await Settle();
            await pair.RunTicksSync(240);

            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.TryGetOutpost(owner, out var loaded), Is.True, "The save landed.");
                Assert.That(loaded.Owner, Is.Not.EqualTo(first));
                var access = entMan.GetComponent<WFShipAccessComponent>(loaded);
                Assert.That(access.Locked, "The lock came back with the save.");
                Assert.That(access.OwnerUsers, Does.Contain(owner), "The loaded outpost is registered to its owner again.");
                Assert.That(access.AllowList.Select(e => e.Name), Is.EquivalentTo(new[] { CrewName }), "The crew member is listed again.");
                Assert.That(loaded.Comp.PendingAccess, Is.Empty);

                var door = Door(loaded);
                Assert.That(readers.IsAllowed(crewBody, door), Is.True, "The crew member's new card opens the loaded outpost.");
                Assert.That(readers.IsAllowed(stranger, door), Is.False, "A stranger's card still doesn't.");
                Assert.That(ConsoleOpens(loaded.Comp.Console, crewBody), Is.False, "Crew still can't use the console.");
            });
        }
        finally
        {
            await server.WaitPost(() => entMan.DeleteEntity(station));
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static List<EntityUid> OnGrid(IEntityManager entMan, EntityUid grid)
    {
        var found = new List<EntityUid>();
        var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
            found.Add(child);
        return found;
    }
}
