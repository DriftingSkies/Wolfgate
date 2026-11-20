#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.CombatFacing;
using Content.Shared.CombatMode;
using Content.Shared.MouseRotator;
using Content.Shared.Movement.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.CombatFacing;

/// <summary>The face-the-cursor option decides whether combat mode adds the mouse rotator, read per player.</summary>
public sealed class CombatFacingTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task OptionDecidesCursorFacing()
    {
        await SetCombat(true);
        Assert.That(FacesCursor(), Is.True, "Combat mode should face the cursor by default.");
        await SetCombat(false);
        Assert.That(FacesCursor(), Is.False);

        try
        {
            await SetOption(false);
            await SetCombat(true);
            Assert.That(SEntMan.System<SharedCombatModeSystem>().IsInCombatMode(SPlayer), Is.True,
                "With the option off, the mob should still enter combat mode.");
            Assert.That(FacesCursor(), Is.False, "The server ignored the player's option.");
            Assert.That(SEntMan.HasComponent<NoRotateOnMoveComponent>(SPlayer), Is.False);

            await Server.WaitPost(() => Transform.SetWorldRotation(SPlayer, Angle.Zero));
            await RunTicks(5);
            Assert.That(Transform.GetWorldRotation(SPlayer).GetCardinalDir(), Is.Not.EqualTo(Direction.West));

            await SetKey(EngineKeyFunctions.MoveLeft, BoundKeyState.Down);
            await RunTicks(10);
            await SetKey(EngineKeyFunctions.MoveLeft, BoundKeyState.Up);
            await RunTicks(5);
            Assert.That(Transform.GetWorldRotation(SPlayer).GetCardinalDir(), Is.EqualTo(Direction.West),
                "With the option off, moving should still turn the mob in combat mode.");

            await SetCombat(false);
        }
        finally
        {
            // The client is pooled.
            await SetOption(true);
        }
    }

    [Test]
    public async Task ChangingTheOptionMidFightApplies()
    {
        await SetCombat(true);
        Assert.That(FacesCursor(), Is.True);

        try
        {
            await SetOption(false);
            Assert.That(FacesCursor(), Is.False, "Turning the option off mid-fight left the rotator on.");
            Assert.That(SEntMan.HasComponent<NoRotateOnMoveComponent>(SPlayer), Is.False);
            Assert.That(ClientFacesCursor(), Is.False, "The client kept the rotator.");
            Assert.That(CEntMan.HasComponent<NoRotateOnMoveComponent>(CPlayer), Is.False);
        }
        finally
        {
            // The client is pooled.
            await SetOption(true);
        }

        Assert.That(FacesCursor(), Is.True, "Turning the option back on mid-fight didn't face the cursor.");
        Assert.That(SEntMan.HasComponent<NoRotateOnMoveComponent>(SPlayer), Is.True);
        Assert.That(ClientFacesCursor(), Is.True, "The client never got the rotator back.");
        Assert.That(CEntMan.HasComponent<NoRotateOnMoveComponent>(CPlayer), Is.True);

        await SetCombat(false);
        Assert.That(FacesCursor(), Is.False);
        Assert.That(SEntMan.HasComponent<NoRotateOnMoveComponent>(SPlayer), Is.False,
            "Leaving combat left the mob unable to turn by moving.");
    }

    private bool FacesCursor() => SEntMan.HasComponent<MouseRotatorComponent>(SPlayer);

    private bool ClientFacesCursor() => CEntMan.HasComponent<MouseRotatorComponent>(CPlayer);

    private async Task SetCombat(bool on)
    {
        await Server.WaitPost(() => SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(SPlayer, on));
        await RunTicks(5);
    }

    private async Task SetOption(bool faceCursor)
    {
        await Client.WaitPost(() =>
            Client.ResolveDependency<IConfigurationManager>().SetCVar(WGCombatFacingCVars.FaceCursor, faceCursor));
        await RunTicks(10);
    }
}
