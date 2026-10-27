using Content.Server.Power.EntitySystems;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>An energy gun cauterises while it has charge and asks for a reload once it has none.</summary>
[TestFixture]
[TestOf(typeof(SurgeryToolConditionsSystem))]
public sealed class WolfmedGunCauteryTest
{
    [Test]
    public async Task ChargedGunCauterises()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var battery = entities.System<BatterySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entities.SpawnEntity("WeaponEnergyGun", map.GridCoords);

            var used = new SurgeryToolUsedEvent(user, user);
            entities.EventBus.RaiseLocalEvent(gun, ref used);
            Assert.That(used.Cancelled, Is.False, "A charged energy gun was refused as a cautery.");

            battery.SetCharge(gun, 0);
            var empty = new SurgeryToolUsedEvent(user, user);
            entities.EventBus.RaiseLocalEvent(gun, ref empty);
            Assert.That(empty.Cancelled, Is.True, "An empty energy gun was accepted as a cautery.");
        });

        await pair.CleanReturnAsync();
    }
}
