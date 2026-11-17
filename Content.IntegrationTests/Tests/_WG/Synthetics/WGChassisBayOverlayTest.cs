#nullable enable
using System.Threading.Tasks;
using Content.Client._WG.Synthetics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.Synthetics;
using Robust.Client.Graphics;

namespace Content.IntegrationTests.Tests._WG.Synthetics;

/// <summary>Playtest: "there's no overlay for opening the bay". The owner's client shows it while their bay is open.</summary>
public sealed class WGChassisBayOverlayTest : InteractionTest
{
    [Test]
    public async Task OwnerSeesBayOverlayTest()
    {
        var overlays = Client.ResolveDependency<IOverlayManager>();

        await Server.WaitPost(() =>
        {
            var chassis = SEntMan.EnsureComponent<WGChassisComponent>(SPlayer);
            chassis.BayOpen = true;
            SEntMan.Dirty(SPlayer, chassis);
        });
        await RunTicks(30);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetComponent(CPlayer, out WGChassisComponent? chassis) && chassis.BayOpen,
                "the open bay never reached the client.");
            Assert.That(overlays.TryGetOverlay(out WGChassisBayOverlay? overlay), "the bay overlay was not added.");
            Assert.That(overlay!.BayAlpha, Is.GreaterThan(0.5f), "the bay overlay is not fading in.");
        });

        await Server.WaitPost(() =>
        {
            var chassis = SEntMan.GetComponent<WGChassisComponent>(SPlayer);
            chassis.BayOpen = false;
            SEntMan.Dirty(SPlayer, chassis);
        });
        await RunTicks(90);

        await Client.WaitAssertion(() =>
        {
            Assert.That(overlays.HasOverlay<WGChassisBayOverlay>(), Is.False, "the bay overlay stayed after the bay closed.");
        });
    }
}
