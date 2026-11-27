#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.ShipAccess;
using Content.Server._WG.IdReissue;
using Content.Server._WG.NanoChat;
using Content.Server._WG.Traders;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.IdReissue;
using Content.Shared._WG.NanoChat;
using Content.Shared._WG.Traders;
using Content.Shared.Access.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Stacks;
using Content.Shared.StationRecords;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.IdReissue;

/// <summary>
/// Reissuing a lost ID through the shipyard dealer: refused without the fee or while carrying a valid card; otherwise
/// the lost cards turn into inert voided cards, the new card keeps the crew record and ColoTalk number and contacts,
/// near ships' deeds move to it (a thief's copy included), a far ship's deed waits until the ship is brought in, and a
/// destroyed card can still be reprinted. An owner without their card is hinted at their own locked door.
/// </summary>
[TestFixture]
[TestOf(typeof(WGIdReissueSystem))]
public sealed class IdReissueTest
{
    private const string HumanProto = "MobHuman";
    private const string CardProto = "PassengerIDCard";
    private const string DealerProto = "WGTraderShipyard";
    private const string DoorProto = "AirlockCommandLocked";
    private const string Name = "Ada Lane";
    private const uint RecordId = 41;

    [Test]
    public async Task DealerReissuesLostIds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var reissue = entMan.System<WGIdReissueSystem>();
        var owners = entMan.System<IdCardOwnerSystem>();
        var traders = entMan.System<TraderSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var chat = entMan.System<NanoChatSystem>();
        var map = await pair.CreateTestMap();
        var key = new StationRecordKey(RecordId, map.MapUid);
        var counter = new EntityCoordinates(map.Grid.Owner, 0.5f, -0.5f);

        EntityUid dealer = default, nearShip = default, farShip = default, door = default;
        EntityUid lost = default, farCard = default, thief = default, card = default;
        uint? chatNumber = null;

        await server.WaitPost(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            for (var x = -4; x < 6; x++)
            for (var y = -4; y < 6; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);

            entMan.SpawnEntity("Table", counter);
            dealer = entMan.SpawnEntity(DealerProto, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
        });

        var body = await AttachPlayer(pair, new EntityCoordinates(map.Grid.Owner, 1.5f, -1.5f));

        await server.WaitAssertion(() =>
        {
            var trader = entMan.GetComponent<TraderComponent>(dealer);
            traders.RefreshTable((dealer, trader));
            Assert.That(traders.TryStartConversation((dealer, trader), body), Is.True, "The customer couldn't talk to the dealer.");

            nearShip = MakeShip(entMan, map, new Vector2(30f, 0f));
            farShip = MakeShip(entMan, map, new Vector2(5000f, 0f));

            // The lost ID holds the near ship; an older card of the same character holds the far one.
            lost = SpawnCard(entMan, map, key);
            owners.Stamp(lost, body);
            ShipAccessTest.GiveDeed(entMan, lost, nearShip);
            var lostChat = entMan.GetComponent<NanoChatCardComponent>(lost);
            chatNumber = lostChat.Number;
            Assert.That(chatNumber, Is.Not.Null, "Precondition: the lost card has a ColoTalk number.");
            chat.SetRecipient((lost, lostChat), 4321, new NanoChatRecipient(4321, "Bram"));
            farCard = SpawnCard(entMan, map, key);
            owners.Stamp(farCard, body);
            ShipAccessTest.GiveDeed(entMan, farCard, farShip);

            // Someone else copied the near ship's deed onto their own card.
            thief = entMan.SpawnEntity(CardProto, map.GridCoords);
            ShipAccessTest.GiveDeed(entMan, thief, nearShip);

            // No cash on the counter and no bank account: the dealer quotes, then refuses, and nothing changes.
            Ask(entMan, dealer, body);
            Assert.That(entMan.GetComponent<WGTraderIdReissueComponent>(dealer).Pending, Is.EqualTo(body), "The dealer didn't quote.");
            Confirm(entMan, dealer, body);
            Assert.That(entMan.Deleted(lost), Is.False, "An unpaid reissue voids nothing.");

            var cash = entMan.SpawnEntity("SpaceCash", counter);
            entMan.System<SharedStackSystem>().SetCount(cash, 1000);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Ask(entMan, dealer, body);
            Confirm(entMan, dealer, body);

            card = hands.EnumerateHeld(body).Single(held => entMan.HasComponent<IdCardComponent>(held));
            Assert.That(entMan.GetComponent<StationRecordKeyStorageComponent>(card).Key, Is.EqualTo(key), "The new card keeps the crew record.");
            Assert.That(entMan.GetComponent<ShuttleDeedComponent>(card).ShuttleUid, Is.EqualTo(nearShip), "The near ship's deed moves to the new card.");
            Assert.That(owners.IsOwnedBy(card, body), Is.True, "The new card is issued to the player.");

            var newChat = entMan.GetComponent<NanoChatCardComponent>(card);
            Assert.That(newChat.Number, Is.EqualTo(chatNumber), "The new card keeps the ColoTalk number.");
            Assert.That(newChat.Recipients.ContainsKey(4321), Is.True, "The new card keeps the ColoTalk contacts.");

            Assert.That(entMan.Deleted(lost) && entMan.Deleted(farCard), Is.True, "The lost cards are gone.");
            var husks = new List<EntityUid>();
            var all = entMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (all.MoveNext(out var uid, out var meta))
            {
                if (meta.EntityPrototype?.ID == "WGVoidIdCard")
                    husks.Add(uid);
            }

            Assert.That(husks, Has.Count.EqualTo(2), "Each lost card leaves a voided card in its place.");
            Assert.That(husks.Any(husk => entMan.HasComponent<IdCardComponent>(husk) || entMan.HasComponent<ShuttleDeedComponent>(husk)), Is.False,
                "A voided card is no ID and holds no deed.");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(thief), Is.False, "A copied deed is revoked too.");
            Assert.That(Holders(entMan, farShip), Is.Empty, "A far ship's deed goes with the voided card.");

            // Carrying a valid card of one's own is refused, so cards are never doubled.
            Ask(entMan, dealer, body);
            Assert.That(entMan.GetComponent<WGTraderIdReissueComponent>(dealer).Pending, Is.Null);
            Assert.That(entMan.GetComponent<TraderComponent>(dealer).CurrentLine, Is.EqualTo(Loc.GetString("wg-id-reissue-carrying")));

            // The near ship is registered to the player: its locked door hints only once they are without their card.
            entMan.EnsureComponent<ShipOwnershipComponent>(nearShip).OwnerUserId = pair.Client.Session!.UserId;
            Assert.That(entMan.GetComponent<ShuttleDeedComponent>(nearShip).ShuttleOwner, Is.EqualTo(Name), "Precondition: the ship's own deed names the character.");
            door = entMan.SpawnEntity(DoorProto, new EntityCoordinates(nearShip, 0.5f, 0.5f));
            var reader = (door, entMan.GetComponent<AccessReaderComponent>(door));
            Assert.That(reissue.NeedsHint(reader, body), Is.False, "An owner carrying their card gets no hint.");

            // The far ship comes back and the new card is destroyed with the near ship's deed on it.
            entMan.System<SharedTransformSystem>().SetWorldPosition(farShip, new Vector2(-30f, 0f));
            entMan.DeleteEntity(card);
            Assert.That(reissue.NeedsHint(reader, body), Is.True, "An owner turned away without their card is hinted.");
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            // Within the cooldown of the last reissue the dealer refuses.
            var settings = entMan.GetComponent<WGTraderIdReissueComponent>(dealer);
            Ask(entMan, dealer, body);
            Assert.That(settings.Pending, Is.Null, "A reissue within the cooldown is refused.");
            Assert.That(entMan.GetComponent<TraderComponent>(dealer).CurrentLine, Is.EqualTo(Loc.GetString("wg-id-reissue-cooldown", ("minutes", 15))));

            // Past it, a second reissue reprints the card, fetches the far deed and finds the near ship by its registration.
            settings.Cooldown = TimeSpan.Zero;
            settings.Fee = 0;
            Ask(entMan, dealer, body);
            Confirm(entMan, dealer, body);

            var holders = Holders(entMan, farShip).Concat(Holders(entMan, nearShip)).ToList();

            Assert.That(holders, Has.Count.EqualTo(2), "Both ships' deeds are on new cards, one each.");
            Assert.That(holders.All(holder => owners.IsOwnedBy(holder, body) && hands.IsHolding(body, holder)), Is.True,
                "Only the player's new cards hold the deeds, both in hand.");
            Assert.That(holders.All(holder => key.Equals(entMan.GetComponent<StationRecordKeyStorageComponent>(holder).Key)), Is.True,
                "A destroyed card is reprinted with its crew record.");
            Assert.That(holders.Any(holder => entMan.GetComponent<NanoChatCardComponent>(holder).Number == chatNumber), Is.True,
                "A destroyed card is reprinted with its ColoTalk number.");

            // Both cards are lost again: the ID and the extra deed card, which shares the crew record but has its own
            // number. The next card takes the character's number, not the extra card's.
            foreach (var held in holders)
                hands.TryDrop(body, held);

            Assert.That(holders.Any(holder => hands.IsHolding(body, holder)), Is.False, "Precondition: both cards are dropped.");
            Ask(entMan, dealer, body);
            Confirm(entMan, dealer, body);
            var reprinted = hands.EnumerateHeld(body)
                .Where(held => entMan.HasComponent<IdCardComponent>(held))
                .Select(held => entMan.GetComponent<NanoChatCardComponent>(held).Number)
                .ToList();
            Assert.That(reprinted, Has.Count.EqualTo(2), "Both ships' deeds come back, one card each.");
            Assert.That(reprinted, Does.Contain(chatNumber), "The reissued ID keeps the character's ColoTalk number, not the extra card's.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The ID cards holding a ship's deed.</summary>
    private static List<EntityUid> Holders(IEntityManager entMan, EntityUid ship)
    {
        var holders = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<ShuttleDeedComponent, IdCardComponent>();
        while (query.MoveNext(out var uid, out var deed, out _))
        {
            if (deed.ShuttleUid == ship)
                holders.Add(uid);
        }

        return holders;
    }

    /// <summary>The customer picks the dealer's "I lost my ID" option.</summary>
    private static void Ask(IEntityManager entMan, EntityUid dealer, EntityUid customer)
    {
        var action = new TraderActionEvent(dealer, customer, TraderAction.ReissueId, null);
        entMan.EventBus.RaiseLocalEvent(dealer, ref action);
        Assert.That(action.Handled, Is.True, "Nobody ran the reissue option.");
    }

    /// <summary>The customer says yes to the quote.</summary>
    private static void Confirm(IEntityManager entMan, EntityUid dealer, EntityUid customer)
    {
        var confirmed = new TraderConfirmedEvent(dealer, customer, true);
        entMan.EventBus.RaiseLocalEvent(dealer, ref confirmed);
    }

    /// <summary>A one-tile grid with a shuttle, as a ship parked at the test station.</summary>
    private static EntityUid MakeShip(IEntityManager entMan, TestMapData map, Vector2 position)
    {
        var maps = entMan.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(map.MapId);
        maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
        entMan.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, position);
        entMan.EnsureComponent<ShuttleComponent>(grid.Owner);
        return grid.Owner;
    }

    /// <summary>An ID card on the station floor carrying the character's crew record.</summary>
    private static EntityUid SpawnCard(IEntityManager entMan, TestMapData map, StationRecordKey key)
    {
        var card = entMan.SpawnEntity(CardProto, map.GridCoords);
        entMan.EnsureComponent<StationRecordKeyStorageComponent>(card);
        entMan.System<StationRecordKeyStorageSystem>().AssignKey(card, key);
        return card;
    }

    /// <summary>Moves the client's player, with a new mind, into a named MobHuman at the given spot.</summary>
    private static async Task<EntityUid> AttachPlayer(TestPair pair, EntityCoordinates coords)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            body = entMan.SpawnEntity(HumanProto, coords);
            entMan.System<MetaDataSystem>().SetEntityName(body, Name);
            var mind = mindSys.CreateMind(session.UserId).Owner;
            mindSys.TransferTo(mind, body);
        });

        await pair.RunTicksSync(5);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "The player did not attach to the body.");
        return body;
    }
}
