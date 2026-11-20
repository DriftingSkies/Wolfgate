#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Server._NF.CryoSleep;
using Content.Server._WG.Augments;
using Content.Server.Preferences.Managers;
using Content.Shared._WG.Augments;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// A player who respawns instead of going into cryo loses the augments their character spawned with and their
/// unlocks, and keeps what they bought in the creator since; a player who hasn't spawned a character loses nothing.
/// At the end of the round only a character alive in the body it spawned in keeps them, or one that went into cryo
/// and didn't wake. A character played as a borg is left as it was.
/// </summary>
[TestFixture]
[TestOf(typeof(WGAugmentSystem))]
public sealed class AugmentRespawnTest
{
    [Test]
    public async Task RespawnTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var augments = server.System<WGAugmentSystem>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var states = client.ResolveDependency<IStateManager>();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState, 600);
        var userId = clientNet.ServerChannel!.UserId;
        var session = players.GetSessionById(userId);
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

        var augmented = HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithAugments(new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.LeftArm] = new("WGAugmentCyberLeftArm", "WGAugmentStyleCyborg"),
            })
            .WithAugmentUnlocks(new ProtoId<WGAugmentPrototype>[] { "WGAugmentCyberLeftArm" });
        await Run(() => prefs.SetProfile(userId, slot, augmented));

        // Never spawned this round: nothing is taken.
        await Run(() => augments.ForfeitAugments(session));
        await server.WaitAssertion(() => Assert.That(Stored().Augments, Is.Not.Empty));

        // Spawned with the arm, then bought eyes in the creator during the round: the arm goes with the body, and the
        // eyes, which never reached it, stay.
        ProtoId<WGAugmentPrototype> eyes = "WGAugmentCyberEyes";
        await server.WaitPost(() => augments.Track(userId,
            new WGAugmentSystem.PlayedCharacter(slot, EntityUid.Invalid, EntityUid.Invalid, new(augmented.Augments),
                new HashSet<ProtoId<WGAugmentPrototype>>())));
        var withEyes = new Dictionary<WGAugmentSlot, WGAugmentChoice>(augmented.Augments) { [WGAugmentSlot.Eyes] = new(eyes, null) };
        await Run(() => prefs.SetProfile(userId, slot,
            augmented.WithAugments(withEyes).WithAugmentUnlocks(new ProtoId<WGAugmentPrototype>[] { "WGAugmentCyberLeftArm", eyes })));
        await Run(() => augments.ForfeitAugments(session));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().Augments.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.Eyes }), "Only the arm the body carried is lost.");
            Assert.That(Stored().AugmentUnlocks, Is.EquivalentTo(new[] { eyes }), "Eyes bought in the creator after the spawn were taken.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundEndTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var augments = server.System<WGAugmentSystem>();
        var minds = server.System<SharedMindSystem>();
        var mobState = server.System<MobStateSystem>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var states = client.ResolveDependency<IStateManager>();
        var map = await pair.CreateTestMap();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState, 600);
        var userId = clientNet.ServerChannel!.UserId;
        const int slot = 0;
        ProtoId<WGAugmentPrototype> eyes = "WGAugmentCyberEyes";
        var picked = new Dictionary<WGAugmentSlot, WGAugmentChoice> { [WGAugmentSlot.Eyes] = new(eyes, null) };
        var bought = HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithAugments(picked)
            .WithAugmentUnlocks(new[] { eyes });

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

        EntityUid Body()
        {
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            var ev = new WGApplyAugmentsEvent(bought);
            entities.EventBus.RaiseLocalEvent(mob, ref ev);
            return mob;
        }

        // Spawns the character with its paid-for eyes, with the player in it.
        async Task<(EntityUid Mind, EntityUid Mob, WGAugmentSystem.PlayedCharacter Played)> Spawn()
        {
            await Run(() => prefs.SetProfile(userId, slot, bought));
            (EntityUid, EntityUid, WGAugmentSystem.PlayedCharacter) spawned = default;
            await server.WaitPost(() =>
            {
                var mob = Body();
                var mind = minds.CreateMind(userId);
                minds.TransferTo(mind.Owner, mob, mind: mind.Comp);
                augments.MarkPaid(mob, bought, new WGAugmentOwner(userId, slot));
                var played = new WGAugmentSystem.PlayedCharacter(slot, mind.Owner, mob, new(picked), new());
                augments.Track(userId, played);
                spawned = (mind.Owner, mob, played);
            });
            return spawned;
        }

        async Task Clean(params EntityUid[] uids)
        {
            await server.WaitPost(() =>
            {
                foreach (var uid in uids)
                {
                    if (entities.HasComponent<MindComponent>(uid))
                        minds.WipeMind(uid);
                    else
                        entities.DeleteEntity(uid);
                }
            });
        }

        // Alive in its own body: the eyes are kept.
        var (mind, mob, played) = await Spawn();
        await Run(() => augments.SaveAtRoundEnd(userId, played));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().AugmentUnlocks, Is.EquivalentTo(new[] { eyes }), "A living character lost its eyes.");
            Assert.That(Stored().Augments, Contains.Key(WGAugmentSlot.Eyes));
        });
        await Clean(mind, mob);

        // Alive, with the player ghosted out of it: the eyes are kept.
        (mind, mob, played) = await Spawn();
        EntityUid ghost = default;
        await server.WaitPost(() =>
        {
            ghost = entities.SpawnEntity("MobObserver", map.GridCoords);
            minds.TransferTo(mind, ghost);
        });
        await Run(() => augments.SaveAtRoundEnd(userId, played));
        await server.WaitAssertion(() =>
            Assert.That(Stored().AugmentUnlocks, Is.EquivalentTo(new[] { eyes }), "A living body lost its eyes when its player ghosted."));
        await Clean(mind, mob, ghost);

        // Dead at the end: everything is gone.
        (mind, mob, played) = await Spawn();
        await server.WaitPost(() => mobState.ChangeMobState(mob, MobState.Dead));
        await Run(() => augments.SaveAtRoundEnd(userId, played));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().AugmentUnlocks, Is.Empty, "A character dead at the end kept its eyes.");
            Assert.That(Stored().Augments, Is.Empty);
        });
        await Clean(mind, mob);

        // Alive in a clone with copies of the eyes: everything is gone too.
        (mind, mob, played) = await Spawn();
        EntityUid clone = default;
        await server.WaitPost(() =>
        {
            clone = Body();
            minds.TransferTo(mind, clone);
        });
        await Run(() => augments.SaveAtRoundEnd(userId, played));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().AugmentUnlocks, Is.Empty, "A clone kept the eyes of the body it was cloned from.");
            Assert.That(Stored().Augments, Is.Empty);
        });
        await Clean(mind, mob, clone);

        // Went into cryo alive, then back to the lobby, which deletes the stored body: the eyes are kept.
        (mind, mob, played) = await Spawn();
        await server.WaitPost(() => entities.EventBus.RaiseLocalEvent(mob, new CryosleepBeforeMindRemovedEvent(mob, userId), true));
        await Clean(mind, mob);
        await Run(() => augments.SaveAllAtRoundEnd());
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().AugmentUnlocks, Is.EquivalentTo(new[] { eyes }), "A character that left from cryo lost its eyes.");
            Assert.That(Stored().Augments, Contains.Key(WGAugmentSlot.Eyes));
        });

        // Woke up from cryo and died: it is in play again, so everything is gone.
        (mind, mob, played) = await Spawn();
        await server.WaitPost(() =>
        {
            entities.EventBus.RaiseLocalEvent(mob, new CryosleepBeforeMindRemovedEvent(mob, userId), true);
            entities.EventBus.RaiseLocalEvent(mob, new CryosleepWakeUpEvent(mob, userId), true);
            mobState.ChangeMobState(mob, MobState.Dead);
        });
        await Run(() => augments.SaveAllAtRoundEnd());
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().AugmentUnlocks, Is.Empty, "A character that woke from cryo and died kept its eyes.");
            Assert.That(Stored().Augments, Is.Empty);
        });
        await Clean(mind, mob);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BorgTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var augments = server.System<WGAugmentSystem>();
        var minds = server.System<SharedMindSystem>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var states = client.ResolveDependency<IStateManager>();
        var map = await pair.CreateTestMap();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState, 600);
        var userId = clientNet.ServerChannel!.UserId;
        var session = players.GetSessionById(userId);
        const int slot = 0;
        ProtoId<WGAugmentPrototype> eyes = "WGAugmentCyberEyes";
        var bought = HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithAugments(new Dictionary<WGAugmentSlot, WGAugmentChoice> { [WGAugmentSlot.Eyes] = new(eyes, null) })
            .WithAugmentUnlocks(new[] { eyes });

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

        // Spawns the player as the given mob through the real spawn event, then respawns them without cryo.
        async Task SpawnAndRespawn(string proto)
        {
            await Run(() => prefs.SetProfile(userId, slot, bought));
            EntityUid mob = default;
            EntityUid mind = default;
            EntityUid station = default;
            await server.WaitPost(() =>
            {
                mob = entities.SpawnEntity(proto, map.GridCoords);
                station = entities.SpawnEntity(null, map.GridCoords);
                mind = minds.CreateMind(userId).Owner;
                minds.TransferTo(mind, mob);

                // A real spawn loads the profile first; a pick that never reached the body isn't taken with it.
                var apply = new WGApplyAugmentsEvent(Stored());
                entities.EventBus.RaiseLocalEvent(mob, ref apply);
                entities.EventBus.RaiseLocalEvent(mob,
                    new PlayerSpawnCompleteEvent(mob, session, null, false, true, 1, station, Stored()),
                    true);
            });
            await Run(() => augments.ForfeitAugments(session));
            await server.WaitPost(() =>
            {
                minds.WipeMind(mind);
                entities.DeleteEntity(mob);
                entities.DeleteEntity(station);
            });
        }

        // A borg never wore the character's body, so a borg round takes nothing.
        await SpawnAndRespawn("PlayerBorgBattery");
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().AugmentUnlocks, Is.EquivalentTo(new[] { eyes }), "Playing a borg took the character's paid-for eyes.");
            Assert.That(Stored().Augments, Contains.Key(WGAugmentSlot.Eyes));
        });

        // The same spawn as the character's own body is tracked, so respawning without cryo does take them.
        await SpawnAndRespawn("MobHuman");
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().AugmentUnlocks, Is.Empty, "A humanoid spawn wasn't tracked.");
            Assert.That(Stored().Augments, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }
}
