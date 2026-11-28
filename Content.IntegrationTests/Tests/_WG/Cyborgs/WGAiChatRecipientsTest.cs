#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._CorvaxNext.Silicons.Borgs;
using Content.Server._WG.Cyborgs;
using Content.Server.Chat.Systems;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Silicons.StationAi;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>Local chat keeps working, and the AI gets a working eye back, when its drone or hologram is deleted.</summary>
[TestFixture]
[TestOf(typeof(StationAiSystem))]
public sealed class WGAiChatRecipientsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGAiChatTestDrone
  components:
  - type: MindContainer
  - type: MobState
  - type: Actions
  - type: AiRemoteController
";

    /// <summary>The drone is deleted while the AI controls it.</summary>
    [Test]
    public async Task DeletedDroneTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var (core, brain) = await SpawnCore(pair, map.GridCoords);
        EntityUid drone = default;
        await server.WaitPost(() =>
        {
            drone = entMan.SpawnEntity("WGAiChatTestDrone", map.GridCoords);
            entMan.System<AiRemoteControlSystem>().AiTakeControl(brain, drone);
            Assert.That(pair.Player!.AttachedEntity, Is.EqualTo(drone), "The AI never reached the drone.");
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => entMan.DeleteEntity(drone));
        await pair.RunTicksSync(5);

        await AssertChatWorks(pair, map.GridCoords);
        await AssertBackInCore(pair, core, brain);
        await pair.CleanReturnAsync();
    }

    /// <summary>The drone's shuttle, holding the drone and the AI's hologram, is deleted while the AI controls it.</summary>
    [Test]
    public async Task DeletedDroneGridTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var (core, brain) = await SpawnCore(pair, map.GridCoords);
        EntityUid shuttle = default;
        await server.WaitPost(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            var xforms = entMan.System<SharedTransformSystem>();
            var grid = maps.CreateGridEntity(map.MapId);
            shuttle = grid.Owner;
            xforms.SetWorldPosition(shuttle, new Vector2(20f, 20f));
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);

            var onShuttle = new EntityCoordinates(shuttle, new Vector2(0.5f, 0.5f));
            var drone = entMan.SpawnEntity("WGAiChatTestDrone", onShuttle);
            entMan.System<WGAiRemoteLinkSystem>().Link(brain, drone);
            // The AI looks at the drone before taking it, so its hologram spawns on the shuttle.
            xforms.SetCoordinates(entMan.GetComponent<StationAiCoreComponent>(core).RemoteEntity!.Value, onShuttle);
            entMan.System<AiRemoteControlSystem>().AiTakeControl(brain, drone);
            Assert.That(pair.Player!.AttachedEntity, Is.EqualTo(drone), "The AI never reached the drone.");
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => entMan.DeleteEntity(shuttle));
        await pair.RunTicksSync(5);

        await AssertChatWorks(pair, map.GridCoords);
        await AssertBackInCore(pair, core, brain);
        await pair.CleanReturnAsync();
    }

    /// <summary>The AI's local hologram is deleted while the AI sits in its core, as on a holopad call; ending the call recovers the eye.</summary>
    [Test]
    public async Task DeletedHologramTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var (core, brain) = await SpawnCore(pair, map.GridCoords);
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<StationAiCoreComponent>(core);
            entMan.System<SharedStationAiSystem>().SwitchRemoteEntityMode((core, comp), false);
            Assert.That(comp.Remote, Is.False);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => entMan.DeleteEntity(entMan.GetComponent<StationAiCoreComponent>(core).RemoteEntity!.Value));
        await pair.RunTicksSync(5);

        await AssertChatWorks(pair, map.GridCoords);

        await server.WaitPost(() => entMan.System<SharedStationAiSystem>()
            .SwitchRemoteEntityMode((core, entMan.GetComponent<StationAiCoreComponent>(core)), true));
        await pair.RunTicksSync(5);
        await AssertBackInCore(pair, core, brain);
        await pair.CleanReturnAsync();
    }

    private static async Task<(EntityUid Core, EntityUid Brain)> SpawnCore(Pair.TestPair pair, EntityCoordinates coords)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        EntityUid core = default;
        EntityUid brain = default;
        await server.WaitPost(() =>
        {
            core = entMan.SpawnEntity("PlayerStationAiTSFMC", coords);
            Assert.That(entMan.System<SharedStationAiSystem>().TryGetHeld(new Entity<StationAiCoreComponent?>(core, null), out brain));
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var take = new TakeGhostRoleEvent(pair.Player!);
            entMan.EventBus.RaiseLocalEvent(brain, ref take);
            Assert.That(take.TookRole, "The AI ghost role couldn't be taken.");
        });
        await pair.RunTicksSync(5);
        return (core, brain);
    }

    /// <summary>Someone speaks, which asks every AI core whether its player can hear.</summary>
    private static async Task AssertChatWorks(Pair.TestPair pair, EntityCoordinates coords)
    {
        var entMan = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var speaker = entMan.SpawnEntity(null, coords);
            var recipients = new Dictionary<ICommonSession, ChatSystem.ICChatRecipientData>();
            Assert.DoesNotThrow(() => entMan.EventBus.RaiseEvent(EventSource.Local, new ExpandICChatRecipientsEvent(speaker, 10f, recipients)),
                "An AI core with a deleted remote entity broke local chat.");
        });
    }

    /// <summary>The player is back in the core, which roams with a live eye.</summary>
    private static async Task AssertBackInCore(Pair.TestPair pair, EntityUid core, EntityUid brain)
    {
        var entMan = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StationAiCoreComponent>(core);
            Assert.That(pair.Player!.AttachedEntity, Is.EqualTo(brain), "The AI didn't return to its core.");
            Assert.That(comp.Remote, Is.True, "The core is still projecting a hologram.");
            Assert.That(comp.RemoteEntity is { } eye && !entMan.Deleted(eye), "The core has no live eye.");
        });
    }
}
