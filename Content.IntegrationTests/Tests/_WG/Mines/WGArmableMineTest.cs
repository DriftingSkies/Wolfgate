using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.Mines;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Toggleable;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Mines;

/// <summary>An armable mine is set on a free tile next to the player, arms after its delay and is disarmed by a multitool.</summary>
public sealed class WGArmableMineTest : InteractionTest
{
    private const string Mine = "WGLandMineExplosive";

    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task SetArmDisarm()
    {
        var mine = await PlaceInHands(Mine);
        var sMine = ToServer(mine);
        var comp = SEntMan.GetComponent<WGArmableMineComponent>(sMine);
        var mines = SEntMan.System<WGArmableMineSystem>();

        // The player's own tile is refused, so nobody is left standing on a mine.
        await Server.WaitAssertion(() =>
        {
            Assert.That(mines.CanSet(ToServer(PlayerCoords), out var reason), Is.False);
            Assert.That(reason, Is.EqualTo("wg-mine-set-someone"));
        });
        await Interact(null, PlayerCoords);
        Assert.That(HandSys.IsHolding(SPlayer, sMine), Is.True);
        Assert.That(comp.State, Is.EqualTo(WGMineState.Safe));
        Assert.That(await CanTrigger(sMine), Is.False);

        await Interact(null, TargetCoords);
        Assert.That(HandSys.IsHolding(SPlayer, sMine), Is.False);
        Assert.That(Xform(sMine).Anchored, Is.True);
        Assert.That(comp.State, Is.EqualTo(WGMineState.Arming));
        Assert.That(await CanTrigger(sMine), Is.False);

        await RunSeconds((float) comp.ArmDelay.TotalSeconds + 1);
        Assert.That(comp.State, Is.EqualTo(WGMineState.Armed));
        Assert.That(await CanTrigger(sMine), Is.True);
        Assert.That(SEntMan.System<SharedAppearanceSystem>().TryGetData<bool>(sMine, ToggleableVisuals.Enabled, out var lit) && lit, Is.True);

        // A second mine can't go on the same tile.
        await Server.WaitAssertion(() =>
        {
            Assert.That(mines.CanSet(ToServer(TargetCoords), out var reason), Is.False);
            Assert.That(reason, Is.EqualTo("wg-mine-set-occupied"));
        });

        Target = mine;
        await InteractUsing("Multitool");
        Assert.That(comp.State, Is.EqualTo(WGMineState.Safe));
        Assert.That(Xform(sMine).Anchored, Is.False);
        Assert.That(await CanTrigger(sMine), Is.False);

        // Stepping off a disarmed mine, as someone standing on it during the disarm would, doesn't set it off.
        await Server.WaitPost(() =>
        {
            var ev = new StepTriggeredOffEvent(sMine, SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(sMine, ref ev);
        });
        await RunTicks(5);
        Assert.That(SEntMan.Deleted(sMine), Is.False);
    }

    [Test]
    public async Task ArmedMineGoesOffWhenSteppedOff()
    {
        var mine = await PlaceInHands(Mine);
        var sMine = ToServer(mine);
        var comp = SEntMan.GetComponent<WGArmableMineComponent>(sMine);

        await Interact(null, TargetCoords);
        await RunSeconds((float) comp.ArmDelay.TotalSeconds + 1);
        Assert.That(comp.State, Is.EqualTo(WGMineState.Armed));

        await Server.WaitPost(() =>
        {
            var ev = new StepTriggeredOffEvent(sMine, SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(sMine, ref ev);
        });
        await RunTicks(5);
        Assert.That(SEntMan.Deleted(sMine), Is.True);
    }

    private async Task<bool> CanTrigger(EntityUid mine)
    {
        var result = false;
        await Server.WaitPost(() =>
        {
            var ev = new StepTriggerAttemptEvent { Source = mine, Tripper = SPlayer };
            SEntMan.EventBus.RaiseLocalEvent(mine, ref ev);
            result = ev.Continue && !ev.Cancelled;
        });
        return result;
    }
}
