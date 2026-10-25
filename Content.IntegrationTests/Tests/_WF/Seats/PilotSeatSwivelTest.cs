using System.Numerics;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Seats;

/// <summary>A seated pilot turns the pilot seat by facing a point, and stays buckled.</summary>
[TestFixture]
public sealed class PilotSeatSwivelTest
{
    [Test]
    public async Task PilotSeatTurnsToFace()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        EntityUid pilot = default, seat = default;

        await server.WaitAssertion(() =>
        {
            var xforms = em.System<SharedTransformSystem>();
            seat = em.SpawnEntity("ChairPilotSeat", map.GridCoords);
            pilot = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(em.GetComponent<TransformComponent>(seat).Anchored, Is.True);
            Assert.That(em.System<SharedBuckleSystem>().TryBuckle(pilot, pilot, seat), Is.True);

            var target = xforms.GetWorldPosition(seat) + new Vector2(0, 3);
            Assert.That(em.System<RotateToFaceSystem>().TryFaceCoordinates(pilot, target), Is.True);
            Assert.That(xforms.GetWorldRotation(seat).EqualsApprox(Angle.FromWorldVec(new Vector2(0, 1)), 0.01), Is.True,
                "The seat turns toward the point.");
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
            Assert.That(em.GetComponent<BuckleComponent>(pilot).BuckledTo, Is.EqualTo(seat), "Turning the seat keeps the pilot in it."));

        await pair.CleanReturnAsync();
    }
}
