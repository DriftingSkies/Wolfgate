#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Movement;
using Content.Server._WF.Chimera;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Chimera;

[NonParallelizable]
public sealed class FleshPustuleMovementTest : MovementTest
{
    [Test]
    public async Task WalkingIntoPustuleBurstsIt()
    {
        TargetCoords = SEntMan.GetNetCoordinates(ToServer(PlayerCoords).Offset(new Vector2(2, 0)));
        await SpawnTarget("WFFleshPustule");
        Assert.That(Comp<WFFleshPustuleComponent>().Bursted, Is.False);
        // Half a second covered the 0.85 m to the sensor only just, with the walk still speeding up; the wall past it stops the player.
        await Move(DirectionFlag.East, 1f);
        Assert.That(Comp<WFFleshPustuleComponent>().Bursted, Is.True, "Actual movement into the sensor failed to burst the pustule.");
    }
}
