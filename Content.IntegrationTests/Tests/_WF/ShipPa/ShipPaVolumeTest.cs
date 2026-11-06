#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.ShipPa;
using Content.Server.Power.Components;
using Content.Shared._WF.ShipPa;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ShipPa;

/// <summary>The console's PA volume slider, which the server keeps between 20% and 100%.</summary>
[TestFixture]
[TestOf(typeof(ShipAlertSystem))]
public sealed class ShipPaVolumeTest : InteractionTest
{
    [Test]
    public async Task VolumeIsClampedToTheAllowedRange()
    {
        await SpawnTarget("ComputerShuttle");
        var console = SEntMan.GetEntity(Target!.Value);
        var grid = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            var speaker = SEntMan.SpawnEntity("WFWallmountShipPaSpeaker", MapData.GridCoords);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
            grid = SEntMan.GetComponent<TransformComponent>(console).GridUid!.Value;
        });
        await RunTicks(20);
        await Activate();
        Assert.That(IsUiOpen(ShuttleConsoleUiKey.Key), Is.True);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Volume(grid), Is.EqualTo(1f), "A ship starts at full volume.");

            Send(console, 0.5f);
            Assert.That(Volume(grid), Is.EqualTo(0.5f).Within(0.001f));

            Send(console, 0f);
            Assert.That(Volume(grid), Is.EqualTo(ShipAlertComponent.MinVolume), "The PA can't be turned below 20%.");

            Send(console, 5f);
            Assert.That(Volume(grid), Is.EqualTo(1f), "The PA can't be turned above 100%.");

            Send(console, float.NaN);
            Assert.That(Volume(grid), Is.EqualTo(1f), "A bad value is ignored.");
        });
    }

    private float Volume(EntityUid grid)
    {
        return SEntMan.GetComponent<ShipAlertComponent>(grid).Volume;
    }

    private void Send(EntityUid console, float volume)
    {
        var message = new ShipPaVolumeRequestMessage(volume) { Actor = SPlayer };
        SEntMan.System<SharedUserInterfaceSystem>().RaiseUiMessage(console, ShuttleConsoleUiKey.Key, message);
    }
}
