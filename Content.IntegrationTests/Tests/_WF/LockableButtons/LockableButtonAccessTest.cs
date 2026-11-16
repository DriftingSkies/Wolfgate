using System.Collections.Generic;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.Lock;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.LockableButtons;

/// <summary>
/// A locked button is denied without access and does not fire; with access it fires and stays locked; once
/// unlocked, anyone can press it.
/// </summary>
public sealed class LockableButtonAccessTest : InteractionTest
{
    private const string Access = "TsfmcArmory";

    [Test]
    public async Task LockedButtonFiresOnlyWithAccess()
    {
        await SpawnTarget("LockableButtonArmory");
        var button = STarget!.Value;
        var lockSys = SEntMan.System<LockSystem>();
        var readers = SEntMan.System<AccessReaderSystem>();
        var signal = SEntMan.GetComponent<SignalSwitchComponent>(button);

        Assert.That(lockSys.IsLocked(button), Is.True);
        Assert.That(signal.State, Is.False);

        await Activate();
        Assert.Multiple(() =>
        {
            Assert.That(signal.State, Is.False, "A denied press must not fire the button.");
            Assert.That(lockSys.IsLocked(button), Is.True);
        });

        // A card with the button's access presses it without unlocking it.
        var card = await PlaceInHands("PassengerIDCard");
        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedAccessSystem>().TrySetTags(SEntMan.GetEntity(card), new List<ProtoId<AccessLevelPrototype>> { Access });
            Assert.That(readers.IsAllowed(SPlayer, button), Is.True, "Precondition: the held card has the button's access.");
        });
        await RunSeconds(1f);

        await Activate();
        Assert.Multiple(() =>
        {
            Assert.That(signal.State, Is.True, "A press with access fires the button.");
            Assert.That(lockSys.IsLocked(button), Is.True, "Pressing with access leaves the button locked.");
        });

        await DeleteHeldEntity();
        await Server.WaitPost(() => lockSys.Unlock(button, null));
        await RunSeconds(1f);

        var before = signal.State;
        await Activate();
        Assert.That(signal.State, Is.Not.EqualTo(before), "An unlocked button fires for anyone.");
    }
}
