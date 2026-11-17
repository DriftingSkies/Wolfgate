#nullable enable
using System.Numerics;
using System.Linq;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Radio.EntitySystems;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Inventory;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Server.NPC.Components;
using Content.Server.Damage.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Spent chambers cannot keep an exhausted gun selected.</summary>
    [Test]
    public async Task SpentChamberFallsBackToUnarmed()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "spent")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            var weapons = Server.System<WGCrewWeaponSystem>();
            Assert.That(weapons.TryDraw(crew), Is.True);
            var gun = SEntMan.GetComponent<WGCrewWeaponComponent>(crew).Drawn!.Value;
            weapons.TryReloadOrSwitch(crew);
            var slots = Server.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(gun, "gun_chamber", out var chamber), Is.True);
            Assert.That(chamber!.Item, Is.Not.Null);
            SEntMan.GetComponent<CartridgeAmmoComponent>(chamber.Item!.Value).Spent = true;
            if (slots.TryEject(gun, "gun_magazine", null, out var magazine))
                SEntMan.DeleteEntity(magazine.Value);
            var inventory = Server.System<InventorySystem>();
            foreach (var name in new[] { "pocket1", "pocket2" })
            {
                if (inventory.TryGetSlotEntity(crew, name, out var ammo))
                    SEntMan.DeleteEntity(ammo.Value);
            }
            Assert.That(weapons.AmmoCount(gun), Is.Zero);
            Assert.That(weapons.TryReloadOrSwitch(crew), Is.False);
            Assert.That(Server.System<SharedHandsSystem>().TryGetActiveItem(crew, out _), Is.False);
        });
    }

    /// <summary>Unarmed crew fight aboard without scavenging a loose empty gun or chasing another grid.</summary>
    [Test]
    public async Task EmptyCrewDoesNotScavengeOrChaseOffShip()
    {
        var deck = await CreateDeck(new Vector2(20, 20), 11, gravity: true);
        var other = await CreateDeck(new Vector2(33, 20), 5, gravity: true);
        EntityUid crew = default, hostile = default, loose = default;
        await Server.WaitAssertion(() =>
        {
            crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "unarmed")!.Value;
            var inventory = Server.System<InventorySystem>();
            foreach (var name in new[] { "belt", "pocket1", "pocket2" })
            {
                if (inventory.TryGetSlotEntity(crew, name, out var item))
                    SEntMan.DeleteEntity(item.Value);
            }
            loose = SEntMan.SpawnAtPosition("WeaponPistolMk58", new EntityCoordinates(deck, new Vector2(3.5f, 2.5f)));
            var slots = Server.System<ItemSlotsSystem>();
            foreach (var name in new[] { "gun_magazine", "gun_chamber" })
            {
                if (slots.TryEject(loose, name, null, out var item))
                    SEntMan.DeleteEntity(item.Value);
            }
            hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(7.5f, 2.5f)));
            Server.System<GodmodeSystem>().EnableGodmode(hostile);
        });
        await WaitUntil(() => SEntMan.HasComponent<NPCMeleeCombatComponent>(crew), 300, () => Describe(crew));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(loose).ParentUid, Is.EqualTo(deck));
            Assert.That(Server.System<SharedHandsSystem>().TryGetActiveItem(crew, out _), Is.False);
            Server.System<SharedTransformSystem>().SetCoordinates(hostile, new EntityCoordinates(other, new Vector2(1.5f)));
        });
        await RunTicks(70);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCrewWeaponSystem>().CanEngage(crew, hostile), Is.False);
            Assert.That(SEntMan.HasComponent<NPCMeleeCombatComponent>(crew), Is.False);
            Assert.That(SEntMan.GetComponent<TransformComponent>(crew).GridUid, Is.EqualTo(deck));
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
        });
        await RunTicks(90);
    }

    /// <summary>Unseen boarders stay hidden; only a functioning radio relays another crewman's sighting.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task BoardersNeedSightAndWorkingRadio(bool receiverEnabled)
    {
        var deck = await CreateDeck(new Vector2(20, 20), 11, gravity: true);
        EntityUid observer = default, radio = default, visitor = default;
        await Server.WaitAssertion(() =>
        {
            for (var y = 0; y < 11; y++)
            {
                SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(3.5f, y + 0.5f)));
                SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(7.5f, y + 0.5f)));
            }
            var crew = Server.System<WGCrewSystem>();
            observer = crew.SpawnCrewman(WGCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(5.5f, 5.5f)), "watch")!.Value;
            radio = crew.SpawnCrewman(WGCrewRoles.RadioOperator, new EntityCoordinates(deck, new Vector2(1.5f, 5.5f)), "watch")!.Value;
            visitor = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(9.5f, 5.5f)));
            foreach (var member in new[] { observer, radio })
            {
                SEntMan.GetComponent<HTNComponent>(member).Enabled = false;
                SEntMan.EnsureComponent<WGCrewSecurityComponent>(member).Boarding = WGCrewSecurityResponse.Hostile;
            }
        });
        await RunTicks(70);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Sent(radio), Is.Empty, "Walls conceal an unreported intruder.");
            Assert.That(Server.System<WGCrewSecuritySystem>().HasThreat(observer), Is.False);
            var inventory = Server.System<InventorySystem>();
            Assert.That(inventory.TryGetSlotEntity(radio, "ears", out var headset), Is.True);
            Server.System<HeadsetSystem>().SetEnabled(headset!.Value, receiverEnabled);
            Server.System<SharedTransformSystem>().SetCoordinates(visitor, new EntityCoordinates(deck, new Vector2(6.5f, 5.5f)));
        });
        await RunTicks(70);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCrewWeaponSystem>().CanSee(radio, visitor), Is.False);
            Assert.That(Server.System<WGCrewSecuritySystem>().HasThreat(observer), Is.True);
            Assert.That(Server.System<WGCrewCommsSystem>().Knows(radio, visitor), Is.EqualTo(receiverEnabled));
            Assert.That(Sent(radio).Any(line => line.Line == WGRadioLine.BoardWarning && line.Channel.Id == "Traffic"), Is.EqualTo(receiverEnabled));
        });
    }

    /// <summary>Action chatter shares a speaker cooldown and does not repeat the same action immediately.</summary>
    [Test]
    public async Task CrewActionSpeechIsRateLimited()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(2.5f)), "speech")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            var speech = Server.System<WGCrewSpeechSystem>();
            Assert.That(speech.Say(crew, "repair"), Is.False, "Action reports are off until there are voicelines.");
            speech.Enabled = true;
            Assert.That(speech.Say(crew, "repair"), Is.True);
            Assert.That(speech.Say(crew, "repair"), Is.False);
            Assert.That(speech.Say(crew, "collect"), Is.False);
            speech.Enabled = false;
        });
    }
}
