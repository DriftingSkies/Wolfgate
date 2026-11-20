#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.HandSelect;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.HandSelect;

/// <summary>The swap-to-left and swap-to-right keys make a hand on that side active.</summary>
[TestOf(typeof(WGHandSelectSystem))]
public sealed class HandSelectTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task KeysPickASide()
    {
        var hands = SEntMan.System<SharedHandsSystem>();

        // The fixture leaves one hand; give the player one on each side.
        await Server.WaitPost(() =>
        {
            hands.AddHand(SPlayer, "wg_test_left", HandLocation.Left);
            hands.AddHand(SPlayer, "wg_test_right", HandLocation.Right);
        });
        await RunTicks(5);

        await PressKey(WGHandSelectKeys.WGSelectRightHand);
        await RunTicks(5);
        Assert.That(await ActiveSide(), Is.EqualTo(HandLocation.Right));

        await PressKey(WGHandSelectKeys.WGSelectLeftHand);
        await RunTicks(5);
        Assert.That(await ActiveSide(), Is.EqualTo(HandLocation.Left));
    }

    private async Task<HandLocation?> ActiveSide()
    {
        HandLocation? side = null;
        await Server.WaitPost(() => side = SEntMan.GetComponent<HandsComponent>(SPlayer).ActiveHand?.Location);
        return side;
    }
}
