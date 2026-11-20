#nullable enable
using System.Numerics;
using Content.Client._WG.PixelShift;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Cuffs;
using Content.Shared._WG.PixelShift;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.PixelShift;

/// <summary>
/// The pixel shift and tilt keys nudge the networked shift, which moves on at the player's speed while a key is held,
/// clamps, is blocked when helpless, stacks on lying down and clears on a reset or a step.
/// </summary>
public sealed class PixelShiftTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    public override async Task Setup()
    {
        await base.Setup();
        // The slowest speeds, so each of the tests' short taps is exactly one step.
        await SetSpeeds(WGPixelShiftSystem.MinShiftSpeed, WGPixelShiftSystem.MinTiltSpeed);
    }

    [Test]
    public async Task ShiftAndTiltUntilReset()
    {
        var start = Transform.GetWorldPosition(SPlayer);

        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveRight, 3);
        await Tap(EngineKeyFunctions.MoveUp);
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Up);

        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveLeft, 2);
        await Tap(EngineKeyFunctions.MoveRight);
        await Tap(EngineKeyFunctions.MoveUp);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Up);

        var shift = SEntMan.GetComponent<WGPixelShiftComponent>(SPlayer);
        Assert.That(shift.Offset, Is.EqualTo(new Vector2i(3, 1)));
        Assert.That(shift.Tilt, Is.EqualTo(WGPixelShiftSystem.TiltStep));
        Assert.That(Vector2.Distance(start, Transform.GetWorldPosition(SPlayer)), Is.LessThan(0.01f),
            "Shifting moved the mob.");

        var clientShift = CEntMan.GetComponent<WGPixelShiftComponent>(CPlayer);
        Assert.That(clientShift.Offset, Is.EqualTo(new Vector2i(3, 1)), "The client never got the shift.");
        await Client.WaitPost(() =>
        {
            var sprite = Settle();
            Assert.That(sprite.Offset.X, Is.EqualTo(3f / EyeManager.PixelsPerMeter).Within(0.001f));
            Assert.That(sprite.Rotation.Degrees, Is.EqualTo(WGPixelShiftSystem.TiltStep).Within(0.001));
            Assert.That(CEntMan.System<WGPixelShiftVisualsSystem>().UnshiftedOffset(CPlayer, sprite).X,
                Is.EqualTo(0f).Within(0.001f), "Status icons would follow the shift.");
            CEntMan.System<WGPixelShiftRestoreSystem>().FrameUpdate(0f);
            Assert.That(sprite.Offset.X, Is.EqualTo(0f).Within(0.001f), "The restore pass left the shift on.");
        });

        await Tap(WGPixelShiftKeys.WGPixelShiftReset);
        Assert.That(shift.Offset, Is.EqualTo(Vector2i.Zero));
        Assert.That(shift.Tilt, Is.Zero);
    }

    [Test]
    public async Task ReleasingOneKeyKeepsTheOther()
    {
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Down);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveLeft);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Up);
        await Tap(EngineKeyFunctions.MoveRight);
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Up);

        var shift = SEntMan.GetComponent<WGPixelShiftComponent>(SPlayer);
        Assert.That(shift.Tilt, Is.EqualTo(WGPixelShiftSystem.TiltStep), "The later key didn't take over.");
        Assert.That(shift.Offset, Is.EqualTo(new Vector2i(1, 0)), "Releasing tilt dropped the held shift key.");
    }

    [Test]
    public async Task WalkingClearsTheShift()
    {
        var start = Transform.GetWorldPosition(SPlayer);

        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveUp, 2);
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Up);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveLeft);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Up);
        Assert.That(Offset(), Is.EqualTo(new Vector2i(0, 2)));

        await SetKey(EngineKeyFunctions.MoveRight, BoundKeyState.Down);
        await RunTicks(20);
        await SetKey(EngineKeyFunctions.MoveRight, BoundKeyState.Up);
        await RunTicks(5);

        Assert.That(Transform.GetWorldPosition(SPlayer).X - start.X, Is.GreaterThan(0.3f));
        var shift = SEntMan.GetComponent<WGPixelShiftComponent>(SPlayer);
        Assert.That(shift.Offset, Is.EqualTo(Vector2i.Zero), "Walking left the shift on.");
        Assert.That(shift.Tilt, Is.Zero, "Walking left the tilt on.");
        Assert.That(CEntMan.GetComponent<WGPixelShiftComponent>(CPlayer).Offset, Is.EqualTo(Vector2i.Zero),
            "The client kept the shift.");
    }

    [TestCase(20, 100)]
    [TestCase(40, 30)]
    public async Task HeldKeysMoveAtTheChosenSpeed(int shiftSpeed, int tiltSpeed)
    {
        const float hold = 0.25f;
        await SetSpeeds(shiftSpeed, tiltSpeed);

        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Down);
        await SetKey(EngineKeyFunctions.MoveRight, BoundKeyState.Down);
        await RunSeconds(hold);
        await SetKey(EngineKeyFunctions.MoveRight, BoundKeyState.Up);
        await RunTicks(5);
        var shifted = Offset().X;
        await RunSeconds(0.5f);
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Up);

        // The press is one step and the key carries on at the speed straight away; the old 0.3 s wait gave one step.
        var expected = 1 + shiftSpeed * hold;
        Assert.That((float) shifted, Is.InRange(expected - Slack(shiftSpeed), expected + Slack(shiftSpeed)),
            "Holding the move key didn't shift at the chosen speed.");
        Assert.That(Offset().X, Is.EqualTo(shifted), "The shift kept going after the move key was let go.");

        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Down);
        await SetKey(EngineKeyFunctions.MoveLeft, BoundKeyState.Down);
        await RunSeconds(hold);
        await SetKey(EngineKeyFunctions.MoveLeft, BoundKeyState.Up);
        await RunTicks(5);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Up);

        var tilt = SEntMan.GetComponent<WGPixelShiftComponent>(SPlayer).Tilt;
        expected = WGPixelShiftSystem.TiltStep + tiltSpeed * hold;
        Assert.That((float) tilt, Is.InRange(expected - Slack(tiltSpeed), expected + Slack(tiltSpeed)),
            "Holding the move key didn't tilt at the chosen speed.");
    }

    [Test]
    public async Task SpeedsAreClamped()
    {
        await SetSpeeds(10000, 0);

        // Unclamped, a tenth of a second at 10000 px/s would run into the 16 px limit.
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Down);
        await SetKey(EngineKeyFunctions.MoveRight, BoundKeyState.Down);
        await RunSeconds(0.1f);
        await SetKey(EngineKeyFunctions.MoveRight, BoundKeyState.Up);
        await RunTicks(5);
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Up);
        Assert.That(Offset().X, Is.InRange(2, WGPixelShiftCVars.MaxOffset.DefaultValue - 1),
            "A huge shift speed wasn't held to the fastest.");

        // Unclamped, a speed of 0 would stop at the first step.
        const float hold = 1.5f;
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Down);
        await SetKey(EngineKeyFunctions.MoveLeft, BoundKeyState.Down);
        await RunSeconds(hold);
        await SetKey(EngineKeyFunctions.MoveLeft, BoundKeyState.Up);
        await RunTicks(5);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Up);
        var expected = WGPixelShiftSystem.TiltStep + WGPixelShiftSystem.MinTiltSpeed * hold;
        var slack = Slack(WGPixelShiftSystem.MinTiltSpeed);
        Assert.That((float) SEntMan.GetComponent<WGPixelShiftComponent>(SPlayer).Tilt,
            Is.InRange(expected - slack, expected + slack), "A speed of 0 wasn't raised to the slowest.");
    }

    [Test]
    public async Task TiltingWhileLyingDownAndStandingUp()
    {
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveLeft, 2);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Up);
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveRight);
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Up);

        var standing = SEntMan.System<StandingStateSystem>();
        await Server.WaitPost(() => Assert.That(standing.Down(SPlayer), Is.True));
        await RunTicks(30);
        await Client.WaitPost(() =>
        {
            var sprite = Settle();
            var lying = CEntMan.GetComponent<RotationVisualsComponent>(CPlayer).HorizontalRotation;
            Assert.That(sprite.Rotation.Degrees, Is.EqualTo(lying.Degrees + 2 * WGPixelShiftSystem.TiltStep).Within(0.001),
                "The tilt didn't stack on the lying-down rotation.");
            Assert.That(sprite.Offset.X, Is.EqualTo(1f / EyeManager.PixelsPerMeter).Within(0.001f),
                "Lying down dropped the shift.");
        });

        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveLeft);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Up);
        Assert.That(SEntMan.GetComponent<WGPixelShiftComponent>(SPlayer).Tilt, Is.EqualTo(3 * WGPixelShiftSystem.TiltStep),
            "Couldn't tilt while lying down.");
        await Client.WaitPost(() =>
        {
            var sprite = Settle();
            var lying = CEntMan.GetComponent<RotationVisualsComponent>(CPlayer).HorizontalRotation;
            Assert.That(sprite.Rotation.Degrees, Is.EqualTo(lying.Degrees + 3 * WGPixelShiftSystem.TiltStep).Within(0.001),
                "Tilting while lying down didn't show.");
        });

        await Server.WaitPost(() => Assert.That(standing.Stand(SPlayer, force: true), Is.True));
        await RunTicks(30);
        await Client.WaitPost(() =>
        {
            var sprite = Settle();
            Assert.That(sprite.Rotation.Degrees, Is.EqualTo(3 * WGPixelShiftSystem.TiltStep).Within(0.001),
                "Standing up didn't come back upright with the tilt.");
            CEntMan.System<WGPixelShiftRestoreSystem>().FrameUpdate(0f);
            Assert.That(sprite.Rotation.Degrees, Is.EqualTo(0).Within(0.001), "The restore pass left the tilt on.");
        });
    }

    [Test]
    public async Task ShiftIsClamped()
    {
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() =>
        {
            cfg.SetCVar(WGPixelShiftCVars.MaxOffset, 2);
            cfg.SetCVar(WGPixelShiftCVars.MaxTilt, 10);
        });
        await RunTicks(5);

        try
        {
            await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Down);
            await Tap(EngineKeyFunctions.MoveLeft, 4);
            await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Up);
            await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Down);
            await Tap(EngineKeyFunctions.MoveRight, 4);
            await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Up);

            var shift = SEntMan.GetComponent<WGPixelShiftComponent>(SPlayer);
            Assert.That(shift.Offset, Is.EqualTo(new Vector2i(-2, 0)));
            Assert.That(shift.Tilt, Is.EqualTo(-10));
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                cfg.SetCVar(WGPixelShiftCVars.MaxOffset, WGPixelShiftCVars.MaxOffset.DefaultValue);
                cfg.SetCVar(WGPixelShiftCVars.MaxTilt, WGPixelShiftCVars.MaxTilt.DefaultValue);
            });
        }
    }

    [Test]
    public async Task LoweringALimitClampsExistingShifts()
    {
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveRight, 3);
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Up);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveLeft, 2);
        await SetKey(WGPixelShiftKeys.WGPixelTilt, BoundKeyState.Up);

        var cfg = Server.ResolveDependency<IConfigurationManager>();
        try
        {
            await Server.WaitPost(() =>
            {
                cfg.SetCVar(WGPixelShiftCVars.MaxOffset, 1);
                cfg.SetCVar(WGPixelShiftCVars.MaxTilt, 0);
            });
            await RunTicks(5);

            var shift = SEntMan.GetComponent<WGPixelShiftComponent>(SPlayer);
            Assert.That(shift.Offset, Is.EqualTo(new Vector2i(1, 0)), "Lowering the limit left the shift past it.");
            Assert.That(shift.Tilt, Is.Zero, "Lowering the limit left the tilt past it.");
            Assert.That(CEntMan.GetComponent<WGPixelShiftComponent>(CPlayer).Offset, Is.EqualTo(new Vector2i(1, 0)),
                "The client never got the clamped shift.");
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                cfg.SetCVar(WGPixelShiftCVars.MaxOffset, WGPixelShiftCVars.MaxOffset.DefaultValue);
                cfg.SetCVar(WGPixelShiftCVars.MaxTilt, WGPixelShiftCVars.MaxTilt.DefaultValue);
            });
        }
    }

    [Test]
    public async Task RestrainedCantShift()
    {
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveRight);
        Assert.That(Offset(), Is.EqualTo(new Vector2i(1, 0)));

        await Server.WaitPost(() =>
        {
            var cuffs = SEntMan.SpawnAtPosition("Handcuffs", SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            Assert.That(SEntMan.System<CuffableSystem>().TryAddNewCuffs(SPlayer, SPlayer, cuffs), Is.True);
        });
        await RunTicks(5);
        await Tap(EngineKeyFunctions.MoveRight);
        Assert.That(Offset(), Is.EqualTo(new Vector2i(1, 0)), "A cuffed mob shifted its sprite.");
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Up);
    }

    [Test]
    public async Task DeadCantShift()
    {
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Down);
        await Tap(EngineKeyFunctions.MoveRight);
        Assert.That(Offset(), Is.EqualTo(new Vector2i(1, 0)));

        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(SPlayer, MobState.Dead));
        await RunTicks(5);
        await Tap(EngineKeyFunctions.MoveRight);
        Assert.That(Offset(), Is.EqualTo(new Vector2i(1, 0)), "A dead mob shifted its sprite.");
        await SetKey(WGPixelShiftKeys.WGPixelShift, BoundKeyState.Up);
    }

    /// <summary>Takes off whatever the client's frames applied, then lets the glide reach the set shift in one frame.</summary>
    private SpriteComponent Settle()
    {
        CEntMan.System<WGPixelShiftRestoreSystem>().FrameUpdate(0f);
        CEntMan.System<WGPixelShiftVisualsSystem>().FrameUpdate(1f);
        return CEntMan.GetComponent<SpriteComponent>(CPlayer);
    }

    /// <summary>Sets the client's speed options and lets them reach the server.</summary>
    private async Task SetSpeeds(int shift, int tilt)
    {
        await Client.WaitPost(() =>
        {
            var cfg = Client.ResolveDependency<IConfigurationManager>();
            cfg.SetCVar(WGPixelShiftCVars.ShiftSpeed, shift);
            cfg.SetCVar(WGPixelShiftCVars.TiltSpeed, tilt);
        });
        await RunTicks(10);
    }

    /// <summary>How far a hold can land from its expected travel: a tick either way at each end, plus rounding down.</summary>
    private float Slack(int speed)
    {
        return speed * 2f / STiming.TickRate + 1f;
    }

    private Vector2i Offset()
    {
        return SEntMan.TryGetComponent<WGPixelShiftComponent>(SPlayer, out var shift) ? shift.Offset : Vector2i.Zero;
    }

    private async Task Tap(BoundKeyFunction key, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            await SetKey(key, BoundKeyState.Down);
            await RunTicks(2);
            await SetKey(key, BoundKeyState.Up);
            await RunTicks(3);
        }
    }
}
