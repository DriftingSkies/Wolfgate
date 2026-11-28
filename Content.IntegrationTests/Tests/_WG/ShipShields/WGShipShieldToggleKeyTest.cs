#nullable enable
using Content.Server._Crescent.ShipShields;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>The pilot's shield and vent hotkeys work the flown ship under the helm's checks.</summary>
[TestFixture]
public sealed class WGShipShieldToggleKeyTest
{
    /// <summary>Spawns a raised shield generator, a powered helm with a pilot, and a bystander on the grid.</summary>
    private static (EntityUid Helm, EntityUid Pilot, EntityUid Bystander) Spawn(IEntityManager entities,
        EntityCoordinates at)
    {
        var generator = entities.SpawnEntity("ShieldGeneratorSmall", at);
        entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
        entities.System<ShipShieldsSystem>().Update(1.5f);

        var helm = entities.SpawnEntity("ComputerShuttle", at);
        entities.RemoveComponent<AccessReaderComponent>(helm);
        entities.GetComponent<ApcPowerReceiverComponent>(helm).Powered = true;
        var bystander = entities.SpawnEntity("MobHuman", at);
        var pilot = entities.SpawnEntity("MobHuman", at);
        entities.EnsureComponent<PilotComponent>(pilot);
        entities.System<ShuttleConsoleSystem>().AddPilot(helm, pilot, entities.GetComponent<ShuttleConsoleComponent>(helm));
        return (helm, pilot, bystander);
    }

    [Test]
    public async Task ThePilotsKeyFlipsTheShields()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var (helm, pilot, bystander) = Spawn(entities, map.GridCoords);
            var controls = entities.System<WGShipShieldShuntSystem>();
            Assert.That(controls.GetState(grid).Enabled, Is.True, "Shields start enabled.");

            Assert.That(controls.TryToggleFromHelm(bystander), Is.False, "Someone not at the helm can't use the key.");
            Assert.That(controls.GetState(grid).Enabled, Is.True);

            Assert.That(controls.TryToggleFromHelm(pilot), Is.True);
            Assert.That(controls.GetState(grid).Enabled, Is.False, "The key lowers raised shields.");
            Assert.That(controls.TryToggleFromHelm(pilot), Is.True);
            Assert.That(controls.GetState(grid).Enabled, Is.True, "And raises them again.");

            entities.GetComponent<ApcPowerReceiverComponent>(helm).Powered = false;
            Assert.That(controls.TryToggleFromHelm(pilot), Is.False, "A dead helm can't work the shields.");
            Assert.That(controls.GetState(grid).Enabled, Is.True);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThePilotsKeyVentsTheShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var (_, pilot, bystander) = Spawn(entities, map.GridCoords);
            var controls = entities.System<WGShipShieldShuntSystem>();
            var shields = entities.System<ShipShieldsSystem>();
            shields.AddWolfgateShipHeat(grid, 1000f);

            Assert.That(controls.TryVentFromHelm(bystander), Is.False, "Someone not at the helm can't use the key.");
            Assert.That(shields.IsWolfgateShipVenting(grid), Is.False);

            Assert.That(controls.TryVentFromHelm(pilot), Is.True);
            Assert.That(shields.IsWolfgateShipVenting(grid), Is.True, "The key vents the ship.");
            Assert.That(controls.TryVentFromHelm(pilot), Is.False, "A vent already running can't be started again.");
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
