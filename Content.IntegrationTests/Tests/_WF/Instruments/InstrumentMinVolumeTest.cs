#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Instruments;
using Content.Shared.Instruments.UI;
using ClientInstrumentSystem = Content.Client.Instruments.InstrumentSystem;
using ServerInstrumentComponent = Content.Server.Instruments.InstrumentComponent;

namespace Content.IntegrationTests.Tests._WF.Instruments;

/// <summary>
/// The overhauled instrument window opens, and its min volume (wizden#41272) reaches the server only from the player.
/// </summary>
[TestFixture]
public sealed class InstrumentMinVolumeTest : InteractionTest
{
    [Test]
    public async Task MinVolumeFromPlayer()
    {
        await SpawnTarget("SynthesizerInstrument");
        await Activate();
        Assert.That(IsUiOpen(InstrumentUiKey.Key), Is.True);

        var instruments = CEntMan.System<ClientInstrumentSystem>();
        await Client.WaitPost(() => instruments.SetMinVolume(CTarget!.Value, 200));
        await RunTicks(10);
        Assert.That(SEntMan.GetComponent<ServerInstrumentComponent>(STarget!.Value).MinVolume, Is.EqualTo(127));

        // Someone who isn't playing it can't change it.
        await CloseBui(InstrumentUiKey.Key);
        await RunTicks(5);
        await Client.WaitPost(() => instruments.SetMinVolume(CTarget!.Value, 10));
        await RunTicks(10);
        Assert.That(SEntMan.GetComponent<ServerInstrumentComponent>(STarget!.Value).MinVolume, Is.EqualTo(127));
    }
}
