#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WG.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Wolfmed.CCVar;
using Content.Shared._WG.Wolfmed.Compat;
using Content.Shared._WG.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Wolfmed;

/// <summary>
/// Infection run tick by tick, each body on its own tick of the 5 s window, comes out as the whole-server batch did, and a
/// corpse waits <see cref="WolfmedInfectionSpreadCVars.SlowSeconds"/> between turns.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedInfectionSystem))]
public sealed class WolfmedInfectionTickSpreadTest : WolfmedGameTest
{
    /// <summary>The infection system's window, its private <c>TickSeconds</c>.</summary>
    private const float Window = 5f;

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionEnabled, true);
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionRate, 1f);
    }

    /// <summary>A minute of ticks moves an untreated cut as far as a minute handed over at once did, on every body.</summary>
    [Test]
    public async Task TicksMatchTheBatch()
    {
        await Pin();
        var map = await CreateTestMap();
        EntityUid first = default, second = default;
        float batched = 0, firstBefore = 0, secondBefore = 0;

        await Server.WaitAssertion(() =>
        {
            first = Cut(SEntMan.SpawnEntity("MobHuman", map.GridCoords));
            second = Cut(SEntMan.SpawnEntity("MobHuman", map.GridCoords));
            var start = Progress(first);
            SEntMan.System<WolfmedInfectionSystem>().Update(60f);
            batched = Progress(first) - start;
            Assert.That(batched, Is.GreaterThan(0f), "a minute in one call should move an untreated cut.");
            Assert.That(Progress(second), Is.EqualTo(Progress(first)).Within(1e-4f), "both cuts were handed the same minute.");
            firstBefore = Progress(first);
            secondBefore = Progress(second);
        });

        await RunSeconds(60);

        await Server.WaitAssertion(() =>
        {
            // Each body's turn lands somewhere in the window, so a minute of ticks is 55 to 65 s of infection.
            var firstTicked = Progress(first) - firstBefore;
            var secondTicked = Progress(second) - secondBefore;
            Assert.That(firstTicked, Is.EqualTo(batched).Within(batched * 0.15f),
                $"a minute of ticks moved the cut {firstTicked:0.000}, a minute at once {batched:0.000}.");
            Assert.That(secondTicked, Is.EqualTo(batched).Within(batched * 0.15f),
                $"the second body's cut moved {secondTicked:0.000} in a minute of ticks.");
        });
    }

    /// <summary>
    /// A limb off any body keeps its infection, but its turn comes once every
    /// <see cref="WolfmedInfectionSpreadCVars.SlowSeconds"/>, as a corpse's does.
    /// </summary>
    [Test]
    public async Task LooseLimbWaitsForItsTurn()
    {
        await Pin();
        await OverrideCVar(Side.Server, WolfmedInfectionSpreadCVars.SlowSeconds, 30f);
        var map = await CreateTestMap();
        EntityUid wound = default;
        float start = 0;

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            wound = Cut(body);
            // Off the body a cut is open to no air, so only something dirty in it is a reason to go bad.
            SEntMan.System<WolfmedInfectionSystem>().Contaminate(wound);
            var hand = SEntMan.GetComponent<WoundComponent>(wound).HoldingPart;
            Assert.That(SEntMan.System<WolfmedBodySystem>().TryDetachPart(hand), Is.True, "the hand would not come off.");
            Assert.That(SEntMan.GetComponent<BodyPartComponent>(hand).Body, Is.Null, "the hand is still on the body.");
            start = Progress(wound);
        });

        // The limb's first turn comes within 30 s of its filing; take it as the start of the wait.
        var moved = false;
        for (var i = 0; i < 45 && !moved; i++)
        {
            await RunSeconds(1);
            await Server.WaitPost(() => moved = Progress(wound) > start);
        }

        Assert.That(moved, Is.True, "a loose limb's infection never took a turn.");
        await Server.WaitPost(() => start = Progress(wound));

        await RunSeconds(20);
        await Server.WaitAssertion(() =>
            Assert.That(Progress(wound), Is.EqualTo(start), "a loose limb took a turn sooner than 30 s after its last."));

        // 30 s, plus up to one window for its slot to come round.
        await RunSeconds(20);
        await Server.WaitAssertion(() =>
            Assert.That(Progress(wound), Is.GreaterThan(start), "a loose limb's infection stopped after one turn."));
    }

    /// <summary>A minute with infection turned off isn't caught up when it is turned back on.</summary>
    [Test]
    public async Task TimeTurnedOffIsNotOwed()
    {
        await Pin();
        var map = await CreateTestMap();
        EntityUid wound = default;
        float perMinute = 0, before = 0;

        await Server.WaitAssertion(() =>
        {
            wound = Cut(SEntMan.SpawnEntity("MobHuman", map.GridCoords));
            var start = Progress(wound);
            SEntMan.System<WolfmedInfectionSystem>().Update(60f);
            perMinute = Progress(wound) - start;
            Assert.That(perMinute, Is.GreaterThan(0f), "a minute in one call should move an untreated cut.");
        });

        // A few turns first, so the body has a last turn on record when infection goes off.
        await RunSeconds(10);
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionEnabled, false);
        await Server.WaitPost(() => before = Progress(wound));
        await RunSeconds(60);
        await Server.WaitAssertion(() =>
            Assert.That(Progress(wound), Is.EqualTo(before), "the cut moved with infection off."));

        await OverrideCVar(Side.Server, WolfmedCVars.InfectionEnabled, true);
        await RunSeconds(10);
        await Server.WaitAssertion(() =>
        {
            // Ten seconds back on, plus at most a window for its turn; the minute off would be six times that.
            var moved = Progress(wound) - before;
            Assert.That(moved, Is.GreaterThan(0f), "infection did not resume once turned back on.");
            Assert.That(moved, Is.LessThan(perMinute * 0.5f),
                $"10 s back on moved the cut {moved:0.000}, a whole minute moves it {perMinute:0.000}.");
        });
    }

    /// <summary>
    /// A body first seen while infection is off has no turn to move on, so its first turn back on is paid only the time
    /// since infection came on, not a window reaching back into the time it was off.
    /// </summary>
    [Test]
    public async Task BodyFirstSeenWhileOffIsNotBackdated()
    {
        await Pin();
        var map = await CreateTestMap();
        var timing = Server.ResolveDependency<IGameTiming>();
        EntityUid wound = default;
        float window = 0, start = 0;
        TimeSpan onAt = default, movedAt = default;

        // How far one whole window moves a fresh cut.
        await Server.WaitAssertion(() =>
        {
            var reference = Cut(SEntMan.SpawnEntity("MobHuman", map.GridCoords));
            var before = Progress(reference);
            SEntMan.System<WolfmedInfectionSystem>().Update(Window);
            window = Progress(reference) - before;
            Assert.That(window, Is.GreaterThan(0f), "a window in one call should move a fresh cut.");
        });

        await OverrideCVar(Side.Server, WolfmedCVars.InfectionEnabled, false);
        await Server.WaitPost(() =>
        {
            wound = Cut(SEntMan.SpawnEntity("MobHuman", map.GridCoords));
            start = Progress(wound);
        });
        // Long enough to be filed while off, so it has no last turn when infection comes back.
        await RunSeconds(10);
        await Server.WaitAssertion(() => Assert.That(Progress(wound), Is.EqualTo(start), "the cut moved with infection off."));

        await OverrideCVar(Side.Server, WolfmedCVars.InfectionEnabled, true, false);
        await Server.WaitPost(() => onAt = timing.CurTime);
        var moved = false;
        for (var i = 0; i < 300 && !moved; i++)
        {
            await RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                moved = Progress(wound) > start;
                movedAt = timing.CurTime;
            });
        }

        Assert.That(moved, Is.True, "the cut never moved once infection was back on.");
        await Server.WaitAssertion(() =>
        {
            // Its first turn is paid at most the time since infection came on, plus a tick of slack.
            var on = (float) (movedAt - onAt).TotalSeconds + 0.1f;
            var allowed = window * MathF.Min(1f, on / Window) * 1.25f;
            var first = Progress(wound) - start;
            Assert.That(first, Is.LessThanOrEqualTo(allowed),
                $"{on:0.00} s after infection came on, the first turn moved the cut {first:0.0000}; a whole window moves it {window:0.0000}.");
        });
    }

    /// <summary>
    /// A loose limb's wait keeps the time it had before infection was turned off: back on, it takes its turn when the
    /// rest of the wait is up, not a whole wait later, and that turn isn't paid the time it was off.
    /// </summary>
    [Test]
    public async Task LooseLimbKeepsItsWaitAcrossTurningOff()
    {
        await Pin();
        await OverrideCVar(Side.Server, WolfmedInfectionSpreadCVars.SlowSeconds, 30f);
        var map = await CreateTestMap();
        EntityUid wound = default;
        float start = 0, turn = 0;

        await Server.WaitAssertion(() =>
        {
            wound = Cut(SEntMan.SpawnEntity("MobHuman", map.GridCoords));
            SEntMan.System<WolfmedInfectionSystem>().Contaminate(wound);
            var hand = SEntMan.GetComponent<WoundComponent>(wound).HoldingPart;
            Assert.That(SEntMan.System<WolfmedBodySystem>().TryDetachPart(hand), Is.True, "the hand would not come off.");
            start = Progress(wound);
        });

        // Its first turn, then a whole wait to the next, which is how far one turn moves it.
        Assert.That(await SecondsToTurn(45, start), Is.Not.Null, "a loose limb's infection never took a turn.");
        await Server.WaitPost(() => start = Progress(wound));
        Assert.That(await SecondsToTurn(45, start), Is.Not.Null, "a loose limb's infection stopped after one turn.");
        await Server.WaitPost(() =>
        {
            turn = Progress(wound) - start;
            start = Progress(wound);
        });

        // Twenty seconds of its wait, then a minute off.
        await RunSeconds(20);
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionEnabled, false);
        await RunSeconds(60);
        await Server.WaitAssertion(() =>
            Assert.That(Progress(wound), Is.EqualTo(start), "the limb took a turn before its wait was up or while off."));

        // About 10 s of the wait is left, plus up to a window for its slot; starting the wait afresh would take 25 s or more.
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionEnabled, true);
        var waited = await SecondsToTurn(30, start);
        Assert.That(waited, Is.Not.Null, "the limb never took a turn once infection was back on.");
        Assert.That(waited, Is.LessThanOrEqualTo(18), $"the limb took {waited} s to turn once back on; its wait started over.");
        await Server.WaitAssertion(() =>
        {
            var moved = Progress(wound) - start;
            Assert.That(moved, Is.LessThan(turn * 1.6f),
                $"the turn back on moved the cut {moved:0.000}, a whole wait moves it {turn:0.000}; it was paid the minute off.");
        });

        async Task<int?> SecondsToTurn(int limit, float from)
        {
            for (var i = 1; i <= limit; i++)
            {
                await RunSeconds(1);
                var moved = false;
                await Server.WaitPost(() => moved = Progress(wound) > from);
                if (moved)
                    return i;
            }

            return null;
        }
    }

    /// <summary>An untreated risk-1 cut on the left hand, made directly so the part it lands on is certain.</summary>
    private EntityUid Cut(EntityUid body)
    {
        var hand = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == BodyPartType.Hand && part.Component.Symmetry == BodyPartSymmetry.Left)
            .Id;
        var wound = SEntMan.System<WoundSystem>().CreateOrMergeWound(hand, "SlashWound", FixedPoint2.New(30));
        Assert.That(wound, Is.Not.Null, "the hand took no cut.");
        Assert.That(SEntMan.HasComponent<WolfmedInfectionComponent>(wound!.Value), Is.True, "the cut is not infectable.");
        return wound.Value;
    }

    private float Progress(EntityUid wound) => SEntMan.GetComponent<WolfmedInfectionComponent>(wound).Progress;
}
