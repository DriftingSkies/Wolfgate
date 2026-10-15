#nullable enable
using System.Collections.Generic;
using Content.Server._WF.Encounters;
using Content.Server._WF.ShipPa;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ShipPa;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.ShipPa;

/// <summary>The SOS button's call: it marks the ship on the sector markers, cancels and lapses.</summary>
[TestFixture]
[TestOf(typeof(ShipAlertSystem))]
public sealed class ShipDistressTest
{
    [Test]
    public async Task DistressCallMarksAndLapses()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var alerts = entMan.System<ShipAlertSystem>();
        var grid = map.Grid.Owner;

        List<WFEncounterMarker> Collect()
        {
            var markers = new List<WFEncounterMarker>();
            var ev = new WFEncounterCollectMarkersEvent(markers);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            return markers;
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(Collect(), Is.Empty);

            alerts.SetDistress(grid, true);
            var alert = entMan.GetComponent<ShipAlertComponent>(grid);
            Assert.That(alert.Distress, Is.True);
            Assert.That(alert.DistressEnds, Is.GreaterThan(timing.CurTime));

            var markers = Collect();
            Assert.That(markers, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(markers[0].Grid, Is.EqualTo(entMan.GetNetEntity(grid)));
                Assert.That(markers[0].Category, Is.EqualTo(WFEncounterCategory.Distress));
                Assert.That(markers[0].Map, Is.EqualTo(map.MapId));
            });

            alerts.SetDistress(grid, false);
            Assert.That(alert.Distress, Is.False);
            Assert.That(Collect(), Is.Empty);

            // A standing call lapses on its own once its time is up.
            alerts.SetDistress(grid, true);
            alert.DistressEnds = timing.CurTime;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ShipAlertComponent>(grid).Distress, Is.False);
            Assert.That(Collect(), Is.Empty);
        });

        await pair.CleanReturnAsync();
    }
}
