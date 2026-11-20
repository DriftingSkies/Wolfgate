#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Server._WG.Augments;
using Content.Server.Cargo.Systems;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._Goobstation.Autosurgeon;
using Content.Shared._WG.Augments;
using Content.Shared._WG.Biotype;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.EntityTable;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// Cyberware of every kind, implants included, is applied to a body and read back from it; a body's paid-for
/// cyberware is written to the profile it belongs to, keeping creator edits; paid-for cyberware taken from another character is
/// the taker's to sell; every augment names real cyberware in the right part; scavenging finds the priced ones only.
/// </summary>
[TestFixture]
[TestOf(typeof(WGAugmentSystem))]
public sealed class AugmentPersistenceTest
{
    private static readonly Dictionary<WGAugmentSlot, WGAugmentChoice> Cyberware = new()
    {
        [WGAugmentSlot.RightArm] = new("WGAugmentJawsOfLifeRightArm", "WGAugmentStyleCyborg"),
        [WGAugmentSlot.LeftHand] = new("WGAugmentDexLeftHand", "WGAugmentStyleCyborg"),
        [WGAugmentSlot.Heart] = new("WGAugmentSandevistan", null),
    };

    private static readonly HashSet<ProtoId<WGAugmentPrototype>> Implants = new()
    {
        "WGAugmentRightMantisBlade",
        "WGAugmentDermalArmor",
    };

    private static void Apply(IEntityManager entities,
        EntityUid mob,
        Dictionary<WGAugmentSlot, WGAugmentChoice> augments,
        HashSet<ProtoId<WGAugmentPrototype>>? implants = null)
    {
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithAugments(augments)
            .WithAugmentImplants(implants ?? new HashSet<ProtoId<WGAugmentPrototype>>());
        var ev = new WGApplyAugmentsEvent(profile);
        entities.EventBus.RaiseLocalEvent(mob, ref ev);
    }

    [Test]
    public async Task ReadBodyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var augments = server.System<WGAugmentSystem>();
        var body = server.System<SharedBodySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            Apply(entities, mob, Cyberware, Implants);

            var read = augments.ReadBody(mob, new Dictionary<WGAugmentSlot, WGAugmentChoice>(), Cyberware);
            Assert.That(read.Keys, Is.EquivalentTo(Cyberware.Keys), "The J.W.L arm's own hand reads as an augment, or one is missing.");
            Assert.That(read[WGAugmentSlot.RightArm].Style?.Id, Is.EqualTo("WGAugmentStyleCyborg"));
            Assert.That(augments.ReadImplants(mob), Is.EquivalentTo(Implants), "The implants put in don't read back.");

            // Nothing picked it, so it wears the trait limbs' style.
            var unpicked = augments.ReadBody(mob, new Dictionary<WGAugmentSlot, WGAugmentChoice>(), new Dictionary<WGAugmentSlot, WGAugmentChoice>());
            Assert.That(unpicked[WGAugmentSlot.RightArm].Style, Is.EqualTo(WGAugmentSystem.TraitLimbStyle));

            // A heart cut out in the round is no longer carried.
            var heart = body.GetBodyOrgans(mob).First(o => o.Component.SlotId == "heart").Id;
            body.RemoveOrgan(heart);
            Assert.That(augments.ReadBody(mob, Cyberware, Cyberware), Does.Not.ContainKey(WGAugmentSlot.Heart));

            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SaveBodyTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var augments = server.System<WGAugmentSystem>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var states = client.ResolveDependency<IStateManager>();
        var map = await pair.CreateTestMap();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState, 600);
        var userId = clientNet.ServerChannel!.UserId;
        const int slot = 0;

        HumanoidCharacterProfile Stored() => (HumanoidCharacterProfile) prefs.GetPreferences(userId).Characters[slot];

        async Task Run(Func<Task?> save)
        {
            Task? task = null;
            await server.WaitPost(() => task = save());
            if (task == null)
                return;
            await PoolManager.WaitUntil(server, () => task.IsCompleted, maxTicks: 300);
            Assert.That(task.IsCompletedSuccessfully, $"The save failed: {task.Exception}");
        }

        // Spawned with a cybernetic left leg, paid-for cyber eyes and dermal armor, and bought a right leg in the
        // creator mid-round.
        var spawned = new Dictionary<WGAugmentSlot, WGAugmentChoice>
        {
            [WGAugmentSlot.LeftLeg] = new("WGAugmentCyberLeftLeg", "WGAugmentStyleCyborg"),
            [WGAugmentSlot.Eyes] = new("WGAugmentCyberEyes", null),
        };
        var spawnedImplants = new HashSet<ProtoId<WGAugmentPrototype>> { "WGAugmentDermalArmor" };
        var edited = new Dictionary<WGAugmentSlot, WGAugmentChoice>(spawned)
        {
            [WGAugmentSlot.RightLeg] = new("WGAugmentCyberRightLeg", "WGAugmentStyleCyborg"),
        };
        await Run(() => prefs.SetProfile(userId, slot, HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithAugments(edited)
            .WithAugmentImplants(spawnedImplants)
            .WithAugmentUnlocks(new ProtoId<WGAugmentPrototype>[] { "WGAugmentDermalArmor", "WGAugmentCyberEyes" })));

        // In the round the left leg and the armor were lost, the eyes kept, and a Sandevistan and muscle fibers put in.
        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            Apply(entities, mob, new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.Heart] = new("WGAugmentSandevistan", null),
                [WGAugmentSlot.Eyes] = new("WGAugmentCyberEyes", null),
            }, new HashSet<ProtoId<WGAugmentPrototype>> { "WGAugmentMuscleFibers" });
        });

        var played = new WGAugmentSystem.PlayedCharacter(slot, EntityUid.Invalid, mob, spawned, spawnedImplants);
        await Run(() => augments.SaveBody(userId, played, mob));
        await server.WaitAssertion(() =>
        {
            // What the round put in was never paid for, so it ends with the round; the eyes bought in the creator stay.
            Assert.That(Stored().Augments.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.RightLeg, WGAugmentSlot.Eyes }));
            Assert.That(Stored().AugmentImplants, Is.Empty);
            Assert.That(Stored().AugmentUnlocks.Select(u => u.Id), Is.EquivalentTo(new[] { "WGAugmentCyberEyes" }));
            Assert.That(Stored().AugmentFound, Is.Empty);
        });

        // Respawning without cryo takes what the character spawned with; the leg picked in the creator since stays.
        await server.WaitPost(() => augments.Track(userId, played));
        await Run(() => augments.ForfeitAugments(players.GetSessionById(userId)));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().Augments.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.RightLeg }));
            Assert.That(Stored().AugmentImplants, Is.Empty);
            Assert.That(Stored().AugmentUnlocks, Is.Empty);
            Assert.That(Stored().AugmentFound, Is.Empty);
            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A player who left before the round ended still has their surviving body written to their stored profile.</summary>
    [Test]
    public async Task SaveOfflineBodyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var db = server.ResolveDependency<IServerDbManager>();
        var augments = server.System<WGAugmentSystem>();
        var map = await pair.CreateTestMap();
        var userId = new NetUserId(Guid.NewGuid());
        const int slot = 0;

        // Spawned with paid-for eyes and a left leg, and lost the leg before leaving.
        var spawned = new Dictionary<WGAugmentSlot, WGAugmentChoice>
        {
            [WGAugmentSlot.LeftLeg] = new("WGAugmentCyberLeftLeg", "WGAugmentStyleCyborg"),
            [WGAugmentSlot.Eyes] = new("WGAugmentCyberEyes", null),
        };
        await db.InitPrefsAsync(userId, HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithAugments(spawned)
            .WithAugmentUnlocks(new ProtoId<WGAugmentPrototype>[] { "WGAugmentCyberLeftLeg", "WGAugmentCyberEyes" }), default);

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            Apply(entities, mob, new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.Eyes] = new("WGAugmentCyberEyes", null),
            });
        });

        var played = new WGAugmentSystem.PlayedCharacter(slot, EntityUid.Invalid, mob, spawned,
            new HashSet<ProtoId<WGAugmentPrototype>>());
        Task? save = null;
        await server.WaitPost(() => save = augments.SaveBody(userId, played, mob));
        Assert.That(save, Is.Not.Null, "Nothing was saved for a player who left.");
        await PoolManager.WaitUntil(server, () => save!.IsCompleted, maxTicks: 300);
        Assert.That(save!.IsCompletedSuccessfully, $"The save failed: {save.Exception}");

        var stored = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(userId, default))!.Characters[slot];
        Assert.That(stored.Augments.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.Eyes }), "The lost leg is still on the profile.");
        Assert.That(stored.AugmentUnlocks.Select(u => u.Id), Is.EquivalentTo(new[] { "WGAugmentCyberEyes" }));

        await server.WaitPost(() => entities.DeleteEntity(mob));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A paid-for heart the body had no slot for never reached it, so neither a save nor a forfeit takes it or its
    /// unlock; the eyes that did go in are saved as before.
    /// </summary>
    [Test]
    public async Task UnfittedTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var db = server.ResolveDependency<IServerDbManager>();
        var augments = server.System<WGAugmentSystem>();
        var spawning = server.System<StationSpawningSystem>();
        var map = await pair.CreateTestMap();
        var userId = new NetUserId(Guid.NewGuid());
        const int slot = 0;

        // A synthetic human is built as the chassis, which has eyes and no heart.
        var picked = new Dictionary<WGAugmentSlot, WGAugmentChoice>
        {
            [WGAugmentSlot.Heart] = new("WGAugmentSandevistan", null),
            [WGAugmentSlot.Eyes] = new("WGAugmentCyberEyes", null),
        };
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithBiotype(WGBiotype.Synthetic)
            .WithAugments(picked)
            .WithAugmentUnlocks(new ProtoId<WGAugmentPrototype>[] { "WGAugmentSandevistan", "WGAugmentCyberEyes" });
        await db.InitPrefsAsync(userId, profile, default);

        EntityUid mob = default;
        HashSet<ProtoId<WGAugmentPrototype>> unfitted = new();
        await server.WaitPost(() =>
        {
            mob = spawning.SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);
            unfitted = augments.Unfitted(mob, profile);
        });
        Assert.That(unfitted.Select(u => u.Id), Is.EquivalentTo(new[] { "WGAugmentSandevistan" }));

        var played = new WGAugmentSystem.PlayedCharacter(slot, EntityUid.Invalid, mob, new(picked),
            new HashSet<ProtoId<WGAugmentPrototype>>()) { Unfitted = unfitted };
        Task? save = null;
        await server.WaitPost(() => save = augments.SaveBody(userId, played, mob));
        if (save != null)
        {
            await PoolManager.WaitUntil(server, () => save.IsCompleted, maxTicks: 300);
            Assert.That(save.IsCompletedSuccessfully, $"The save failed: {save.Exception}");
        }

        var stored = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(userId, default))!.Characters[slot];
        Assert.That(stored.Augments.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.Heart, WGAugmentSlot.Eyes }),
            "A heart that never went in was taken off the profile.");
        Assert.That(stored.AugmentUnlocks.Select(u => u.Id),
            Is.EquivalentTo(new[] { "WGAugmentSandevistan", "WGAugmentCyberEyes" }));

        // Dead at the end, the character loses the eyes it had and keeps the heart it never did.
        Task? forfeit = null;
        await server.WaitPost(() => forfeit = augments.Forfeit(userId, played, "wg-augments-forfeit-dead"));
        Assert.That(forfeit, Is.Not.Null);
        await PoolManager.WaitUntil(server, () => forfeit!.IsCompleted, maxTicks: 300);

        stored = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(userId, default))!.Characters[slot];
        Assert.That(stored.Augments.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.Heart }));
        Assert.That(stored.AugmentUnlocks.Select(u => u.Id), Is.EquivalentTo(new[] { "WGAugmentSandevistan" }));

        await server.WaitPost(() => entities.DeleteEntity(mob));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Paid-for eyes cut out of one character and kept by another are the thief's to sell, once: the victim loses
    /// them, and the eyes still in the thief's head after the sale are worth nothing. A victim can't sell eyes that
    /// are no longer in their head. Cut out, the eyes are priced at what they sell back for, and selling them as
    /// loot costs the victim the augment.
    /// </summary>
    [Test]
    public async Task TheftTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var augments = server.System<WGAugmentSystem>();
        var body = server.System<SharedBodySystem>();
        var pricing = server.System<PricingSystem>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var states = client.ResolveDependency<IStateManager>();
        var map = await pair.CreateTestMap();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState, 600);
        var userId = clientNet.ServerChannel!.UserId;
        const int victim = 0;
        const int thief = 1;
        const int balance = 1000;
        ProtoId<WGAugmentPrototype> eyes = "WGAugmentCyberEyes";
        var refund = WGAugmentRules.Refund(server.ProtoMan.Index(eyes));
        Assert.That(refund, Is.GreaterThan(0));

        HumanoidCharacterProfile Stored(int slot) => (HumanoidCharacterProfile) prefs.GetPreferences(userId).Characters[slot];

        async Task Run(Func<Task?> save)
        {
            Task? task = null;
            await server.WaitPost(() => task = save());
            if (task == null)
                return;
            await PoolManager.WaitUntil(server, () => task.IsCompleted, maxTicks: 300);
            Assert.That(task.IsCompletedSuccessfully, $"The save failed: {task.Exception}");
        }

        var none = new Dictionary<WGAugmentSlot, WGAugmentChoice>();
        var picked = new Dictionary<WGAugmentSlot, WGAugmentChoice> { [WGAugmentSlot.Eyes] = new(eyes, null) };
        var bought = HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithBankBalance(balance)
            .WithAugments(picked)
            .WithAugmentUnlocks(new[] { eyes });
        var thiefPlayed = new WGAugmentSystem.PlayedCharacter(thief, EntityUid.Invalid, EntityUid.Invalid, none, new HashSet<ProtoId<WGAugmentPrototype>>());

        // A body with the bought eyes, marked as the victim's when it spawns.
        EntityUid Victim()
        {
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            Apply(entities, mob, picked);
            augments.MarkPaid(mob, Stored(victim), new WGAugmentOwner(userId, victim));
            return mob;
        }

        EntityUid CutOut(EntityUid from)
        {
            var organ = body.GetBodyOrgans(from).First(o => o.Component.SlotId == "eyes").Id;
            body.RemoveOrgan(organ);
            return organ;
        }

        void PutIn(EntityUid organ, EntityUid into)
        {
            var own = body.GetBodyOrgans(into).First(o => o.Component.SlotId == "eyes").Id;
            var head = entities.GetComponent<TransformComponent>(own).ParentUid;
            body.RemoveOrgan(own);
            entities.DeleteEntity(own);
            Assert.That(body.InsertOrgan(head, organ, "eyes"), "The eyes didn't go into the thief's head.");
        }

        await Run(() => prefs.SetProfile(userId, thief, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithBankBalance(balance)));
        await Run(() => prefs.SetProfile(userId, victim, bought));

        // The thief ends the round with the victim's eyes: they are the thief's, paid for, and no longer the victim's.
        EntityUid robbed = default;
        EntityUid robber = default;
        await server.WaitPost(() =>
        {
            robbed = Victim();
            robber = entities.SpawnEntity("MobHuman", map.GridCoords);
            PutIn(CutOut(robbed), robber);
        });
        await Run(() => augments.SaveBody(userId, thiefPlayed, robber));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored(thief).AugmentUnlocks, Is.EquivalentTo(new[] { eyes }));
            Assert.That(Stored(thief).AugmentFound, Is.Empty, "Stolen paid-for eyes read as never bought.");
            Assert.That(Stored(victim).AugmentUnlocks, Is.Empty, "The victim still owns the eyes the thief kept.");
            Assert.That(Stored(victim).Augments, Is.Empty);
        });

        // Selling them pays the thief once; the eyes still in the head are scrap after that.
        await Run(() => prefs.SetProfile(userId, thief, Stored(thief).WithAugments(none), authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored(thief).BankBalance, Is.EqualTo(balance + refund));
            Assert.That(Stored(thief).AugmentUnlocks, Is.Empty);
        });
        await Run(() => augments.SaveBody(userId, thiefPlayed, robber));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored(thief).AugmentUnlocks, Is.Empty, "Eyes already sold can be sold again.");
            Assert.That(Stored(thief).Augments, Is.Empty, "Eyes already sold are kept though nobody paid for them.");
            entities.DeleteEntity(robbed);
            entities.DeleteEntity(robber);
        });

        // A victim whose eyes were cut out can't sell them from the creator; whoever keeps them still gets them paid for.
        await Run(() => prefs.SetProfile(userId, thief, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithBankBalance(balance)));
        await Run(() => prefs.SetProfile(userId, victim, bought));
        EntityUid loose = default;
        await server.WaitPost(() =>
        {
            robbed = Victim();
            loose = CutOut(robbed);
        });
        await Run(() => prefs.SetProfile(userId, victim, Stored(victim).WithAugments(none), authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored(victim).BankBalance, Is.EqualTo(balance), "The victim was paid for eyes someone else has.");
            Assert.That(Stored(victim).AugmentUnlocks, Is.Empty);
            robber = entities.SpawnEntity("MobHuman", map.GridCoords);
            PutIn(loose, robber);
        });
        await Run(() => augments.SaveBody(userId, thiefPlayed, robber));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored(thief).AugmentUnlocks, Is.EquivalentTo(new[] { eyes }));
            Assert.That(Stored(thief).AugmentFound, Is.Empty);
            entities.DeleteEntity(robbed);
            entities.DeleteEntity(robber);
        });

        // In the victim's head the eyes are worth what any eyes are; cut out they are worth their sell-back price
        // on top, and once sold the victim no longer owns them. The test runs in the lobby, so the sale is called.
        await Run(() => prefs.SetProfile(userId, victim, bought));
        await Run(() =>
        {
            robbed = Victim();
            var inHead = pricing.GetPrice(body.GetBodyOrgans(robbed).First(o => o.Component.SlotId == "eyes").Id);
            loose = CutOut(robbed);
            Assert.That(pricing.GetPrice(loose) - inHead, Is.EqualTo(refund), "Bought eyes cut out of their owner aren't worth their sell-back price.");
            var lost = augments.LoseDestroyed((loose, entities.GetComponent<WGAugmentPaidComponent>(loose)));
            entities.DeleteEntity(loose);
            return lost;
        });
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored(victim).AugmentUnlocks, Is.Empty, "The victim still owns eyes that were sold as loot.");
            Assert.That(Stored(victim).Augments, Is.Empty);
            entities.DeleteEntity(robbed);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A wreck find is marked hardware nobody owns: it is priced at what its augment sells back for, and a character
    /// who ends a round with it installed owns it as paid for. A marked autosurgeon passes its mark to the part.
    /// </summary>
    [Test]
    public async Task ScavengeTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var augments = server.System<WGAugmentSystem>();
        var body = server.System<SharedBodySystem>();
        var pricing = server.System<PricingSystem>();
        var surgeons = server.System<AutoSurgeonSystem>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var states = client.ResolveDependency<IStateManager>();
        var map = await pair.CreateTestMap();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState, 600);
        var userId = clientNet.ServerChannel!.UserId;
        const int slot = 0;
        ProtoId<WGAugmentPrototype> eyes = "WGAugmentCyberEyes";
        ProtoId<WGAugmentPrototype> armor = "WGAugmentDermalArmor";

        // The spawner leaves one marked find that nobody owns, worth at least what its augment sells back for.
        await server.WaitPost(() => entities.SpawnEntity("WGAugmentScavengeSpawner", map.GridCoords));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            var finds = entities.AllEntities<WGAugmentPaidComponent>().ToList();
            Assert.That(finds, Has.Count.EqualTo(1), "The scavenge spawner didn't leave one marked find.");
            var (id, mark) = finds[0].Comp.Augments.Single();
            Assert.That(mark.Owner, Is.Null);
            Assert.That(pricing.GetPrice(finds[0]), Is.GreaterThanOrEqualTo(WGAugmentRules.Refund(server.ProtoMan.Index(id))));
            entities.DeleteEntity(finds[0]);
        });

        Task? save = null;
        await server.WaitPost(() => save = prefs.SetProfile(userId, slot, HumanoidCharacterProfile.DefaultWithSpecies("Human")));
        await PoolManager.WaitUntil(server, () => save!.IsCompleted, maxTicks: 300);

        // Found eyes and a found dermal armor autosurgeon, installed and kept to the end of the round.
        EntityUid mob = default;
        await server.WaitAssertion(() =>
        {
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            var found = entities.SpawnEntity("BasicCyberneticEyes", map.GridCoords);
            Assert.That(augments.MarkLoot(found), "Cyber eyes aren't a priced augment's hardware.");
            var own = body.GetBodyOrgans(mob).First(o => o.Component.SlotId == "eyes").Id;
            var head = entities.GetComponent<TransformComponent>(own).ParentUid;
            body.RemoveOrgan(own);
            entities.DeleteEntity(own);
            Assert.That(body.InsertOrgan(head, found, "eyes"));

            var surgeon = entities.SpawnEntity("AutosurgeonDermalArmorPDV", map.GridCoords);
            Assert.That(augments.MarkLoot(surgeon));
            Assert.That(pricing.GetPrice(surgeon), Is.GreaterThanOrEqualTo(WGAugmentRules.Refund(server.ProtoMan.Index(armor))));
            Assert.That(surgeons.WgApplyUpgrade(mob, entities.GetComponent<AutoSurgeonComponent>(surgeon)));
            augments.PassMark((surgeon, entities.GetComponent<WGAugmentPaidComponent>(surgeon)), mob);
        });
        await pair.RunTicksSync(2);

        await server.WaitPost(() => save = augments.SaveBody(userId,
            new WGAugmentSystem.PlayedCharacter(slot, EntityUid.Invalid, mob, new(), new()), mob));
        Assert.That(save, Is.Not.Null, "Nothing was kept from the body.");
        await PoolManager.WaitUntil(server, () => save!.IsCompleted, maxTicks: 300);
        await server.WaitAssertion(() =>
        {
            var stored = (HumanoidCharacterProfile) prefs.GetPreferences(userId).Characters[slot];
            Assert.That(stored.AugmentUnlocks, Is.EquivalentTo(new[] { eyes, armor }));
            Assert.That(stored.AugmentFound, Is.Empty, "Scavenged cyberware kept through the round sells for nothing.");
            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RegistryTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var installed = new Dictionary<string, string>();
            foreach (var augment in proto.EnumeratePrototypes<WGAugmentPrototype>().Where(a => !a.ID.StartsWith("WGTest")))
            {
                if (WGAugmentRules.Installed(augment) is not { } id)
                    continue;

                Assert.That(proto.TryIndex(id, out var entity), $"{augment.ID} names a missing entity.");
                Assert.That(installed.TryAdd(id, augment.ID), $"{augment.ID} and {installed.GetValueOrDefault(id)} both install {id}.");
                if (augment.Kind == WGAugmentKind.Upgrade)
                {
                    Assert.That(entity!.TryComp(out AutoSurgeonComponent? surgeon, factory) && surgeon.NewPartProto == null,
                        $"{augment.ID} names {id}, which is not an upgrading autosurgeon.");

                    // An implant's slot is the part its autosurgeon works on.
                    var target = surgeon!.TargetBodyPart switch
                    {
                        BodyPartType.Torso => WGAugmentSlot.Chest,
                        BodyPartType.Head => WGAugmentSlot.Head,
                        _ => Enum.Parse<WGAugmentSlot>($"{surgeon.TargetBodyPartSymmetry}{surgeon.TargetBodyPart}"),
                    };
                    Assert.That(augment.Slot, Is.EqualTo(target), $"{augment.ID} goes in the wrong part for {id}.");
                }
            }

            var table = proto.Index<EntityTablePrototype>("WGAugmentScavengeTable").Table;
            var odds = table.ListSpawns(server.EntMan, proto, new EntityTableContext()).ToDictionary(s => s.spawn.Id, s => s.prob);
            Assert.That(odds.Values.Sum(), Is.EqualTo(1).Within(1e-6));
            Assert.That(odds, Does.Not.ContainKey("WGAugmentLeftArm"), "A free augment is scavenged.");
            Assert.That(odds["OrganHeartSandevistan"], Is.LessThan(odds["BasicCyberneticEyes"]));
            Assert.That(odds, Contains.Key("AutosurgeonDermalArmorPDV"));
        });

        await pair.CleanReturnAsync();
    }
}
