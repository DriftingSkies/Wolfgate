#nullable enable
using Content.Shared._CE.ZLevels.Flight;
using Content.Shared._CE.ZLevels.Flight.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Avali;

/// <summary>An Avali flies between z-levels the way a moth does: same settings, same actions, and it takes off.</summary>
[TestFixture]
public sealed class AvaliFlightTest
{
    [Test]
    public async Task AvaliFliesLikeAMothTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var avali = entMan.SpawnEntity("MobAvali", map.GridCoords);
            var moth = entMan.SpawnEntity("MobMoth", map.GridCoords);
            var flyer = entMan.GetComponent<CEZFlyerComponent>(avali);
            var mothFlyer = entMan.GetComponent<CEZFlyerComponent>(moth);
            var control = entMan.GetComponent<CEControllableFlightComponent>(avali);
            var mothControl = entMan.GetComponent<CEControllableFlightComponent>(moth);

            Assert.Multiple(() =>
            {
                Assert.That(flyer.FlightSpeed, Is.EqualTo(mothFlyer.FlightSpeed));
                Assert.That(flyer.FlightMoveSpeedModifier, Is.EqualTo(mothFlyer.FlightMoveSpeedModifier));
                Assert.That(flyer.HoverStaminaDrain, Is.EqualTo(mothFlyer.HoverStaminaDrain));
                Assert.That(control.StartFlightDoAfter, Is.EqualTo(mothControl.StartFlightDoAfter));
                Assert.That(control.ZLevelToggleActionEntity, Is.Not.Null, "The Avali has no flight toggle.");
                Assert.That(control.ZLevelUpActionEntity, Is.Not.Null, "The Avali has no ascend action.");
                Assert.That(control.ZLevelDownActionEntity, Is.Not.Null, "The Avali has no descend action.");
            });

            Assert.That(entMan.System<CESharedZFlightSystem>().TryActivateFlight(avali), Is.True, "The Avali did not take off.");
            Assert.That(flyer.Active, Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
