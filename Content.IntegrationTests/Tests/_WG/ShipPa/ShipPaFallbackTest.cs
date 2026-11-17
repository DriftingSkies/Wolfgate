#nullable enable
using System.Collections.Generic;
using Content.Server._WG.ShipPa;
using Content.Server.Power.Components;
using Content.Shared._WG.ShipPa;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.ShipPa;

/// <summary>
/// A ship with no PA speakers announces through its air alarms; the first dedicated speaker takes over.
/// </summary>
[TestFixture]
[TestOf(typeof(ShipPaSystem))]
public sealed class ShipPaFallbackTest
{
    private static readonly SoundPathSpecifier Chime = new("/Audio/Announcements/attention.ogg");

    [Test]
    public async Task AirAlarmsStandInUntilASpeakerIsPlaced()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var pa = entMan.System<ShipPaSystem>();
        var grid = map.Grid.Owner;

        EntityUid alarm = default;
        EntityUid speaker = default;

        await server.WaitAssertion(() =>
        {
            alarm = entMan.SpawnEntity("AirAlarm", map.GridCoords);
            entMan.GetComponent<ApcPowerReceiverComponent>(alarm).NeedsPower = false;

            var comp = entMan.GetComponent<ShipPaSpeakerComponent>(alarm);
            Assert.That(comp.Fallback, Is.True, "Air alarms should carry a fallback speaker.");
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var counts = pa.CountSpeakers(grid);
            Assert.That(counts.Fallback, Is.True, "With no speakers the ship should be running on its air alarms.");
            Assert.That(counts.Total, Is.EqualTo(1));
            Assert.That(counts.Online, Is.EqualTo(1));

            // Only the console creates the alert state for an air-alarm ship; alarms alone must not.
            Assert.That(entMan.HasComponent<ShipAlertComponent>(grid), Is.False,
                "Air alarms alone should not give every grid an alert component.");

            var id = pa.Broadcast(grid, Chime);
            Assert.That(id, Is.Not.Null, "The air alarm should carry a broadcast.");
            Assert.That(StreamsFor(entMan, id!.Value), Is.Empty);
            Assert.That(entMan.GetComponent<ShipPaSpeakerComponent>(alarm).Enabled, Is.True);

            speaker = entMan.SpawnEntity("WGWallmountShipPaSpeaker", map.GridCoords);
            entMan.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var counts = pa.CountSpeakers(grid);
            Assert.That(counts.Fallback, Is.False, "A dedicated speaker should take the ship off its air alarms.");
            Assert.That(counts.Total, Is.EqualTo(1));

            var alert = entMan.GetComponent<ShipAlertComponent>(grid);
            Assert.That(alert.SpeakersFallback, Is.False);
            Assert.That(alert.SpeakersTotal, Is.EqualTo(1));

            var id = pa.Broadcast(grid, Chime);
            Assert.That(id, Is.Not.Null);
            Assert.That(StreamsFor(entMan, id!.Value), Is.Empty);
            Assert.That(entMan.GetComponent<ShipPaSpeakerComponent>(alarm).Enabled, Is.False);
            Assert.That(entMan.GetComponent<ShipPaSpeakerComponent>(speaker).Enabled, Is.True);

            // Losing the only speaker puts the ship back on its air alarms.
            entMan.DeleteEntity(speaker);
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var counts = pa.CountSpeakers(grid);
            Assert.That(counts.Fallback, Is.True, "Without any speaker the air alarms should take over again.");
            Assert.That(entMan.GetComponent<ShipAlertComponent>(grid).SpeakersFallback, Is.True);
            Assert.That(entMan.GetComponent<ShipPaSpeakerComponent>(alarm).Enabled, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The speakers a broadcast's streams are parented to.</summary>
    private static List<EntityUid> StreamsFor(IEntityManager entMan, int broadcastId)
    {
        var speakers = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<ShipPaAudioComponent>();

        while (query.MoveNext(out _, out var audio))
        {
            if (audio.BroadcastId == broadcastId && audio.Speaker is { } speaker)
                speakers.Add(speaker);
        }

        return speakers;
    }
}
