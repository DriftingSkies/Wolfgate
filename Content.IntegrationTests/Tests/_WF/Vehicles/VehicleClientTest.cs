#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Pair;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using SharedMover = Content.Shared._WF.Vehicles.Grid.GridVehicleMoverSystem;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>What the vehicle module needs from the client: input contexts for the keys its systems bind.</summary>
[TestFixture]
public sealed class VehicleClientTest
{
    /// <summary>
    /// A vehicle nobody sits inside is driven by a session attached to the vehicle itself (the Bison). The client picks the
    /// input context from the attached entity, and with none it falls back to "common", which has no use-item key: the
    /// horn, bound to that key, could not be sounded.
    /// </summary>
    [Test]
    public async Task AVehicleDrivenFromItselfHasAContextWithTheHornKey()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var checkedVehicles = new List<string>();

        await client.WaitAssertion(() =>
        {
            var factory = client.EntMan.ComponentFactory;
            var contexts = client.ResolveDependency<IInputManager>().Contexts;

            foreach (var proto in client.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                // A vehicle with a doorway has its driver in a body of their own, which has a context.
                if (proto.Abstract ||
                    VehicleSpawnDefaultsTest.IsTestVehicle(proto.ID) ||
                    !proto.TryGetComponent<RMCVehicleComponent>(out _, factory) ||
                    proto.TryGetComponent<VehicleEnterComponent>(out _, factory) ||
                    !proto.TryGetComponent<VehicleSoundComponent>(out var sound, factory) ||
                    sound.HornSound == null)
                {
                    continue;
                }

                checkedVehicles.Add(proto.ID);

                if (!proto.TryGetComponent<InputComponent>(out var input, factory))
                {
                    Assert.Fail($"{proto.ID} has a horn but no input context, so its pilot's use-item key never reaches it.");
                    continue;
                }

                Assert.That(
                    contexts.GetContext(input.ContextName).FunctionExistsHierarchy(ContentKeyFunctions.UseItemInHand),
                    Is.True,
                    $"{proto.ID} names the input context \"{input.ContextName}\", which has no use-item key to sound the horn with.");
            }
        });

        Assert.That(checkedVehicles, Does.Contain("CMUCargoCarrier"));
        await pair.CleanReturnAsync();
    }
}

/// <summary>
/// The debug overlay commands set static flags on the shared mover, which every pair in the process shares, so this runs
/// with nothing else.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class VehicleDebugFlagsTest
{
    /// <summary>
    /// A disconnect shuts the client's mover system down and builds a new one with every overlay off. The shared mover's
    /// flags must go off with the old one, or it would take its debug path for ever with nothing to show for it.
    /// </summary>
    [Test]
    public async Task ShuttingTheClientMoverDownSwitchesTheSharedDebugFlagsOff()
    {
        // The client has no systems until it has joined a game.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var mover = client.System<Content.Client._WF.Vehicles.Grid.GridVehicleMoverSystem>();

            try
            {
                Assert.That(mover.ToggleCollisionOverlay(), Is.True);
                Assert.That(mover.ToggleMovementOverlay(), Is.True);
                SharedMover.DebugTestedTiles.Add((EntityUid.Invalid, default));
                Assert.Multiple(() =>
                {
                    Assert.That(SharedMover.CollisionDebugEnabled, Is.True, "Test setup: the collision flag should be on.");
                    Assert.That(SharedMover.MovementDebugEnabled, Is.True, "Test setup: the movement flag should be on.");
                });

                mover.Shutdown();

                Assert.Multiple(() =>
                {
                    Assert.That(SharedMover.CollisionDebugEnabled, Is.False, "The collision flag outlived the system.");
                    Assert.That(SharedMover.MovementDebugEnabled, Is.False, "The movement flag outlived the system.");
                    Assert.That(SharedMover.DebugTestedTiles, Is.Empty, "Debug data outlived the system.");
                });

                // The system is the same object for the rest of this pair's life; it starts again with its overlays off.
                Assert.That(mover.ToggleCollisionOverlay(), Is.True, "The overlay did not start from off after a shutdown.");
            }
            finally
            {
                mover.Shutdown();
            }
        });

        await pair.CleanReturnAsync();
    }
}
