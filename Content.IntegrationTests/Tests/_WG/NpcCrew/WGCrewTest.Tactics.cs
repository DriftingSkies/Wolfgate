#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Explosion.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    private static readonly WGCrewGrenadeRules GrenadeRules = new(4.5f, 9f, 8f, 3f, 3f, 20f, 1.5f);

    /// <summary>The skill table's tiers and tactics, and a crewman's offset clamped to Green..Elite.</summary>
    [Test]
    public async Task SkillTableAndOffsetsClamp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGCrewSkills.Of(WGCrewSkill.Green).DamageTaken, Is.EqualTo(1.3f));
            Assert.That(WGCrewSkills.Of(WGCrewSkill.Regular).DamageTaken, Is.EqualTo(1.15f));
            Assert.That(WGCrewSkills.Of(WGCrewSkill.Veteran).DamageTaken, Is.EqualTo(1f));
            Assert.That(WGCrewSkills.Of(WGCrewSkill.Elite).DamageTaken, Is.EqualTo(1f));

            var green = WGCrewSkills.Of(WGCrewSkill.Green);
            Assert.That(green.Braces || green.Throws || green.Sidesteps || green.FallsBack, Is.False, "Greens fight as before.");
            var regular = WGCrewSkills.Of(WGCrewSkill.Regular);
            Assert.That(regular.Braces && regular.Throws, Is.True);
            Assert.That(regular.CookTime, Is.Zero);
            Assert.That(regular.HuntParty, Is.EqualTo(1));
            var veteran = WGCrewSkills.Of(WGCrewSkill.Veteran);
            Assert.That(veteran.Sidesteps && veteran.FallsBack, Is.True);
            Assert.That(veteran.HuntParty, Is.EqualTo(2));
            Assert.That(WGCrewSkills.Of(WGCrewSkill.Elite).CookTime, Is.GreaterThan(veteran.CookTime));

            var crew = new WGCrewComponent { Skill = WGCrewSkill.Green, SkillOffset = -1 };
            Assert.That(WGCrewSkills.Effective(crew), Is.EqualTo(WGCrewSkill.Green));
            crew.Skill = WGCrewSkill.Elite;
            crew.SkillOffset = 2;
            Assert.That(WGCrewSkills.Effective(crew), Is.EqualTo(WGCrewSkill.Elite));
            crew.Skill = WGCrewSkill.Regular;
            crew.SkillOffset = 1;
            Assert.That(WGCrewSkills.Effective(crew), Is.EqualTo(WGCrewSkill.Veteran));
            Assert.That(WGCrewSkills.Of(crew), Is.EqualTo(veteran));
        });
        await Task.CompletedTask;
    }

    /// <summary>
    /// A crewman regaining sight of a target he saw moments ago fires after his re-acquire delay; one meeting it for
    /// the first time waits the full delay, and so does an NPC that is not crew.
    /// </summary>
    [Test]
    public async Task CrewReacquireAnExpectedTargetSooner()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        EntityUid expecting = default, fresh = default, outsider = default;
        await Server.WaitAssertion(() =>
        {
            var weapons = Server.System<WGCrewWeaponSystem>();
            var hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(5.5f, 3.5f)));
            expecting = ConvoyCrew(deck, "WGCrewDeckhand", "reacquire", new Vector2(1.5f, 1.5f));
            fresh = ConvoyCrew(deck, "WGCrewDeckhand", "reacquire", new Vector2(1.5f, 3.5f));
            outsider = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(1.5f, 5.5f)));
            var pistol = SEntMan.SpawnAtPosition("WeaponPistolMk58", new EntityCoordinates(deck, new Vector2(1.5f, 5.5f)));
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(outsider, pistol, checkActionBlocker: false), Is.True);

            foreach (var shooter in new[] { expecting, fresh })
                Assert.That(weapons.TryDraw(shooter), Is.True, $"Precondition: a sidearm to draw. {Describe(shooter)}");
            foreach (var shooter in new[] { expecting, fresh, outsider })
            {
                var ranged = SEntMan.EnsureComponent<NPCRangedCombatComponent>(shooter);
                ranged.Target = hostile;
                ranged.Status = CombatStatus.Normal;
                ranged.TargetInLOS = false;
                ranged.LOSAccumulator = 0f;
                ranged.ShootAccumulator = 0f;
            }

            weapons.Saw((expecting, SEntMan.GetComponent<WGCrewWeaponComponent>(expecting)), hostile);
        });

        await RunTicks(1);
        await Server.WaitAssertion(() =>
        {
            var skill = WGCrewSkills.Of(SEntMan.GetComponent<WGCrewComponent>(expecting));
            var quick = SEntMan.GetComponent<NPCRangedCombatComponent>(expecting);
            Assert.That(quick.TargetInLOS, Is.True, "Precondition: the target is in sight again.");
            Assert.That(quick.ShootAccumulator, Is.GreaterThanOrEqualTo(quick.ShootDelay - skill.ReacquireDelay - 0.001f),
                "A target seen moments ago is fired on after the re-acquire delay.");
            Assert.That(SEntMan.GetComponent<NPCRangedCombatComponent>(fresh).ShootAccumulator, Is.LessThan(0.1f),
                "First contact waits the full delay.");
            Assert.That(SEntMan.GetComponent<NPCRangedCombatComponent>(outsider).ShootAccumulator, Is.LessThan(0.1f),
                "Other NPCs are unchanged.");
        });
    }

    /// <summary>
    /// With the hunt not his to take, a regular crewman covers a known hostile behind a wall with his weapon drawn,
    /// and holsters once he no longer knows of it; a green crewman does not brace at all.
    /// </summary>
    [Test]
    public async Task RegularCrewBraceAndGreensDoNot()
    {
        var hands = Server.System<SharedHandsSystem>();
        var deck = await CreateDeck(new Vector2(6, 0), 11, gravity: true);
        EntityUid regular = default, green = default, hostile = default;
        await Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 11; x++)
                SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(x + 0.5f, 4.5f)));
            hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(5.5f, 8.5f)));
            regular = ConvoyCrew(deck, "WGCrewDeckhand", "brace", new Vector2(2.5f, 1.5f));
            green = ConvoyCrew(deck, "WGCrewDeckhand", "brace", new Vector2(7.5f, 1.5f));
            var heard = STiming.CurTime + TimeSpan.FromSeconds(600);
            foreach (var (member, skill) in new[] { (regular, WGCrewSkill.Regular), (green, WGCrewSkill.Green) })
            {
                var crew = SEntMan.GetComponent<WGCrewComponent>(member);
                crew.Skill = skill;
                // He may not go after it himself, so knowing of it can only make him brace.
                crew.Pursues = false;
                crew.RadioSightings[hostile] = heard;
                SEntMan.GetComponent<HTNComponent>(member).Enabled = true;
            }

            var weapons = Server.System<WGCrewWeaponSystem>();
            Assert.That(weapons.CanSee(regular, hostile), Is.False, "Precondition: the wall hides the hostile.");
            Assert.That(weapons.Assess(regular, hostile), Is.EqualTo(WGCrewEngage.Brace));
            Assert.That(weapons.Assess(green, hostile), Is.EqualTo(WGCrewEngage.No));
        });

        await WaitUntil(() => hands.TryGetActiveItem(regular, out var held) && SEntMan.HasComponent<GunComponent>(held.Value)
                && Server.System<WGCrewWeaponSystem>().IsBracing((regular, SEntMan.GetComponent<WGCrewWeaponComponent>(regular))),
            300, () => $"A regular should draw and brace for a hostile he knows of. {Describe(regular)}");
        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(hands.TryGetActiveItem(green, out _), Is.False, $"A green should not brace. {Describe(green)}");
            Assert.That(hands.TryGetActiveItem(regular, out var held) && SEntMan.HasComponent<GunComponent>(held.Value), Is.True,
                "The brace holds while he knows of the hostile.");
            SEntMan.GetComponent<WGCrewComponent>(regular).RadioSightings.Remove(hostile);
        });

        await WaitUntil(() => !hands.TryGetActiveItem(regular, out _), 600,
            () => $"He should holster once he no longer knows of the hostile. {Describe(regular)}");
    }

    /// <summary>
    /// A crewman throws a grenade round the corner a known hostile holds: it lands within the blast of the hostile,
    /// an elite's cook leaves at least the minimum fuse, a green never throws, nobody throws with a crewmate in the
    /// blast or inside the crew's cooldown, and an interrupted thrower lets the armed grenade go.
    /// </summary>
    [Test]
    public async Task CrewThrowGrenadesRoundACorner()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 13, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var grenades = Server.System<WGCrewGrenadeSystem>();
            var weapons = Server.System<WGCrewWeaponSystem>();
            var inventory = Server.System<InventorySystem>();
            var hands = Server.System<SharedHandsSystem>();
            var xform = Server.System<SharedTransformSystem>();

            // A wall between the thrower's corner and the hostile, open at its left end.
            for (var x = 5; x < 13; x++)
                SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(x + 0.5f, 4.5f)));
            var hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(8.5f, 6.5f)));
            var thrower = new Vector2(1.5f, 1.5f);

            EntityUid Thrower(WGCrewSkill skill, Vector2? at = null)
            {
                var member = ConvoyCrew(deck, "WGCrewDeckhand", "frag", at ?? thrower);
                var crew = SEntMan.GetComponent<WGCrewComponent>(member);
                crew.Skill = skill;
                crew.RadioSightings[hostile] = STiming.CurTime + TimeSpan.FromSeconds(60);
                var weapon = SEntMan.EnsureComponent<WGCrewWeaponComponent>(member);
                weapon.Noticed[hostile] = STiming.CurTime - TimeSpan.FromSeconds(5);
                inventory.TryUnequip(member, "pocket2", silent: true, force: true);
                var grenade = SEntMan.SpawnAtPosition("ExGrenade", new EntityCoordinates(deck, at ?? thrower));
                Assert.That(inventory.TryEquip(member, grenade, "pocket2", silent: true, force: true), Is.True);
                return member;
            }

            var green = Thrower(WGCrewSkill.Green);
            Assert.That(grenades.TryPlan(green, hostile, GrenadeRules, out _, out _, out _), Is.False, "Greens never throw.");
            SEntMan.DeleteEntity(green);

            var careful = Thrower(WGCrewSkill.Regular);
            Assert.That(weapons.CanSee(careful, hostile), Is.False, "Precondition: the wall hides the hostile.");
            var mate = ConvoyCrew(deck, "WGCrewDeckhand", "frag", new Vector2(6.5f, 6.5f));
            Assert.That(grenades.TryPlan(careful, hostile, GrenadeRules, out _, out _, out _), Is.False,
                "Never with a crewmate in the blast.");
            SEntMan.DeleteEntity(mate);
            SEntMan.DeleteEntity(careful);

            var elite = Thrower(WGCrewSkill.Elite);
            Assert.That(grenades.TryPlan(elite, hostile, GrenadeRules, out var grenade, out var target, out var at), Is.True,
                $"An elite should find somewhere to throw. {Describe(elite)}");
            Assert.That(target, Is.EqualTo(hostile));
            var landing = xform.ToMapCoordinates(at).Position;
            var there = xform.GetWorldPosition(hostile);
            Assert.That(Vector2.Distance(landing, there), Is.LessThanOrEqualTo(GrenadeRules.LandNear));
            Assert.That(Vector2.Distance(landing, there), Is.LessThan(GrenadeRules.BlastRadius));
            Assert.That(Vector2.Distance(landing, xform.GetWorldPosition(elite)), Is.GreaterThan(GrenadeRules.BlastRadius));

            Assert.That(grenades.Ready(elite, grenade, target, at, GrenadeRules), Is.True);
            var armed = SEntMan.GetComponent<WGCrewArmedGrenadeComponent>(elite);
            Assert.That(hands.IsHolding(elite, grenade, out _), Is.True, "The grenade is in his hand.");
            Assert.That(SEntMan.HasComponent<ActiveTimerTriggerComponent>(grenade), Is.True, "It is armed.");
            Assert.That(weapons.BlastHeld(hostile), Is.True, "Nobody hunts into the blast.");
            var fuse = SEntMan.GetComponent<OnUseTimerTriggerComponent>(grenade).Delay;
            var cook = (float) (armed.ThrowTime - STiming.CurTime).TotalSeconds;
            var flight = Vector2.Distance(landing, xform.GetWorldPosition(elite)) / 11f;
            Assert.That(cook, Is.GreaterThan(0f), "An elite cooks it.");
            Assert.That(fuse - cook - flight, Is.GreaterThanOrEqualTo(GrenadeRules.MinFuse - 0.01f),
                "The minimum fuse is left when it lands.");

            grenades.Release(elite);
            Assert.That(SEntMan.HasComponent<WGCrewArmedGrenadeComponent>(elite), Is.False);
            Assert.That(hands.IsHolding(elite, grenade, out _), Is.False, "An interrupted thrower never keeps an armed grenade.");

            // A fresh hostile where the first stood, with no grenade on its way, so only the cooldown can refuse it.
            SEntMan.DeleteEntity(hostile);
            SEntMan.DeleteEntity(elite);
            var fresh = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(8.5f, 6.5f)));
            void Knows(EntityUid member)
            {
                SEntMan.GetComponent<WGCrewComponent>(member).RadioSightings[fresh] = STiming.CurTime + TimeSpan.FromSeconds(60);
                SEntMan.GetComponent<WGCrewWeaponComponent>(member).Noticed[fresh] = STiming.CurTime - TimeSpan.FromSeconds(5);
            }

            var next = Thrower(WGCrewSkill.Elite);
            Knows(next);
            Assert.That(weapons.BlastHeld(fresh), Is.False, "Precondition: nothing is thrown at the fresh hostile.");
            Assert.That(grenades.TryPlan(next, fresh, GrenadeRules, out _, out _, out _), Is.False,
                "The crew waits out its cooldown.");

            // Another crew, outside that cooldown, can throw at the same hostile from just behind him.
            var behind = new Vector2(1.5f, 2.5f);
            var outsider = ConvoyCrew(deck, "WGCrewDeckhand", "other", behind);
            SEntMan.GetComponent<WGCrewComponent>(outsider).Skill = WGCrewSkill.Elite;
            SEntMan.EnsureComponent<WGCrewWeaponComponent>(outsider);
            Knows(outsider);
            inventory.TryUnequip(outsider, "pocket2", silent: true, force: true);
            Assert.That(inventory.TryEquip(outsider, SEntMan.SpawnAtPosition("ExGrenade", new EntityCoordinates(deck, behind)),
                "pocket2", silent: true, force: true), Is.True);
            Assert.That(grenades.TryPlan(outsider, fresh, GrenadeRules, out _, out _, out _), Is.True,
                $"Control: without the cooldown the throw is on. {Describe(outsider)}");
        });
    }

    /// <summary>
    /// Two crewmen who planned together throw only one grenade, and a crewmate who steps into the blast during the
    /// cook is never thrown at.
    /// </summary>
    [Test]
    public async Task GrenadeChecksAgainBeforeItLeavesTheHand()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 13, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var grenades = Server.System<WGCrewGrenadeSystem>();
            var inventory = Server.System<InventorySystem>();
            var hands = Server.System<SharedHandsSystem>();
            var xform = Server.System<SharedTransformSystem>();

            for (var x = 5; x < 13; x++)
                SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(x + 0.5f, 4.5f)));
            var hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(8.5f, 6.5f)));

            EntityUid Thrower(Vector2 at)
            {
                var member = ConvoyCrew(deck, "WGCrewDeckhand", "twin", at);
                var crew = SEntMan.GetComponent<WGCrewComponent>(member);
                crew.Skill = WGCrewSkill.Elite;
                crew.RadioSightings[hostile] = STiming.CurTime + TimeSpan.FromSeconds(60);
                var weapon = SEntMan.EnsureComponent<WGCrewWeaponComponent>(member);
                weapon.Noticed[hostile] = STiming.CurTime - TimeSpan.FromSeconds(5);
                inventory.TryUnequip(member, "pocket2", silent: true, force: true);
                var grenade = SEntMan.SpawnAtPosition("ExGrenade", new EntityCoordinates(deck, at));
                Assert.That(inventory.TryEquip(member, grenade, "pocket2", silent: true, force: true), Is.True);
                return member;
            }

            var first = Thrower(new Vector2(1.5f, 1.5f));
            // Off to one side, so his throw does not pass over the first.
            var second = Thrower(new Vector2(2.5f, 0.5f));
            Assert.That(grenades.TryPlan(first, hostile, GrenadeRules, out var grenade, out var target, out var at), Is.True,
                $"The first should find somewhere to throw. {Describe(first)}");
            Assert.That(grenades.TryPlan(second, hostile, GrenadeRules, out var other, out var otherTarget, out var otherAt), Is.True,
                "Planned in the same moment, the second sees no cooldown yet.");
            Assert.That(grenades.Ready(first, grenade, target, at, GrenadeRules), Is.True);
            Assert.That(grenades.Ready(second, other, otherTarget, otherAt, GrenadeRules), Is.False,
                "The second must not arm inside the cooldown the first just started.");
            Assert.That(hands.IsHolding(second, other, out _), Is.False);

            // A crewmate steps up beside the hostile while the grenade cooks: every spot that reaches it is now in
            // the crewmate's blast, and the fuse has time to spare, so he keeps hold.
            var mate = ConvoyCrew(deck, "WGCrewDeckhand", "twin", new Vector2(9.5f, 6.5f));
            grenades.Release(first);
            Assert.That(SEntMan.HasComponent<WGCrewArmedGrenadeComponent>(first), Is.True,
                "A grenade must not be thrown with a crewmate in its blast while the fuse can wait.");
            Assert.That(hands.IsHolding(first, grenade, out _), Is.True, "Held back, it stays in his hand.");

            // The crewmate moves off and the throw goes.
            SEntMan.DeleteEntity(mate);
            grenades.Release(first);
            Assert.That(SEntMan.HasComponent<WGCrewArmedGrenadeComponent>(first), Is.False);
            Assert.That(hands.IsHolding(first, grenade, out _), Is.False, "Once the way is clear he throws.");

            SEntMan.DeleteEntity(grenade);
        });
    }

    /// <summary>A reaver hand stands a tier below his crew and an officer a tier above; a settings round trip keeps both.</summary>
    [Test]
    public async Task ReaverSkillOffsetsSurviveASettingsRoundTrip()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var setup = Server.System<WGCrewSetupSystem>();
            var posts = new List<WGCrewSetupPost>
            {
                new() { Position = new Vector2(1.5f), Loadout = "WGCrewPirateHand" },
                new() { Position = new Vector2(3.5f), Loadout = "WGCrewPirateOfficer" },
                new() { Position = new Vector2(5.5f), Loadout = "WGCrewPirateRifle" },
            };
            var mission = new WGCrewMission { Profile = "WGCrewProfilePirate", Group = "reavers", Skill = WGCrewSkill.Regular };
            Assert.That(setup.TrySpawn(deck, posts, mission, false, out var spawned), Is.True);
            foreach (var mob in spawned)
                SEntMan.GetComponent<HTNComponent>(mob).Enabled = false;

            WGCrewSkill Effective(EntityUid mob) => WGCrewSkills.Effective(SEntMan.GetComponent<WGCrewComponent>(mob));
            Assert.That(Effective(spawned[0]), Is.EqualTo(WGCrewSkill.Green));
            Assert.That(Effective(spawned[1]), Is.EqualTo(WGCrewSkill.Veteran));
            Assert.That(Effective(spawned[2]), Is.EqualTo(WGCrewSkill.Regular));
            Assert.That(Server.System<InventorySystem>().TryGetSlotEntity(spawned[2], "belt", out var belt)
                && SEntMan.HasComponent<OnUseTimerTriggerComponent>(belt.Value), Is.True, "Reaver riflemen carry a grenade.");

            // Crew Setup reads back the crew's own skill and applies it again.
            var reread = new WGCrewMission
            {
                Profile = "WGCrewProfilePirate",
                Group = "reavers",
                Skill = SEntMan.GetComponent<WGCrewComponent>(spawned[0]).Skill,
            };
            Assert.That(setup.TryApplyMission(deck, reread), Is.True);
            Assert.That(Effective(spawned[0]), Is.EqualTo(WGCrewSkill.Green));
            Assert.That(Effective(spawned[1]), Is.EqualTo(WGCrewSkill.Veteran));
            Assert.That(Effective(spawned[2]), Is.EqualTo(WGCrewSkill.Regular));
        });
    }

    /// <summary>A rifleman with his rifle wielded frees a hand and readies his grenade, and gets his grip back after.</summary>
    [Test]
    public async Task WieldingRiflemanReadiesHisGrenade()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 11, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var posts = new List<WGCrewSetupPost> { new() { Position = new Vector2(1.5f), Loadout = "WGCrewPirateRifle" } };
            var mission = new WGCrewMission { Profile = "WGCrewProfilePirate", Group = "wielding", Skill = WGCrewSkill.Regular };
            Assert.That(Server.System<WGCrewSetupSystem>().TrySpawn(deck, posts, mission, false, out var spawned), Is.True);
            var rifleman = spawned[0];
            SEntMan.GetComponent<HTNComponent>(rifleman).Enabled = false;
            var weapons = Server.System<WGCrewWeaponSystem>();
            var hands = Server.System<SharedHandsSystem>();
            Assert.That(weapons.TryDraw(rifleman), Is.True);
            var rifle = SEntMan.GetComponent<WGCrewWeaponComponent>(rifleman).Drawn!.Value;
            Assert.That(SEntMan.GetComponent<WieldableComponent>(rifle).Wielded, Is.True, "Precondition: the rifle is wielded.");
            Assert.That(Server.System<InventorySystem>().TryGetSlotEntity(rifleman, "belt", out var grenade), Is.True);

            var hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(8.5f, 1.5f)));
            var at = new EntityCoordinates(deck, new Vector2(8.5f, 1.5f));
            Assert.That(Server.System<WGCrewGrenadeSystem>().Ready(rifleman, grenade!.Value, hostile, at, GrenadeRules), Is.True,
                "Wielding never keeps him from taking the grenade in hand.");
            Assert.That(hands.IsHolding(rifleman, grenade.Value, out _), Is.True);
            Assert.That(hands.IsHolding(rifleman, rifle, out _), Is.True, "He keeps the rifle in his other hand.");

            Server.System<WGCrewGrenadeSystem>().Release(rifleman);
            Assert.That(SEntMan.GetComponent<WieldableComponent>(rifle).Wielded, Is.True, "He takes his grip again.");
            SEntMan.DeleteEntity(grenade.Value);
        });
    }
}
