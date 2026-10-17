using System.Numerics;
using System.Reflection;
using Content.Client._WF.ShipShields;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Content.Server._Crescent.ShipShields;
using Content.Server._Crescent.ShipShields.Components;
using Content.Server._WF.ShipShields;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks FTL spooling immediately suppresses shields until cooldown ends.</summary>
[TestFixture]
public sealed class WFShipShieldFtlTest
{
    [Test]
    public async Task ActualFtlSpoolingDropsFieldBeforeReturningAndAllJumpStatesStayLocked()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var panel = new WFShipShieldShuntScreen();
            panel.UpdateState(new WFShipShieldShuntState(true, false, 1f, 0f, 0f, MathF.PI / 2f)
            {
                RecoveryStatus = WFShipShieldRecoveryStatus.FtlLocked,
                RecoverySeconds = -1,
            }, 0f);
            var recovery = (RichTextLabel) typeof(WFShipShieldShuntScreen).GetField("_recovery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
            Assert.That(recovery.Visible, Is.True);
            Assert.That(recovery.Text, Is.EqualTo(Loc.GetString("wf-shield-recovery-ftl-locked")));
        });
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var shields = entities.System<ShipShieldsSystem>();
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", source.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var oldField = emitter.Shield!.Value;
            var shuttle = entities.EnsureComponent<ShuttleComponent>(source.Grid.Owner);
            entities.System<ShuttleSystem>().FTLToCoordinates(source.Grid.Owner, shuttle,
                new EntityCoordinates(target.MapUid, Vector2.Zero), Angle.Zero, startupTime: 30f, hyperspaceTime: 30f);
            var ftl = entities.GetComponent<FTLComponent>(source.Grid.Owner);
            Assert.That(ftl.State, Is.EqualTo(FTLState.Starting));
            Assert.That(emitter.Shield, Is.Null, "The field must drop inside the initiation call, before the next emitter tick.");
            Assert.That(!entities.EntityExists(oldField) || entities.IsQueuedForDeletion(oldField), Is.True);
            Assert.That(entities.TryGetComponent<ShipShieldedComponent>(source.Grid.Owner, out _), Is.False);
            Assert.That(emitter.Damage, Is.Zero);
            Assert.That(emitter.Recharging, Is.False, "FTL suppression must not manufacture damage or recharge punishment.");
            foreach (var state in new[] { FTLState.Starting, FTLState.Travelling, FTLState.Arriving, FTLState.Cooldown })
            {
                ftl.State = state;
                shields.Update(1.5f);
                Assert.That(emitter.Shield, Is.Null, $"No field may redeploy during {state}.");
                Assert.That(shields.IsWolfgateShieldFtlLocked(source.Grid.Owner), Is.True);
                Assert.That(shields.AbsorbWolfgateCollision(source.Grid.Owner, Vector2.Zero, 100f, 1f), Is.EqualTo(100f));
                var snapshot = entities.System<WFShipShieldShuntSystem>().GetState(source.Grid.Owner);
                Assert.That(snapshot.Available, Is.True);
                Assert.That(snapshot.Active, Is.False);
                Assert.That(snapshot.RecoveryStatus, Is.EqualTo(WFShipShieldRecoveryStatus.FtlLocked));
                Assert.That(snapshot.RecoverySeconds, Is.EqualTo(-1));
            }
            entities.RemoveComponent<FTLComponent>(source.Grid.Owner);
            Assert.That(shields.IsWolfgateShieldFtlLocked(source.Grid.Owner), Is.False);
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null, "Normal powered shields must return on their next update after cooldown removal.");
            var maps = entities.AllEntityQueryEnumerator<FTLMapComponent>();
            while (maps.MoveNext(out var uid, out _))
                entities.QueueDeleteEntity(uid);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [TestCase("manual")]
    [TestCase("recharge")]
    [TestCase("disabled")]
    public async Task EndingFtlDoesNotOverrideOtherReasonsForShieldBeingOffline(string reason)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var shields = entities.System<ShipShieldsSystem>();
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            shields.Update(1.5f);
            entities.EnsureComponent<FTLComponent>(map.Grid.Owner).State = FTLState.Cooldown;
            shields.SuppressWolfgateShieldForFtl(map.Grid.Owner);
            if (reason == "manual")
                shields.SetWolfgateShieldEnabled(map.Grid.Owner, false);
            else if (reason == "recharge")
            {
                emitter.Damage = emitter.DamageLimit;
                emitter.Recharging = true;
                emitter.HealPerSecond = 0f;
            }
            else
                entities.EnsureComponent<ShipShieldDisabledGridComponent>(map.Grid.Owner);
            entities.RemoveComponent<FTLComponent>(map.Grid.Owner);
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null, $"FTL completion must not bypass {reason} state.");
            Assert.That(entities.System<WFShipShieldShuntSystem>().GetState(map.Grid.Owner).Active, Is.False);
            Assert.That(entities.System<WFShipShieldShuntSystem>().GetState(map.Grid.Owner).RecoveryStatus,
                Is.Not.EqualTo(WFShipShieldRecoveryStatus.FtlLocked));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LinkedShuttleRemainsSuppressedByItsJumpLeaderThroughCooldown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var mainMap = await pair.CreateTestMap();
        var childMap = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var shields = entities.System<ShipShieldsSystem>();
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", childMap.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            shields.Update(1.5f);
            var main = entities.EnsureComponent<FTLComponent>(mainMap.Grid.Owner);
            var child = entities.EnsureComponent<FTLComponent>(childMap.Grid.Owner);
            child.LinkedShuttle = mainMap.Grid.Owner;
            child.State = FTLState.Available;
            foreach (var state in new[] { FTLState.Starting, FTLState.Travelling, FTLState.Arriving, FTLState.Cooldown })
            {
                main.State = state;
                shields.Update(0.01f);
                Assert.That(shields.IsWolfgateShieldFtlLocked(childMap.Grid.Owner), Is.True);
                Assert.That(emitter.Shield, Is.Null, "A linked grid must not regain its shield while its leader is still jumping.");
            }
            entities.RemoveComponent<FTLComponent>(mainMap.Grid.Owner);
            entities.RemoveComponent<FTLComponent>(childMap.Grid.Owner);
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DockedPartnerDropsImmediatelyDuringLeaderSpoolupAndCanRecoverAfterUndocking()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var maps = entities.System<SharedMapSystem>();
            var partner = pair.Server.System<SharedMapSystem>().CreateGridEntity(source.MapId);
            maps.SetTile(partner.Owner, partner.Comp, Vector2i.Zero, source.Tile.Tile);
            maps.SetTile(partner.Owner, partner.Comp, new Vector2i(1, 0), source.Tile.Tile);
            entities.System<SharedTransformSystem>().SetLocalPosition(partner.Owner, new Vector2(0f, 10f));
            var mainShuttle = entities.EnsureComponent<ShuttleComponent>(source.Grid.Owner);
            entities.EnsureComponent<ShuttleComponent>(partner.Owner);
            Assert.That(entities.EnsureComponent<FTLLockComponent>(partner.Owner).Enabled, Is.True);
            var mainDockUid = entities.SpawnEntity("AirlockShuttle", source.GridCoords);
            var partnerDockUid = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(partner.Owner, 0.5f, 0.5f));
            Entity<DockingComponent> mainDock = (mainDockUid, entities.GetComponent<DockingComponent>(mainDockUid));
            Entity<DockingComponent> partnerDock = (partnerDockUid, entities.GetComponent<DockingComponent>(partnerDockUid));
            var docking = entities.System<DockingSystem>();
            docking.Dock(mainDock, partnerDock);
            Assert.That(partnerDock.Comp.Docked, Is.True);
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", new EntityCoordinates(partner.Owner, 1.5f, 0.5f));
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            var shields = entities.System<ShipShieldsSystem>();
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            entities.System<ShuttleSystem>().FTLToCoordinates(source.Grid.Owner, mainShuttle,
                new EntityCoordinates(target.MapUid, Vector2.Zero), Angle.Zero, startupTime: 30f, hyperspaceTime: 30f);
            Assert.That(entities.GetComponent<FTLComponent>(source.Grid.Owner).State, Is.EqualTo(FTLState.Starting));
            Assert.That(entities.HasComponent<FTLComponent>(partner.Owner), Is.False,
                "Docked protection must drop during spoolup before linked FTL state is assigned at departure.");
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(shields.IsWolfgateShieldFtlLocked(partner.Owner), Is.True);
            docking.Undock(partnerDock);
            Assert.That(shields.IsWolfgateShieldFtlLocked(partner.Owner), Is.False);
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null, "An undocked partner no longer belongs to the jumping group.");
            entities.RemoveComponent<FTLComponent>(source.Grid.Owner);
            var ftlMaps = entities.AllEntityQueryEnumerator<FTLMapComponent>();
            while (ftlMaps.MoveNext(out var uid, out _))
                entities.QueueDeleteEntity(uid);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

}
