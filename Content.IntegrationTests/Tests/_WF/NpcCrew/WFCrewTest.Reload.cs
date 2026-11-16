#nullable enable
using System.Numerics;
using System.Collections.Generic;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A loaded magazine with an empty chamber is racked and can supply a real shot.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CrewChambersLoadedGun(bool marine)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var mob = Server.System<WFCrewSystem>().SpawnCrewman(marine ? WFCrewRoles.Marine : WFCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "chamber")!.Value;
            SEntMan.GetComponent<HTNComponent>(mob).Enabled = false;
            var weapons = Server.System<WFCrewWeaponSystem>();
            weapons.TryDraw(mob);
            var gun = SEntMan.GetComponent<WFCrewWeaponComponent>(mob).Drawn!.Value;
            var slots = Server.System<ItemSlotsSystem>();
            if (slots.TryEject(gun, "gun_chamber", null, out var round))
                SEntMan.DeleteEntity(round.Value);
            Assert.That(weapons.AmmoCount(gun), Is.GreaterThan(0));
            Assert.That(weapons.TryReloadOrSwitch(mob), Is.True);
            var shot = new TakeAmmoEvent(1, new List<(EntityUid? Entity, IShootable Shootable)>(),
                SEntMan.GetComponent<TransformComponent>(mob).Coordinates, mob);
            SEntMan.EventBus.RaiseLocalEvent(gun, shot);
            Assert.That(shot.Ammo.Count, Is.EqualTo(1), "Loaded crew guns must fire rather than merely report magazine rounds.");
        });
    }

    /// <summary>Reloads from a pocket, or switches to a loaded sidearm or empty hands when no ammunition remains.</summary>
    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(false, true)]
    public async Task CrewReloadsOrSwitches(bool spare, bool marine)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = Server.System<WFCrewSystem>().SpawnCrewman(marine ? WFCrewRoles.Marine : WFCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "reload")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            var weapons = Server.System<WFCrewWeaponSystem>();
            var slots = Server.System<ItemSlotsSystem>();
            var inventory = Server.System<InventorySystem>();
            Assert.That(weapons.TryDraw(crew), Is.True);
            var original = SEntMan.GetComponent<WFCrewWeaponComponent>(crew).Drawn!.Value;
            if (slots.TryEject(original, "gun_magazine", null, out var magazine))
                SEntMan.DeleteEntity(magazine.Value);
            if (slots.TryEject(original, "gun_chamber", null, out var cartridge))
                SEntMan.DeleteEntity(cartridge.Value);
            Assert.That(weapons.AmmoCount(original), Is.Zero);
            if (!spare)
            {
                foreach (var name in new[] { "pocket1", "pocket2" })
                {
                    if (inventory.TryGetSlotEntity(crew, name, out var item))
                        SEntMan.DeleteEntity(item.Value);
                }
            }
            var loaded = weapons.TryReloadOrSwitch(crew);
            var drawn = SEntMan.GetComponent<WFCrewWeaponComponent>(crew).Drawn;
            if (spare)
            {
                Assert.That(loaded, Is.True);
                Assert.That(drawn, Is.EqualTo(original));
                Assert.That(weapons.AmmoCount(original), Is.GreaterThan(0));
                Assert.That(inventory.TryGetSlotEntity(crew, "pocket1", out _), Is.False);
            }
            else if (marine)
            {
                Assert.That(drawn, Is.Not.Null.And.Not.EqualTo(original));
                Assert.That(weapons.AmmoCount(drawn!.Value), Is.GreaterThan(0));
            }
            else
                Assert.That(drawn, Is.Null);
        });
    }

    /// <summary>A wielded rifle with a spent magazine in it reloads from a pocket and stays wielded; the spent magazine takes the pocket.</summary>
    [Test]
    public async Task WieldedRifleReloadsFromAPocket()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var posts = new List<WFCrewSetupPost> { new() { Position = new Vector2(2.5f), Loadout = "WFCrewPirateRifle" } };
            var mission = new WFCrewMission { Profile = "WFCrewProfilePirate", Group = "wielded", Skill = WFCrewSkill.Regular };
            Assert.That(Server.System<WFCrewSetupSystem>().TrySpawn(deck, posts, mission, false, out var spawned), Is.True);
            var crew = spawned[0];
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            var weapons = Server.System<WFCrewWeaponSystem>();
            var slots = Server.System<ItemSlotsSystem>();
            var inventory = Server.System<InventorySystem>();

            Assert.That(weapons.TryDraw(crew), Is.True);
            var rifle = SEntMan.GetComponent<WFCrewWeaponComponent>(crew).Drawn!.Value;
            Assert.That(SEntMan.GetComponent<WieldableComponent>(rifle).Wielded, Is.True, "Precondition: the rifle is wielded.");
            Assert.That(slots.TryGetSlot(rifle, "gun_magazine", out var slot) && slot.Item is { }, Is.True);
            SEntMan.DeleteEntity(slot!.Item!.Value);
            var spent = SEntMan.SpawnAtPosition("Magazine68x52mmCaselessEmpty", new EntityCoordinates(deck, new Vector2(2.5f)));
            Assert.That(slots.TryInsert(rifle, slot, spent, null), Is.True);
            if (slots.TryEject(rifle, "gun_chamber", null, out var round))
                SEntMan.DeleteEntity(round.Value);
            Assert.That(weapons.AmmoCount(rifle), Is.Zero);
            Assert.That(inventory.TryGetSlotEntity(crew, "pocket1", out var spare) && weapons.AmmoCount(spare.Value) > 0, Is.True,
                "Precondition: a loaded spare in the first pocket.");

            Assert.That(weapons.TryReloadOrSwitch(crew), Is.True);
            Assert.That(slot.Item, Is.EqualTo(spare));
            Assert.That(weapons.AmmoCount(rifle), Is.GreaterThan(0));
            Assert.That(SEntMan.GetComponent<WFCrewWeaponComponent>(crew).Drawn, Is.EqualTo(rifle));
            Assert.That(SEntMan.GetComponent<WieldableComponent>(rifle).Wielded, Is.True, "The rifle stays wielded.");
            Assert.That(inventory.TryGetSlotEntity(crew, "pocket1", out var pocket) && pocket == spent, Is.True,
                "The spent magazine takes the spare's pocket.");
        });
    }
}
