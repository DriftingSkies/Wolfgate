#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WG.Wolfmed.Life;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Temperature.Systems;
using Content.Shared._WG.Wolfmed.CCVar;
using Content.Shared._WG.Wolfmed.Consciousness;
using Content.Shared._WG.Wolfmed.Life;
using Content.Shared.Atmos;
using Content.Shared.Inventory;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WG.Wolfmed.Scenarios;

/// <summary>
/// "Let's make space a bit more dangerous, e.g. going out without oxygen, or going out without space suit. Currently
/// takes a long time to die." Real time, nothing driven by hand, the shipped numbers pinned. A naked human in space
/// was untouched for 75 s and only then went down, to the cold; with no oxygen it stood for three minutes. Now hard
/// vacuum with no suit starves the brain on its own clock, internals only slow it, and no air is a minute and a
/// quarter. A suit with internals is safe.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedVacuumSystem))]
public sealed class WolfmedSpaceExposureTest : WolfmedGameTest
{
    private const string Suit = "ClothingOuterHardsuitEVA";
    private const string Helmet = "ClothingHeadHelmetEVA";

    private readonly List<string> _notes = new();

    private void Note(string line) => _notes.Add(line);

    [TearDown]
    public void WriteNotes()
    {
        foreach (var line in _notes)
            TestContext.Out.WriteLine(line);

        _notes.Clear();
    }

    private WolfmedVacuumSystem Vacuum => SEntMan.System<WolfmedVacuumSystem>();
    private WolfmedBodyTemperatureSystem BodyTemperature => SEntMan.System<WolfmedBodyTemperatureSystem>();

    /// <summary>The shipped values of everything these times are built from.</summary>
    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainAirlossSeconds, 90f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainWeakBreathSeconds, 180f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainVacuumSeconds, 55f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainVacuumBreathingFactor, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.AirlossFull, 30f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainArrestSeconds, 180f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainRefillFactor, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestOxygenation, 0.15f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureStart, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureOut, 0.45f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainColdFactor, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.CoreCoolingSeconds, 900f);
        await OverrideCVar(Side.Server, WolfmedCVars.HypothermiaDownOffset, 30f);
        await OverrideCVar(Side.Server, WolfmedCVars.HypothermiaOutOffset, 15f);
        await OverrideCVar(Side.Server, WolfmedCVars.HypothermiaArrestOffset, 2f);
    }

    /// <summary>A human on a map of space or of unbreathable air at station pressure, dressed as asked.</summary>
    private async Task<(WolfmedScenario S, EntityUid Body, TestMapData Map)> Spawn(bool space, bool suit, bool internals)
    {
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;

        await Server.WaitAssertion(() =>
        {
            if (space)
                SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, true, GasMixture.SpaceGas);
            else
                s.SetAir(map.MapUid, false);

            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var inventory = SEntMan.System<InventorySystem>();
            if (suit)
            {
                Equip(inventory, body, Suit, "outerClothing", map);
                Equip(inventory, body, Helmet, "head", map);
            }

            if (!internals)
                return;

            Equip(inventory, body, "ClothingMaskBreath", "mask", map);
            Equip(inventory, body, "EmergencyOxygenTankFilled", "belt", map);
            var tanks = SEntMan.System<InternalsSystem>();
            Assert.That(tanks.ToggleInternals(body, body, force: true), Is.True);
            Assert.That(tanks.AreInternalsWorking(body), Is.True, "the internals are not connected.");
        });

        return (s, body, map);
    }

    private void Equip(InventorySystem inventory, EntityUid body, string id, string slot, TestMapData map) =>
        Assert.That(inventory.TryEquip(body, SEntMan.SpawnEntity(id, map.GridCoords), slot, silent: true, force: true),
            Is.True, $"{id} would not go on.");

    private sealed class Timeline
    {
        public int? Downed, Unconscious, Arrest;
        public WolfmedCause Cause;
        public WolfmedCauseSource Source;
        public bool SuffocatingWhenDowned;
        public string ArrestCause = string.Empty;

        /// <summary>The analyzer's vitals block the second the body went down.</summary>
        public string Analyzer = string.Empty;
    }

    /// <summary>Runs second by second until the heart stops or the time is up, and records when each state came.</summary>
    private async Task<Timeline> Watch(WolfmedScenario s, EntityUid body, int seconds)
    {
        var timeline = new Timeline();
        for (var second = 1; second <= seconds && timeline.Arrest == null; second++)
        {
            await RunSeconds(1);
            var now = second;
            await Server.WaitPost(() =>
            {
                var vitals = s.Vitals(body);
                if (timeline.Downed == null && vitals.State != WolfmedConsciousness.Up)
                {
                    timeline.Downed = now;
                    timeline.Cause = vitals.Cause;
                    timeline.Source = vitals.CauseSource;
                    timeline.SuffocatingWhenDowned = s.Breathing.IsSuffocating(body);
                    timeline.Analyzer = s.Analyzer(body);
                }

                if (timeline.Unconscious == null && vitals.State == WolfmedConsciousness.Unconscious)
                    timeline.Unconscious = now;

                if (timeline.Arrest == null && SEntMan.TryGetComponent(body, out WolfmedCardiacArrestComponent? arrest))
                {
                    timeline.Arrest = now;
                    timeline.ArrestCause = arrest.Cause;
                }
            });
        }

        return timeline;
    }

    /// <summary>
    /// No suit, no air: told at once, Downed in about half a minute with the vacuum named, Unconscious seconds later
    /// and in arrest for want of oxygen inside two and a half minutes. Before, the first thing to happen was the cold,
    /// at 75 s.
    /// </summary>
    [Test]
    public async Task NakedInSpaceTest()
    {
        await Pin();
        var (s, body, _) = await Spawn(space: true, suit: false, internals: false);
        await RunSeconds(3);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Vacuum.IsExposed(body), Is.True, "a naked human in space is not exposed.");
                Assert.That(s.Life.GetActiveRoutes(body) & WolfmedRoutes.Vacuum, Is.EqualTo(WolfmedRoutes.Vacuum));
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Up), "space dropped the body within seconds.");
            });
        });

        var timeline = await Watch(s, body, 177);
        Note($"NakedInSpaceTest: Downed at {timeline.Downed + 3} s ({timeline.Cause}/{timeline.Source}), Unconscious at " +
             $"{timeline.Unconscious + 3} s, arrest at {timeline.Arrest + 3} s ({timeline.ArrestCause}).");
        Note($"NakedInSpaceTest, the analyzer when it went down: {timeline.Analyzer}");
        Assert.Multiple(() =>
        {
            Assert.That(timeline.Downed + 3, Is.InRange(20, 40), "Downed in space.");
            Assert.That(timeline.Cause, Is.EqualTo(WolfmedCause.Hypoxia));
            Assert.That(timeline.Source, Is.EqualTo(WolfmedCauseSource.Vacuum));
            Assert.That(timeline.Unconscious + 3, Is.InRange(25, 50), "Unconscious in space.");
            Assert.That(timeline.Arrest + 3, Is.InRange(50, 150), "arrest in space.");
            Assert.That(timeline.ArrestCause, Is.EqualTo("oxygen"));
            // What the medic reads: the cause named, and what to do about it.
            Assert.That(timeline.Analyzer, Does.Contain("vacuum exposure"), timeline.Analyzer);
            Assert.That(timeline.Analyzer, Does.Contain("Do first: get them into pressure or a suit, now"), timeline.Analyzer);
        });
    }

    /// <summary>
    /// A mask and a tank with no suit: still breathing, and the vacuum still takes the body down, later than with
    /// nothing. Before, internals were a full answer for 75 s.
    /// </summary>
    [Test]
    public async Task InternalsWithoutASuitTest()
    {
        await Pin();
        var (s, body, _) = await Spawn(space: true, suit: false, internals: true);
        var timeline = await Watch(s, body, 100);
        Note($"InternalsWithoutASuitTest: Downed at {timeline.Downed} s ({timeline.Cause}/{timeline.Source}), " +
             $"Unconscious at {timeline.Unconscious} s.");
        Assert.Multiple(() =>
        {
            Assert.That(timeline.Downed, Is.InRange(30, 55), "Downed in space on internals.");
            Assert.That(timeline.Cause, Is.EqualTo(WolfmedCause.Hypoxia));
            Assert.That(timeline.Source, Is.EqualTo(WolfmedCauseSource.Vacuum));
            Assert.That(timeline.SuffocatingWhenDowned, Is.False, "the internals were not feeding the lungs.");
            Assert.That(timeline.Unconscious, Is.InRange(40, 75), "Unconscious in space on internals.");
        });
    }

    /// <summary>
    /// No air, with pressure on the body (a suit in space, or a room of nitrogen): Downed in a minute and a quarter,
    /// in arrest inside two minutes, and never exposed. Was 185 and 256 s. The stopped heart then keeps its own clock:
    /// the faster no-air clock is not counted on top of it.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task NoAirTest(bool suitInSpace)
    {
        await Pin();
        var (s, body, _) = await Spawn(space: suitInSpace, suit: suitInSpace, internals: false);
        var timeline = await Watch(s, body, 160);
        Note($"NoAirTest({suitInSpace}): Downed at {timeline.Downed} s ({timeline.Cause}/{timeline.Source}), Unconscious " +
             $"at {timeline.Unconscious} s, arrest at {timeline.Arrest} s ({timeline.ArrestCause}).");

        await Server.WaitAssertion(() =>
        {
            var brain = s.Life.GetBrain(body)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(Vacuum.IsExposed(body), Is.False, "a body under pressure counts as exposed.");
                Assert.That(timeline.Downed, Is.InRange(60, 95), "Downed with no air.");
                Assert.That(timeline.Cause, Is.EqualTo(WolfmedCause.Hypoxia));
                Assert.That(timeline.Source, Is.EqualTo(WolfmedCauseSource.Airway));
                Assert.That(timeline.Unconscious, Is.InRange(65, 105), "Unconscious with no air.");
                Assert.That(timeline.Arrest, Is.InRange(90, 140), "arrest with no air.");
                Assert.That(timeline.ArrestCause, Is.EqualTo("oxygen"));
                Assert.That(s.Life.DrainRate(body, brain), Is.EqualTo(1f / 180f).Within(0.0001f),
                    "the arrested brain does not drain on the arrest clock.");
            });
        });
    }

    /// <summary>A suit and internals: two minutes in space cost nothing.</summary>
    [Test]
    public async Task SuitAndInternalsAreSafeTest()
    {
        await Pin();
        var (s, body, _) = await Spawn(space: true, suit: true, internals: true);
        await RunSeconds(120);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Up));
                Assert.That(Vacuum.IsExposed(body), Is.False);
                Assert.That(s.Life.GetOxygenation(body), Is.GreaterThan(0.99f));
                Assert.That(s.Life.GetActiveRoutes(body), Is.EqualTo(WolfmedRoutes.None));
            });
        });
    }

    /// <summary>Back in air the exposure ends within a second or two, the drain stops and the brain refills.</summary>
    [Test]
    public async Task BackInPressureTheDrainStopsTest()
    {
        await Pin();
        var (s, body, map) = await Spawn(space: true, suit: false, internals: false);
        await RunSeconds(20);

        var low = 0f;
        await Server.WaitAssertion(() =>
        {
            low = s.Life.GetOxygenation(body);
            Assert.Multiple(() =>
            {
                Assert.That(Vacuum.IsExposed(body), Is.True);
                Assert.That(low, Is.InRange(0.55f, 0.85f), "twenty seconds of vacuum.");
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Up));
            });

            s.SetAir(map.MapUid, true);
        });
        await RunSeconds(4);

        var settled = 0f;
        await Server.WaitAssertion(() =>
        {
            settled = s.Life.GetOxygenation(body);
            Assert.Multiple(() =>
            {
                Assert.That(Vacuum.IsExposed(body), Is.False, "back in air and still exposed.");
                Assert.That(s.Life.GetActiveRoutes(body) & WolfmedRoutes.Vacuum, Is.EqualTo(WolfmedRoutes.None));
            });
        });
        await RunSeconds(30);

        await Server.WaitAssertion(() =>
        {
            Note($"BackInPressureTheDrainStopsTest: {low:0.00} after 20 s of vacuum, {settled:0.00} four seconds into air, " +
                 $"{s.Life.GetOxygenation(body):0.00} thirty seconds later.");
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.GetOxygenation(body), Is.GreaterThan(settled), "the brain did not refill in air.");
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Up));
            });
        });
    }

    /// <summary>
    /// A body that loses its BarotraumaComponent while exposed (zombification takes it off, and so does a trader)
    /// stops being read for exposure; the mark is cleared on the next reading instead of draining the brain for good.
    /// </summary>
    [Test]
    public async Task LosingBarotraumaEndsTheExposureTest()
    {
        await Pin();
        var (s, body, _) = await Spawn(space: true, suit: false, internals: false);
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Vacuum.IsExposed(body), Is.True, "a naked human in space is not exposed.");
            SEntMan.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(body);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Vacuum.IsExposed(body), Is.False, "the body is still marked exposed.");
                Assert.That(s.Life.GetActiveRoutes(body) & WolfmedRoutes.Vacuum, Is.EqualTo(WolfmedRoutes.None));
            });
        });
    }

    /// <summary>
    /// The brain's cold protection reads the core. A cold skin over a warm core protects nothing (in space the skin is
    /// under 20 C within seconds, which made the brain safe before it was short of anything); a cold core does.
    /// </summary>
    [Test]
    public async Task ColdProtectionReadsTheCoreTest()
    {
        await Pin();
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var brain = s.Life.GetBrain(body)!.Value;
            Assert.That(s.Life.StartArrest(body, "oxygen"), Is.True);
            var arrest = 1f / 180f;

            SEntMan.System<TemperatureSystem>().ForceChangeTemperature(body, 250f);
            BodyTemperature.SetCore(body, 310f);
            // SetCore re-reads the surface, and a colder surface pulls the core a hair under what was set.
            Assert.That(s.Life.DrainRate(body, brain), Is.EqualTo(arrest).Within(0.0001f),
                "a cold skin over a warm core slowed the brain's drain.");

            BodyTemperature.SetCore(body, 300f);
            Assert.That(s.Life.DrainRate(body, brain), Is.EqualTo(arrest * 0.5f).Within(0.0001f), "core under 30 C.");

            BodyTemperature.SetCore(body, 280f);
            Assert.That(s.Life.DrainRate(body, brain), Is.EqualTo(arrest * 0.1f).Within(0.0001f), "core under 20 C.");
        });
    }
}
