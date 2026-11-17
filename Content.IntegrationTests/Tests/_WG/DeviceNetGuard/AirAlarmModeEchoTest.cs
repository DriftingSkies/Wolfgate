#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Monitor.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Power.Components;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.DeviceNetwork.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.DeviceNetGuard;

/// <summary>
///     Linked air alarms that set different modes in the same tick settle on one of them. They used to echo each
///     other's mode back every tick forever, flipping every vent and scrubber on the network (the 2026-11-11 packet
///     storms). A mode still passes along a chain of alarms that are not all linked to each other.
/// </summary>
[TestFixture]
[TestOf(typeof(AirAlarmSystem))]
public sealed class AirAlarmModeEchoTest
{
    [Test]
    public async Task ConflictingModesSettle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var alarms = server.System<AirAlarmSystem>();
        var deviceNet = server.System<DeviceNetworkSystem>();

        EntityUid a = default, b = default;
        await server.WaitPost(() =>
        {
            a = entities.SpawnEntity("AirAlarm", map.GridCoords);
            b = entities.SpawnEntity("AirAlarm", map.GridCoords);
            foreach (var alarm in new[] { a, b })
            {
                entities.GetComponent<ApcPowerReceiverComponent>(alarm).NeedsPower = false;
                entities.GetComponent<AirAlarmComponent>(alarm).AutoMode = false;
            }

            // Alarms broadcast only to the devices linked to them, as mappers link a ship's rooms.
            var lists = entities.System<DeviceListSystem>();
            lists.UpdateDeviceList(a, new[] { b });
            lists.UpdateDeviceList(b, new[] { a });
        });
        await WaitUntilPowered(pair, a, b);

        await server.WaitPost(() =>
        {
            // Two rooms disagree in the same tick, as auto mode does when one goes Danger and another Normal.
            alarms.SetMode(a, entities.GetComponent<DeviceNetworkComponent>(a).Address, AirAlarmMode.WideFiltering, false);
            alarms.SetMode(b, entities.GetComponent<DeviceNetworkComponent>(b).Address, AirAlarmMode.Panic, false);
        });

        await pair.RunTicksSync(5);
        (AirAlarmMode, AirAlarmMode) Modes() => (entities.GetComponent<AirAlarmComponent>(a).CurrentMode,
            entities.GetComponent<AirAlarmComponent>(b).CurrentMode);

        var settled = (AirAlarmMode.None, AirAlarmMode.None);
        await server.WaitAssertion(() =>
        {
            settled = Modes();
            Assert.That(settled.Item1, Is.EqualTo(settled.Item2), "The alarms did not converge on one mode.");
            Assert.That(settled.Item1, Is.AnyOf(AirAlarmMode.WideFiltering, AirAlarmMode.Panic));
        });

        for (var tick = 0; tick < 10; tick++)
        {
            await pair.RunTicksSync(1);
            await server.WaitAssertion(() =>
            {
                Assert.That(Modes(), Is.EqualTo(settled), "The alarms kept echoing modes back and forth.");
                Assert.That(deviceNet.Backlog, Is.Zero, "Mode packets are still going round.");
            });
        }

        await pair.CleanReturnAsync();
    }

    /// <param name="middleHasIt">The middle alarm is already in the mode being set.</param>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ModePassesAlongAChain(bool middleHasIt)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var alarms = server.System<AirAlarmSystem>();
        var deviceNet = server.System<DeviceNetworkSystem>();

        EntityUid a = default, b = default, c = default;
        await server.WaitPost(() =>
        {
            a = entities.SpawnEntity("AirAlarm", map.GridCoords);
            b = entities.SpawnEntity("AirAlarm", map.GridCoords);
            c = entities.SpawnEntity("AirAlarm", map.GridCoords);
            foreach (var alarm in new[] { a, b, c })
            {
                entities.GetComponent<ApcPowerReceiverComponent>(alarm).NeedsPower = false;
                entities.GetComponent<AirAlarmComponent>(alarm).AutoMode = false;
            }

            // A reaches C only through B.
            var lists = entities.System<DeviceListSystem>();
            lists.UpdateDeviceList(a, new[] { b });
            lists.UpdateDeviceList(b, new[] { a, c });
            lists.UpdateDeviceList(c, new[] { b });

            if (middleHasIt)
                entities.GetComponent<AirAlarmComponent>(b).CurrentMode = AirAlarmMode.Panic;
        });
        await WaitUntilPowered(pair, a, b, c);

        await server.WaitPost(() =>
            alarms.SetMode(a, entities.GetComponent<DeviceNetworkComponent>(a).Address, AirAlarmMode.Panic, false));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            foreach (var alarm in new[] { a, b, c })
            {
                Assert.That(entities.GetComponent<AirAlarmComponent>(alarm).CurrentMode, Is.EqualTo(AirAlarmMode.Panic));
            }

            Assert.That(deviceNet.Backlog, Is.Zero, "Mode packets are still going round.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Alarms drop packets until the power net marks them powered, which it does every half second, not every tick.
    /// </summary>
    private static async Task WaitUntilPowered(TestPair pair, params EntityUid[] alarms)
    {
        var entities = pair.Server.EntMan;
        for (var tick = 0; tick < 120; tick++)
        {
            if (alarms.All(alarm => entities.GetComponent<ApcPowerReceiverComponent>(alarm).Powered))
                return;

            await pair.RunTicksSync(1);
        }

        Assert.Fail("The air alarms never became powered.");
    }
}
