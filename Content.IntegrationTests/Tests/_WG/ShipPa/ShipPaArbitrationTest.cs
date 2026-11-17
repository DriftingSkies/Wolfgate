#nullable enable
using System.Linq;
using Content.Server._WG.ShipPa;
using Content.Server.Power.Components;
using Content.Server._WG.Shuttles.Systems;
using Content.Shared._WG.ShipPa;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ShipPa;

[TestFixture]
[TestOf(typeof(ShipPaSystem))]
public sealed class ShipPaArbitrationTest
{
    private const string Chime = "/Audio/Announcements/attention.ogg";

    [Test]
    public async Task ImminentAlarmOutranksAnnouncementsAndGeneralQuarters()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var pa = entMan.System<ShipPaSystem>();
        await server.WaitPost(() =>
        {
            var speaker = entMan.SpawnEntity("WGWallmountShipPaSpeaker", map.GridCoords);
            entMan.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            Assert.That(pa.Announce(grid, "Routine announcement"), Is.True);
            pa.StartAlarm(grid, CollisionWarningSystem.ImminentAlarm, new SoundPathSpecifier(Chime),
                priority: CollisionWarningSystem.ImminentPriority);
            pa.StartAlarm(grid, "general-quarters", new SoundPathSpecifier(Chime));

            var broadcasts = entMan.GetComponent<ShipPaBroadcastComponent>(grid).Broadcasts;
            Assert.That(broadcasts.Single(b => b.Key == "announcement").Priority,
                Is.EqualTo(ShipPaPlaybackPolicy.AnnouncementPriority));
            Assert.That(broadcasts.Single(b => b.Key == "general-quarters").Priority,
                Is.EqualTo(ShipPaPlaybackPolicy.DefaultAlarmPriority));
            Assert.That(broadcasts.Single(b => b.Key == CollisionWarningSystem.ImminentAlarm).Priority,
                Is.GreaterThan(ShipPaPlaybackPolicy.AnnouncementPriority));
            Assert.That(broadcasts.MaxBy(b => b.Priority)!.Key,
                Is.EqualTo(CollisionWarningSystem.ImminentAlarm));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdvisoryCalloutUsesIndependentKeyAndStopsWithoutRemovingCaption()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var pa = entMan.System<ShipPaSystem>();
        await server.WaitPost(() =>
        {
            var speaker = entMan.SpawnEntity("WGWallmountShipPaSpeaker", map.GridCoords);
            entMan.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            Assert.That(pa.Announce(grid, "Human caption"), Is.True);
            var calloutId = pa.Broadcast(grid, new SoundPathSpecifier(Chime),
                priority: ShipPaPlaybackPolicy.AdvisoryCalloutPriority,
                key: CollisionWarningSystem.AdvisoryCallout);
            Assert.That(calloutId, Is.Not.Null);

            var broadcasts = entMan.GetComponent<ShipPaBroadcastComponent>(grid).Broadcasts;
            Assert.That(broadcasts.Single(b => b.Key == "announcement").Caption, Is.EqualTo("Human caption"));
            Assert.That(broadcasts.Single(b => b.Key == CollisionWarningSystem.AdvisoryCallout).Priority,
                Is.EqualTo(ShipPaPlaybackPolicy.AdvisoryCalloutPriority));

            Assert.That(ShipPaPlaybackPolicy.AdvisoryCalloutPriority, Is.LessThan(ShipPaPlaybackPolicy.DefaultAlarmPriority));
            Assert.That(ShipPaPlaybackPolicy.AdvisoryCalloutPriority, Is.GreaterThan(ShipPaPlaybackPolicy.AdvisoryPriority));
            pa.StopAlarm(grid, CollisionWarningSystem.AdvisoryCallout);
            Assert.That(entMan.GetComponent<ShipPaBroadcastComponent>(grid).Broadcasts.Count(b => b.Key == "announcement"),
                Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }
}
