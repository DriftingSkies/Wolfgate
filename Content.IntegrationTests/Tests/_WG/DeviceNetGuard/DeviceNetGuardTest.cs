#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Tests.DeviceNetwork;
using Content.Server.DeviceNetwork.Systems;
using Content.Shared._WG.DeviceNetGuard;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.DeviceNetGuard;

/// <summary>
///     The device network sends a bounded amount per tick and drops a runaway backlog instead of stalling the tick.
/// </summary>
[TestFixture]
[TestOf(typeof(DeviceNetworkSystem))]
public sealed class DeviceNetGuardTest
{
    private const int Packets = 5;
    private const string Counter = "wg_guard_test_n";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGDeviceNetGuardDummy
  components:
  - type: DeviceNetwork
    transmitFrequency: 7411
    receiveFrequency: 7411
";

    [Test]
    public async Task OverBudgetPacketsWaitInOrder()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var deviceNet = server.System<DeviceNetworkSystem>();
        var probe = server.System<DeviceNetworkTestSystem>();
        var cfg = server.CfgMan;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(DeviceNetGuardCVars.TickBudgetMs, 0f);
            cfg.SetCVar(DeviceNetGuardCVars.MinPacketsPerTick, 1);
            QueueCounted(server.EntMan, deviceNet);
        });

        // With no time budget and a minimum of one, exactly one packet goes out per tick.
        var seen = new List<int>();
        for (var tick = 0; tick < Packets + 3 && (seen.Count == 0 || seen[^1] < Packets - 1); tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                if (probe.LastPayload != null && probe.LastPayload.TryGetValue(Counter, out int n) && (seen.Count == 0 || seen[^1] != n))
                    seen.Add(n);
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(seen, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }), "Packets should arrive one per tick, in the order queued.");
            Assert.That(deviceNet.Backlog, Is.Zero);

            cfg.SetCVar(DeviceNetGuardCVars.TickBudgetMs, DeviceNetGuardCVars.TickBudgetMs.DefaultValue);
            cfg.SetCVar(DeviceNetGuardCVars.MinPacketsPerTick, DeviceNetGuardCVars.MinPacketsPerTick.DefaultValue);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RunawayBacklogIsDropped()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var deviceNet = server.System<DeviceNetworkSystem>();
        var probe = server.System<DeviceNetworkTestSystem>();
        var cfg = server.CfgMan;

        // Dropping a storm is logged as an error on purpose.
        var failureLevel = pair.ServerLogHandler.FailureLevel;
        pair.ServerLogHandler.FailureLevel = LogLevel.Fatal;

        long shedBefore = 0;
        await server.WaitPost(() =>
        {
            shedBefore = deviceNet.ShedPackets;
            probe.LastPayload = null;
            cfg.SetCVar(DeviceNetGuardCVars.MaxBacklog, Packets - 1);
            QueueCounted(server.EntMan, deviceNet);
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(deviceNet.Backlog, Is.Zero);
            Assert.That(deviceNet.ShedPackets - shedBefore, Is.EqualTo(Packets));
            Assert.That(probe.LastPayload == null || !probe.LastPayload.ContainsKey(Counter),
                "A dropped packet was still delivered.");

            cfg.SetCVar(DeviceNetGuardCVars.MaxBacklog, DeviceNetGuardCVars.MaxBacklog.DefaultValue);
        });

        pair.ServerLogHandler.FailureLevel = failureLevel;
        await pair.CleanReturnAsync();
    }

    /// <summary>Queues <see cref="Packets"/> numbered packets from one new device to another.</summary>
    private static void QueueCounted(IEntityManager entMan, DeviceNetworkSystem deviceNet)
    {
        var sender = entMan.SpawnEntity("WGDeviceNetGuardDummy", MapCoordinates.Nullspace);
        var receiver = entMan.SpawnEntity("WGDeviceNetGuardDummy", MapCoordinates.Nullspace);
        var address = entMan.GetComponent<DeviceNetworkComponent>(receiver).Address;

        for (var i = 0; i < Packets; i++)
        {
            Assert.That(deviceNet.QueuePacket(sender, address, new NetworkPayload { [Counter] = i }), Is.True);
        }
    }
}
