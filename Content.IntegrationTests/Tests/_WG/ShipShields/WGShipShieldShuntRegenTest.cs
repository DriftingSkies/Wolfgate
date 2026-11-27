using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks a shunted field regenerates at the same rate as an even one.</summary>
[TestFixture]
public sealed class WGShipShieldShuntRegenTest
{
    [Test]
    public async Task ShuntLeavesRegenUnchanged()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var shields = entities.System<ShipShieldsSystem>();
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            shields.Update(1.5f);
            Assert.That(emitter.Shielded, Is.EqualTo(map.Grid.Owner), "The field must be up before measuring regen.");

            float Heal()
            {
                // Heat from the ship itself; what the field absorbs would stay while it is up.
                emitter.Damage = 0f;
                shields.AddWolfgateShipHeat(map.Grid.Owner, 10000f);
                emitter.Accumulator = 0f;
                shields.Update(1.5f);
                return 10000f - shields.GetWolfgateShipHeat(map.Grid.Owner).Heat;
            }

            var even = Heal();
            Assert.That(even, Is.EqualTo((emitter.HealPerSecond + shields.WolfgateHullCooling(generator)) * 1.5f).Within(0.01f));
            Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 1f, MathF.PI / 6f), Is.True);
            Assert.That(Heal(), Is.EqualTo(even).Within(0.01f), "A narrow full shunt must not change regeneration.");
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
