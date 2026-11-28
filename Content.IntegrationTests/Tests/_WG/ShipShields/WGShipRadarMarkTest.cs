#nullable enable
using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>What radars draw of a ship is kept on its grid: the overload mark and the raised field's outline.</summary>
[TestFixture]
public sealed class WGShipRadarMarkTest
{
    [Test]
    public async Task AnOverloadIsMarkedForRadars()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            var shields = entities.System<ShipShieldsSystem>();
            shields.Update(1.5f);
            Assert.That(entities.HasComponent<WGShipOverloadedComponent>(grid), Is.False, "A healthy ship isn't marked.");

            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            emitter.OverloadAccumulator = 10f;
            shields.Update(0.3f);
            Assert.That(shields.IsWolfgateShipOverloaded(grid), Is.True);
            Assert.That(entities.HasComponent<WGShipOverloadedComponent>(grid), Is.True, "An overloaded ship is marked.");

            emitter.OverloadAccumulator = 0f;
            shields.Update(0.3f);
            Assert.That(entities.HasComponent<WGShipOverloadedComponent>(grid), Is.False, "The mark goes once the overload ends.");
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARaisedFieldIsCopiedToItsGrid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var grid = map.Grid.Owner;
        await server.WaitAssertion(() =>
        {
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            var shields = entities.System<ShipShieldsSystem>();
            shields.Update(1.5f);
            shields.Update(0.3f);

            var field = entities.GetComponent<ShipShieldedComponent>(grid).Shield;
            var visuals = entities.GetComponent<WGShipShieldVisualsComponent>(field);
            Assert.That(entities.TryGetComponent<WGShipShieldRadarComponent>(grid, out var radar), Is.True, "A raised field is copied to its grid.");
            Assert.That(radar!.Contours, Is.Not.Empty);
            Assert.That(radar.Contours, Is.SameAs(visuals.Contours), "The grid carries the field's outline.");
            Assert.That(radar.Color, Is.EqualTo(entities.GetComponent<ShipShieldVisualsComponent>(field).ShieldColor));
            Assert.That(radar.Health, Is.EqualTo(MathF.Round(visuals.Health * 20f) / 20f).Within(0.0001f));

            shields.SetWolfgateShieldEnabled(grid, false);
            shields.Update(0.3f);
        });
        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<WGShipShieldRadarComponent>(grid), Is.False, "A lowered field leaves the grid."));
        await pair.CleanReturnAsync();
    }
}
