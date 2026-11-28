#nullable enable
using System.Numerics;
using System.Reflection;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server._WG.ShipFacing;
using Content.Shared.Shuttles.Components;
using Content.Shared._WG.ShipFacing;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.ShipFacing;

/// <summary>A pilot holding the facing key turns the ship's nose onto the cursor and stops there.</summary>
[TestFixture]
public sealed class WGShipFacingTest
{
    [Test]
    public async Task TheNoseSettlesOnTheCursor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var ticks = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var xforms = server.System<SharedTransformSystem>();
        var facing = server.System<WGShipFacingSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        EntityUid helm = default, pilot = default;
        var target = MapCoordinates.Nullspace;

        await server.WaitPost(() =>
        {
            helm = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(helm).NeedsPower = false;

            // About 2 rad/s² of turn, as if from gyroscopes.
            var body = em.GetComponent<PhysicsComponent>(grid);
            em.EnsureComponent<ShuttleComponent>(grid).AngularThrust = 2f / body.InvI;

            pilot = em.SpawnEntity("MobHuman", map.GridCoords);
            em.EnsureComponent<PilotComponent>(pilot);
            server.System<ShuttleConsoleSystem>().AddPilot(helm, pilot, em.GetComponent<ShuttleConsoleComponent>(helm));

            // Ten metres east of the hull's centre: a quarter turn clockwise.
            var center = Vector2.Transform(body.LocalCenter, xforms.GetWorldMatrix(grid));
            target = new MapCoordinates(center + new Vector2(10f, 0f), map.MapId);
        });

        // Held for four seconds, resent as the client does.
        for (var i = 0; i < 16; i++)
        {
            await server.WaitPost(() => facing.SetTarget(pilot, helm, target));
            await pair.RunTicksSync(ticks / 4);
        }

        await server.WaitAssertion(() =>
        {
            var heading = xforms.GetWorldRotation(grid);
            var error = Angle.ShortestDistance(heading, new Angle(-MathF.PI / 2f)).Degrees;
            var spin = em.GetComponent<PhysicsComponent>(grid).AngularVelocity;
            Assert.That(em.GetComponent<PilotComponent>(pilot).Console, Is.EqualTo(helm), "The pilot stays at the helm.");
            Assert.That(Math.Abs(error), Is.LessThan(2.0), $"The nose points at the cursor: heading {heading.Degrees:F1}°.");
            Assert.That(MathF.Abs(spin), Is.LessThan(0.05f), $"And the turn has stopped: {spin:F3} rad/s.");
        });

        // Let go: the point lapses and the hull is left alone.
        await pair.RunTicksSync(ticks);
        await server.WaitAssertion(() =>
            Assert.That(facing.TryGetTarget((pilot, em.GetComponent<PilotComponent>(pilot)), out _), Is.False,
                "A point nobody resends lapses."));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AShiftComboKeepsSteering()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var input = client.ResolveDependency<IInputManager>();
        var inputs = client.System<InputSystem>();
        var facing = client.System<Content.Client._WG.ShipFacing.WGShipFacingSystem>();
        var function = WGShipFacingKeys.WGShuttleFaceCursor;
        await client.WaitAssertion(() =>
        {
            var pressed = (bool[]) input.GetType().GetField("_keysPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
            void Send(BoundKeyState state) => inputs.HandleInputCommand(null, function,
                new ClientFullInputCmdMessage(client.ResolveDependency<IGameTiming>().CurTick, 0, input.NetworkBindMap.KeyFunctionID(function)) { State = state });

            pressed[(int) Keyboard.Key.Shift] = true;
            Send(BoundKeyState.Down);
            Assert.That(facing.Held, Is.True, "Shift steers.");

            // A combo such as Shift+click or Shift+1 lifts the Shift-only binding while Shift is still down.
            Send(BoundKeyState.Up);
            Assert.That(facing.Held, Is.True, "A Shift combo keeps steering.");

            pressed[(int) Keyboard.Key.Shift] = false;
            facing.FrameUpdate(0.016f);
            Assert.That(facing.Held, Is.False, "Letting go of Shift stops steering.");
        });
        await pair.CleanReturnAsync();
    }
}
