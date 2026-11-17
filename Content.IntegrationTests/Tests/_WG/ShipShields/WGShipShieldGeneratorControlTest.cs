using Content.Server._Crescent.ShipShields;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks generator controls share persistent ship settings and require installation.</summary>
[TestFixture]
public sealed class WGShipShieldGeneratorControlTest
{
    [Test]
    public async Task GeneratorTogglePersistsAndLooseGeneratorCannotChangeShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var transform = entities.System<SharedTransformSystem>();
            Assert.That(entities.GetComponent<TransformComponent>(uid).Anchored, Is.True, "The generator prototype spawns installed.");
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(uid);
            var receiver = entities.GetComponent<ApcPowerReceiverComponent>(uid);
            receiver.Powered = true;
            var shields = entities.System<ShipShieldsSystem>();
            var controls = entities.System<WGShipShieldShuntSystem>();
            shields.Update(1.5f);
            var shield = entities.GetComponent<ShipShieldedComponent>(map.Grid.Owner).Shield;
            var actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            var ui = entities.System<SharedUserInterfaceSystem>();
            ui.OpenUi(uid, WGShipShieldUiKey.Key, actor);
            Assert.That(ui.TryGetUiState<WGShipShieldGeneratorUiState>(uid, WGShipShieldUiKey.Key, out var opened), Is.True);
            Assert.That(opened!.ShieldShunt.Available, Is.True);
            var stats = opened.ShieldShunt.Stats!;
            Assert.That(stats.Name, Is.EqualTo(entities.GetComponent<MetaDataComponent>(uid).EntityName));
            Assert.That(stats.Capacity, Is.EqualTo(WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp)));
            Assert.That(stats.MaximumPowerWatts, Is.EqualTo(emitter.BaseDraw + emitter.MaxDraw));
            emitter.BaseDraw += 1234f;
            controls.Update(0.21f);
            Assert.That(ui.TryGetUiState<WGShipShieldGeneratorUiState>(uid, WGShipShieldUiKey.Key, out var changedStats), Is.True);
            Assert.That(changedStats, Is.Not.SameAs(opened), "Runtime power changes must refresh an otherwise unchanged panel.");
            Assert.That(changedStats!.ShieldShunt.Stats!.BasePowerWatts, Is.EqualTo(emitter.BaseDraw));

            Send(new WGShipShieldSetShuntMessage(0.6f, 0.5f, 1.2f));
            Assert.That(controls.GetState(map.Grid.Owner).TargetConcentration, Is.EqualTo(0.5f));
            Assert.That(controls.GetState(map.Grid.Owner).Concentration, Is.Zero);
            for (var step = 0; step < 100; step++)
                controls.Update(0.1f);
            Assert.That(controls.GetState(map.Grid.Owner).Concentration, Is.EqualTo(0.5f));
            emitter.Damage = emitter.DamageLimit * 0.8995f;
            controls.Update(0.21f);
            emitter.Damage = emitter.DamageLimit * 0.9005f;
            controls.Update(0.21f);
            Assert.That(ui.TryGetUiState<WGShipShieldGeneratorUiState>(uid, WGShipShieldUiKey.Key, out var critical), Is.True);
            Assert.That(critical!.ShieldShunt.Health, Is.LessThan(0.1f), "Critical warnings refresh even if the rounded percentage has not changed.");
            emitter.Damage = emitter.DamageLimit * 0.5f;
            Send(new WGShipShieldSetEnabledMessage(false));
            Assert.That(entities.IsQueuedForDeletion(shield), Is.True, "Manual lowering removes the field immediately.");
            var lowered = controls.GetState(map.Grid.Owner);
            Assert.That(lowered.Enabled, Is.False);
            Assert.That(lowered.Active, Is.False);
            Assert.That(lowered.Health, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(lowered.DirectionRadians, Is.EqualTo(0.6f).Within(0.001f));
            shields.Update(1.5f);
            Assert.That(entities.HasComponent<ShipShieldedComponent>(map.Grid.Owner), Is.False,
                "Powered emitters cannot recreate a manually lowered field.");
            Assert.That(emitter.Damage, Is.GreaterThan(0f), "Lowering does not reset capacity.");

            transform.Unanchor(uid, entities.GetComponent<TransformComponent>(uid));
            Send(new WGShipShieldSetEnabledMessage(true));
            Assert.That(controls.GetState(map.Grid.Owner).Available, Is.False);
            Assert.That(controls.GetState(map.Grid.Owner).Enabled, Is.False, "A loose generator cannot operate the ship's controls.");
            transform.AnchorEntity(uid, entities.GetComponent<TransformComponent>(uid));
            Send(new WGShipShieldSetEnabledMessage(true));
            shields.Update(1.5f);
            var restored = controls.GetState(map.Grid.Owner);
            Assert.That(restored.Enabled, Is.True);
            Assert.That(restored.Active, Is.True);
            Assert.That(restored.Concentration, Is.EqualTo(0.5f));
            controls.Update(0.21f);
            Assert.That(ui.TryGetUiState<WGShipShieldGeneratorUiState>(uid, WGShipShieldUiKey.Key, out var refreshed), Is.True);
            Assert.That(refreshed!.ShieldShunt.Enabled, Is.True);
            Assert.That(refreshed.ShieldShunt.Active, Is.True);

            void Send(BoundUserInterfaceMessage message)
            {
                message.Actor = actor;
                message.Entity = entities.GetNetEntity(uid);
                message.UiKey = WGShipShieldUiKey.Key;
                entities.EventBus.RaiseLocalEvent(uid, (object) message, true);
            }
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

}
