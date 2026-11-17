using System.Linq;
using Content.Server._Crescent.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks live requests move real protection gradually without rebuilding unchanged coverage.</summary>
[TestFixture]
public sealed class WGShipShieldLiveShuntTest
{
    [Test]
    public async Task RequestedAllocationMovesCoverageAndPreservesFullPerimeterFixtures()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var system = entities.System<ShipShieldsSystem>();
            server.ResolveDependency<IConsoleHost>().ExecuteCommand($"shieldentity {map.Grid.Owner}");
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            system.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 0f, MathF.PI / 2f);
            var fixture = entities.GetComponent<FixturesComponent>(shield).Fixtures.First();
            var fullFixtureCount = entities.GetComponent<FixturesComponent>(shield).Fixtures.Count;
            // Start the manual timing check on a fresh redistribution tick.
            system.UpdateWolfgateShieldShunts(0.2f);
            Assert.That(system.RequestWolfgateShieldShunt(map.Grid.Owner, MathF.PI / 2f, 0.8f, MathF.PI), Is.True);
            var current = entities.GetComponent<WGShipShieldShuntComponent>(map.Grid.Owner);
            Assert.That(current.DirectionRadians, Is.Zero, "A live request must not teleport protection.");
            Assert.That(current.Concentration, Is.Zero);
            system.UpdateWolfgateShieldShunts(0.1f);
            Assert.That(current.Concentration, Is.InRange(0.001f, WGShipShieldShuntMath.TransferSpeed * 0.1f + 0.001f));
            Assert.That(current.DirectionRadians, Is.InRange(0.001f, WGShipShieldShuntMath.TurnSpeed * 0.1f + 0.0001f));
            var field = entities.GetComponent<WGShipShieldShuntComponent>(shield);
            Assert.That(field.DirectionRadians, Is.EqualTo(current.DirectionRadians));
            Assert.That(field.Concentration, Is.EqualTo(current.Concentration));
            Assert.That(field.ArcRadians, Is.EqualTo(current.ArcRadians));
            for (var step = 0; step < 100; step++)
                system.UpdateWolfgateShieldShunts(0.1f);
            Assert.That(current.Concentration, Is.EqualTo(0.8f));
            Assert.That(current.DirectionRadians, Is.EqualTo(MathF.PI / 2f).Within(0.0001f));
            Assert.That(current.ArcRadians, Is.EqualTo(MathF.PI).Within(0.0001f));
            Assert.That(entities.GetComponent<FixturesComponent>(shield).Fixtures[fixture.Key], Is.SameAs(fixture.Value),
                "Partial allocation still protects the full perimeter and must reuse its fixtures.");
            Assert.That(system.RequestWolfgateShieldShunt(map.Grid.Owner, float.NaN, 1f, MathF.PI), Is.False);
            Assert.That(system.RequestWolfgateShieldShunt(map.Grid.Owner, MathF.PI / 2f, 1f, MathF.PI), Is.True);
            system.UpdateWolfgateShieldShunts(0.1f);
            Assert.That(current.Concentration, Is.LessThan(1f));
            for (var step = 0; step < 100; step++)
                system.UpdateWolfgateShieldShunts(0.1f);
            Assert.That(current.Concentration, Is.EqualTo(1f));
            Assert.That(entities.GetComponent<FixturesComponent>(shield).Fixtures.Count, Is.LessThan(fullFixtureCount),
                "Settled full diversion must physically open the unpowered half.");
            Assert.That(entities.GetComponent<PhysicsComponent>(shield).CanCollide, Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
