#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Remade.Dread;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Dread;
using Content.Server._WF.Remade.Gifts;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Power.Components;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Dread;
using Content.Shared.CCVar;
using Content.Shared.Doors.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>Remembers each time the biohazard alarm starts to play.</summary>
public sealed class WFDreadAlarmObserver : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public readonly Dictionary<EntityUid, TimeSpan> Plays = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out var uid, out var audio))
        {
            if (audio.FileName == DreadTest.AlarmPath && !Plays.ContainsKey(uid))
                Plays[uid] = _timing.CurTime;
        }
    }
}

/// <summary>The three-play alarm, and the whispers, glimpses and warped screen the stone puts in every head.</summary>
[TestFixture]
public sealed class DreadTest
{
    public const string AlarmPath = "/Audio/_WF/Remade/monolith/alarm.ogg";

    private static EntityCoordinates At(TestMapData map, float x, float y) => WFRemadeDirectorTest.At(map, x, y);

    /// <summary>A Scion with its Director removed, so a test sees only the dread.</summary>
    private static Entity<WFMonolithComponent> SpawnScion(IEntityManager entMan, EntityCoordinates at, float resonance)
    {
        var uid = entMan.SpawnEntity("WFMonolithScion", at);
        entMan.GetComponent<WFRemadeDirectorComponent>(uid).BuildUp = false;
        entMan.RemoveComponent<WFRemadeDirectorComponent>(uid);
        var comp = entMan.GetComponent<WFMonolithComponent>(uid);
        comp.Resonance = resonance;
        return (uid, comp);
    }

    private static void AssertThreeSeparatePlays(WFDreadAlarmObserver observer, TimeSpan clip, string what)
    {
        var starts = observer.Plays.Values.OrderBy(time => time).ToList();
        Assert.That(starts, Has.Count.EqualTo(3), $"{what} played the alarm {starts.Count} times, not 3.");
        for (var i = 1; i < starts.Count; i++)
        {
            var gap = (starts[i] - starts[i - 1]).TotalSeconds;
            Assert.That(gap, Is.GreaterThanOrEqualTo(clip.TotalSeconds - 0.05), $"{what}: play {i + 1} began {gap:F2} s after the last, inside its {clip.TotalSeconds:F2} s.");
            Assert.That(gap, Is.LessThan(clip.TotalSeconds + 0.5), $"{what}: play {i + 1} came {gap:F2} s after the last, not back to back.");
        }
    }

    [Test]
    public async Task TheCrescendoAlarmPlaysThreeTimesBackToBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var audio = entMan.System<SharedAudioSystem>();
        var observer = entMan.System<WFDreadAlarmObserver>();
        TimeSpan clip = default;

        await server.WaitPost(() =>
        {
            observer.Plays.Clear();
            clip = audio.GetAudioLength(audio.ResolveSound(new SoundPathSpecifier(AlarmPath)));
            WFRemadeDirectorTest.LayDeck(pair, map);
            var scion = SpawnScion(entMan, At(map, 0, 0), 99.95f);
            entMan.System<WFMonolithSystem>().AddResonance(scion, 1f);
            Assert.That(scion.Comp!.Stage, Is.EqualTo(WFMonolithStage.Crescendo));
        });

        await pair.RunSeconds((float) clip.TotalSeconds * 3f + 3f);

        await server.WaitAssertion(() => AssertThreeSeparatePlays(observer, clip, "the Crescendo"));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheLockdownAlarmPlaysThreeTimesBackToBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var audio = entMan.System<SharedAudioSystem>();
        var observer = entMan.System<WFDreadAlarmObserver>();
        EntityUid scion = default, crewman = default;
        TimeSpan clip = default;

        await server.WaitPost(() =>
        {
            observer.Plays.Clear();
            clip = audio.GetAudioLength(audio.ResolveSound(new SoundPathSpecifier(AlarmPath)));
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 0, 0));
            scion = SpawnScion(entMan, At(map, -20, 20), 5f);
            var door = entMan.SpawnEntity("Airlock", At(map, 4, 0));
            entMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
        });
        await pair.RunTicksSync(30);

        await server.WaitPost(() =>
        {
            var director = entMan.AddComponent<WFRemadeDirectorComponent>(scion);
            var bolted = entMan.System<WFRemadeDirectorSystem>().Lockdown((scion, director), crewman, 3f);
            Assert.That(bolted, Is.GreaterThan(0), "the lockdown bolted nothing, so it sounded no alarm.");
        });

        await pair.RunSeconds((float) clip.TotalSeconds * 3f + 3f);

        await server.WaitAssertion(() =>
        {
            AssertThreeSeparatePlays(observer, clip, "the lockdown");
            entMan.RemoveComponent<WFRemadeDirectorComponent>(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheAlarmStopsWhenItsEntityIsDeleted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var alarm = entMan.System<WFDreadAlarmSystem>();
        var observer = entMan.System<WFDreadAlarmObserver>();
        EntityUid host = default;

        await server.WaitPost(() =>
        {
            observer.Plays.Clear();
            host = entMan.SpawnEntity(null, map.GridCoords);
            alarm.Start(host, new SoundPathSpecifier(AlarmPath));
        });
        await pair.RunSeconds(1.5f);
        await server.WaitPost(() => entMan.DeleteEntity(host));
        await pair.RunSeconds(10f);

        await server.WaitAssertion(() =>
        {
            Assert.That(observer.Plays, Has.Count.EqualTo(1), "the alarm kept playing for an entity that was gone.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryWhisperAndScrawlHasItsWords()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var loc = server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var keys = WFDreadSystem.AllWhisperKeys().Concat(Enumerable.Range(1, 30).Select(n => $"wf-remade-scrawl-{n}")).ToList();
            Assert.That(keys, Has.Count.GreaterThanOrEqualTo(60));
            foreach (var key in keys)
            {
                Assert.That(loc.TryGetString(key, out var text, ("name", "Rook")), Is.True, $"{key} has no words.");
                Assert.That(text, Does.Not.Contain("Marker").IgnoreCase, $"{key} says Marker.");
                Assert.That(text, Does.Not.Contain("Make us whole").IgnoreCase, $"{key} says Make us whole.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Puts the single player on a map of its own, far from or alongside a Scion, and counts what the stone does to them.</summary>
    private static async Task<(int Whispers, int Glimpses, int Pulses)> CountOver(
        TestPair pair, float resonance, bool sameMap, float away, float seconds, bool sector = true)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        TestMapData? other = sameMap ? null : await pair.CreateTestMap();
        var map = await pair.CreateTestMap();
        EntityUid crewman = default, scion = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WFDreadCVars.Sector, sector);
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), resonance);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, sameMap ? new EntityCoordinates(map.MapUid, new System.Numerics.Vector2(away, 0f)) : new EntityCoordinates(other!.MapUid, System.Numerics.Vector2.Zero));
        });

        await pair.RunSeconds(seconds);

        var counts = (0, 0, 0);
        await server.WaitPost(() =>
        {
            var subject = entMan.GetComponent<WFDreadSubjectComponent>(crewman);
            counts = (subject.Whispers, subject.Glimpses, subject.Pulses);
            entMan.DeleteEntity(scion);
        });
        return counts;
    }

    [Test]
    public async Task TheCrescendoReachesAFarPlayerOnTheSameMapAndAnotherMap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });

        var same = await CountOver(pair, 100f, sameMap: true, away: 600f, seconds: 120f);
        Assert.That(same.Whispers + same.Glimpses + same.Pulses, Is.GreaterThan(0), "a far player on the Scion's map felt nothing at the Crescendo.");

        var elsewhere = await CountOver(pair, 100f, sameMap: false, away: 0f, seconds: 120f);
        Assert.That(elsewhere.Whispers + elsewhere.Glimpses + elsewhere.Pulses, Is.GreaterThan(0), "a player on another map felt nothing at the Crescendo.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyRaptureAndTheCrescendoReachAFarPlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });

        var harvest = await CountOver(pair, 70f, sameMap: true, away: 600f, seconds: 120f);
        Assert.That(harvest, Is.EqualTo((0, 0, 0)), "Harvest reached a player 600 m off.");

        var off = await CountOver(pair, 100f, sameMap: true, away: 600f, seconds: 120f, sector: false);
        Assert.That(off, Is.EqualTo((0, 0, 0)), "the sector setting was off and the Crescendo still reached a far player.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheCrescendoIsFarMoreThanTheHymnBesideTheStone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });

        var quiet = await CountOver(pair, 5f, sameMap: true, away: 3f, seconds: 120f);
        Assert.That(quiet, Is.EqualTo((0, 0, 0)), "a quiet stone troubled a player.");

        // Six minutes each: in two the Crescendo's ~10 whispers fell to 5 or fewer about one run in twenty.
        var hymn = await CountOver(pair, 20f, sameMap: true, away: 3f, seconds: 360f);
        var crescendo = await CountOver(pair, 100f, sameMap: true, away: 3f, seconds: 360f);
        var hymnTotal = hymn.Whispers + hymn.Glimpses + hymn.Pulses;
        var crescendoTotal = crescendo.Whispers + crescendo.Glimpses + crescendo.Pulses;
        Assert.That(crescendoTotal, Is.GreaterThan(hymnTotal * 2 + 8), $"the Hymn gave {hymnTotal} and the Crescendo only {crescendoTotal}.");
        Assert.That(crescendo.Glimpses, Is.GreaterThan(2), "the Crescendo showed almost no glimpses.");
        Assert.That(crescendo.Whispers, Is.GreaterThan(12));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AGlimpseAppearsOnTheClientForAMomentAndReducedMotionTurnsItOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid crewman = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 0, 0));
        });
        await pair.RunSeconds(1f);

        var glimpses = client.System<WFDreadGlimpseSystem>();
        var vision = client.System<WFDreadVisionSystem>();
        var before = 0;
        await client.WaitPost(() => before = glimpses.Shown);

        await server.WaitPost(() =>
        {
            var dread = entMan.System<WFDreadSystem>();
            dread.Glimpse(crewman, WFMonolithStage.Crescendo);
            dread.Pulse(crewman, 0.8f);
        });
        await pair.RunTicksSync(6);

        await client.WaitAssertion(() =>
        {
            Assert.That(glimpses.Shown, Is.EqualTo(before + 1), "the client never showed the glimpse.");
            Assert.That(vision.Spike, Is.GreaterThan(0f), "the pulse never reached the client's screen.");
            Assert.That(vision.Overlay.Wobble + vision.Overlay.Split + vision.Overlay.Vignette + vision.Overlay.Drain, Is.GreaterThan(0f),
                "the screen warp is not drawing a pulse.");
        });

        await pair.RunSeconds(2f);
        await client.WaitAssertion(() =>
        {
            Assert.That(glimpses.Showing, Is.Zero, "the glimpse stayed on screen.");
            Assert.That(vision.Spike, Is.Zero.Within(0.01f), "the pulse never faded.");
        });

        // Reduced motion: no flashes and no motion in the warp.
        await client.WaitPost(() => client.CfgMan.SetCVar(CCVars.ReducedMotion, true));
        await server.WaitPost(() =>
        {
            var dread = entMan.System<WFDreadSystem>();
            dread.Glimpse(crewman, WFMonolithStage.Crescendo);
            dread.Pulse(crewman, 0.8f);
        });
        await pair.RunTicksSync(6);
        await client.WaitAssertion(() =>
        {
            Assert.That(glimpses.Shown, Is.EqualTo(before + 1), "a glimpse showed with reduced motion on.");
            Assert.That(vision.Overlay.Wobble, Is.Zero, "the screen wobbled with reduced motion on.");
            Assert.That(vision.Overlay.Split, Is.Zero);
            Assert.That(vision.Overlay.Drain, Is.Zero);
            client.CfgMan.SetCVar(CCVars.ReducedMotion, false);
        });
        await pair.CleanReturnAsync();
    }

    private const string ShardProto = "WFMonolithShard";

    /// <summary>A Scion on a map of its own, one of its shards put by (or in the pocket of) the single player, and what the player felt.</summary>
    private static async Task<(int Whispers, int Murmurs, int Glimpses, int Pulses, float Bed)> CountShard(
        TestPair pair, float resonance, float shardAway, bool carried = false, bool sanctuary = false, float seconds = 120f)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var other = await pair.CreateTestMap();
        var map = await pair.CreateTestMap();
        EntityUid crewman = default, scion = default, shard = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WFDreadCVars.Sector, false);
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(other, 0, 0), resonance);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 0, 0));
            shard = entMan.SpawnEntity(ShardProto, At(map, shardAway, 0));
            entMan.GetComponent<WFMonolithShardComponent>(shard).Parent = scion;
            if (sanctuary)
                entMan.AddComponent<WFRemadeSanctuaryComponent>(map.Grid.Owner);

            if (carried)
            {
                var containers = entMan.System<SharedContainerSystem>();
                var pocket = containers.EnsureContainer<ContainerSlot>(crewman, "wf_test_pocket");
                Assert.That(containers.Insert(shard, pocket), Is.True, "the shard wouldn't go in a pocket.");
            }
        });

        await pair.RunSeconds(seconds);

        var counts = (0, 0, 0, 0, 0f);
        await server.WaitPost(() =>
        {
            var subject = entMan.GetComponent<WFDreadSubjectComponent>(crewman);
            counts = (subject.Whispers, subject.Murmurs, subject.Glimpses, subject.Pulses, subject.Bed);
            if (sanctuary)
                entMan.RemoveComponent<WFRemadeSanctuaryComponent>(map.Grid.Owner);

            entMan.DeleteEntity(scion);
        });
        return counts;
    }

    private static int Total((int Whispers, int Murmurs, int Glimpses, int Pulses, float Bed) counts) =>
        counts.Whispers + counts.Murmurs + counts.Glimpses + counts.Pulses;

    [Test]
    public async Task AShardTroublesThoseNearItAndNobodyFarFromItAndStaysQuietNearAStation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });

        var far = await CountShard(pair, 70f, 15f);
        Assert.That(far, Is.EqualTo((0, 0, 0, 0, 0f)), "a shard 15 m off troubled the player.");

        // About 2.6 events a minute at 1 m: two minutes drew none once in ~180 runs, four make that ~1 in 30,000.
        var near = await CountShard(pair, 70f, 1f, seconds: 240f);
        Assert.That(Total(near), Is.GreaterThan(0), "a player 1 m from a Harvest shard felt nothing in four minutes.");
        Assert.That(near.Bed, Is.GreaterThan(0f), "the whisper bed never started beside a Harvest shard.");

        var carried = await CountShard(pair, 70f, 0f, carried: true);
        Assert.That(Total(carried), Is.GreaterThan(0), "a player carrying a Harvest shard felt nothing.");
        Assert.That(carried.Bed, Is.GreaterThan(near.Bed), "carrying a shard was no worse than standing beside it.");

        var quiet = await CountShard(pair, 5f, 0f, carried: true);
        Assert.That(quiet, Is.EqualTo((0, 0, 0, 0, 0f)), "a quiet Scion's shard troubled its carrier.");

        var station = await CountShard(pair, 70f, 0f, carried: true, sanctuary: true);
        Assert.That(station, Is.EqualTo((0, 0, 0, 0, 0f)), "a shard troubled someone on a sanctuary grid.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheCrescendoWhispersAlmostWithoutStopping()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid crewman = default, scion = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 100f);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 3, 0));
        });
        // Three minutes: about 16 a minute come on average, and one minute fell to 8 or fewer about one run in a hundred.
        await pair.RunSeconds(180f);

        await server.WaitAssertion(() =>
        {
            var subject = entMan.GetComponent<WFDreadSubjectComponent>(crewman);
            Assert.That(subject.Whispers + subject.Murmurs, Is.GreaterThan(24), $"{subject.Whispers} whispers and {subject.Murmurs} murmurs in three minutes at the Crescendo.");
            Assert.That(subject.Murmurs, Is.GreaterThan(subject.Whispers / 2), "murmurs don't add to the whispers.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheBedPlaysFromHarvestOnTheClientAndCannotBeEscapedAtTheCrescendo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var other = await pair.CreateTestMap();
        var map = await pair.CreateTestMap();
        EntityUid crewman = default, scion = default;
        var bed = client.System<WFDreadBedSystem>();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WFDreadCVars.Sector, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(other, 0, 0), 40f);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 0, 0));
        });
        await pair.RunSeconds(8f);

        await client.WaitAssertion(() =>
        {
            Assert.That(bed.Bed, Is.Zero, "the bed plays for a Growth stone.");
            Assert.That(bed.Playing, Is.Zero);
        });

        // The Crescendo, on another map entirely.
        await server.WaitPost(() => entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 100f);
        await pair.RunSeconds(14f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFDreadSubjectComponent>(crewman).Bed, Is.GreaterThanOrEqualTo(0.5f),
                "a player on another map doesn't get the Crescendo bed.");
        });
        await client.WaitAssertion(() =>
        {
            Assert.That(bed.Bed, Is.GreaterThan(0.3f), "the client never raised the bed.");
            Assert.That(bed.Playing, Is.GreaterThanOrEqualTo(2), "the client isn't playing the bed's layers.");
        });

        // Leaving the stone's reach lets it fade out.
        await server.WaitPost(() => entMan.DeleteEntity(scion));
        await pair.RunSeconds(25f);
        await client.WaitAssertion(() =>
        {
            Assert.That(bed.Bed, Is.LessThan(0.05f), "the bed never faded.");
            Assert.That(bed.Playing, Is.Zero, "the bed's streams stayed open.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Lights on the Scion's ship and the unbroken lights on another ship, counted after a span of time.</summary>
    private static async Task<(int Total, int Dark, int ElsewhereDark)> Darken(TestPair pair, float resonance, float seconds)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var ours = new List<EntityUid>();
        var theirs = new List<EntityUid>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            for (var x = -15; x <= 15; x += 3)
            {
                ours.Add(entMan.SpawnEntity("PoweredSmallLight", At(map, x, -9)));
                ours.Add(entMan.SpawnEntity("PoweredSmallLight", At(map, x, 9)));
            }

            // A second ship, nowhere near docked.
            var maps = entMan.System<SharedMapSystem>();
            var other = maps.CreateGridEntity(map.MapId);
            entMan.System<SharedTransformSystem>().SetLocalPosition(other.Owner, new Vector2(300f, 0f));
            var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            for (var x = 0; x < 4; x++)
            {
                maps.SetTile(other.Owner, other.Comp, new Vector2i(x, 0), plating);
            }

            for (var x = 0; x < 3; x++)
            {
                theirs.Add(entMan.SpawnEntity("PoweredSmallLight", new EntityCoordinates(other.Owner, new Vector2(x + 0.5f, 0.5f))));
            }

            scion = SpawnScion(entMan, At(map, 0, 0), resonance);
        });

        await pair.RunSeconds(seconds);

        var result = (0, 0, 0);
        await server.WaitPost(() =>
        {
            var lights = entMan.System<SharedPoweredLightSystem>();
            bool Dark(EntityUid light)
            {
                return lights.GetBulb(light) is not { } bulb || entMan.GetComponent<LightBulbComponent>(bulb).State != LightBulbState.Normal;
            }

            result = (ours.Count, ours.Count(Dark), theirs.Count(Dark));
            entMan.DeleteEntity(scion);
        });
        return result;
    }

    [Test]
    public async Task TheStoneBreaksLightsOnItsShipFromHarvestAndNotWhileQuiet()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });

        var quiet = await Darken(pair, 5f, 40f);
        Assert.That(quiet.Dark, Is.Zero, "a quiet stone broke lights.");

        var growth = await Darken(pair, 40f, 40f);
        Assert.That(growth.Dark, Is.Zero, "a Growth stone broke lights.");

        var harvest = await Darken(pair, 60f, 40f);
        var rapture = await Darken(pair, 85f, 40f);
        var crescendo = await Darken(pair, 100f, 40f);

        Assert.That(harvest.Dark, Is.InRange(1, (int) Math.Ceiling(harvest.Total * WFDarknessSystem.DarkShare(WFMonolithStage.Harvest))),
            $"{harvest.Dark} of {harvest.Total} lights dark at Harvest: it should be a few.");
        Assert.That(rapture.Dark, Is.GreaterThan(harvest.Dark), $"Rapture broke {rapture.Dark} against Harvest's {harvest.Dark}.");
        Assert.That(rapture.Dark, Is.LessThanOrEqualTo((int) Math.Ceiling(rapture.Total * WFDarknessSystem.DarkShare(WFMonolithStage.Rapture))));
        Assert.That(crescendo.Dark, Is.GreaterThan(rapture.Dark), $"the Crescendo broke {crescendo.Dark} against Rapture's {rapture.Dark}.");
        Assert.That(crescendo.Dark, Is.GreaterThanOrEqualTo((int) (crescendo.Total * 0.7f)), $"only {crescendo.Dark} of {crescendo.Total} lights dark at the Crescendo.");
        Assert.That(harvest.ElsewhereDark + rapture.ElsewhereDark + crescendo.ElsewhereDark, Is.Zero, "the stone broke lights on a ship it isn't docked to.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheStoneBreaksTheLightsNearItFirstAndCrewCanReplaceThem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var lights = new List<EntityUid>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            for (var x = -20; x <= 20; x += 2)
            {
                lights.Add(entMan.SpawnEntity("PoweredSmallLight", At(map, x, 12)));
            }

            scion = SpawnScion(entMan, At(map, 0, 0), 100f);
        });
        await pair.RunSeconds(4.5f);

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<SharedPoweredLightSystem>();
            bool Dark(EntityUid light) => system.GetBulb(light) is not { } bulb || entMan.GetComponent<LightBulbComponent>(bulb).State != LightBulbState.Normal;

            var transform = entMan.System<SharedTransformSystem>();
            var origin = transform.GetWorldPosition(scion);
            var darkOnes = lights.Where(Dark).ToList();
            Assert.That(darkOnes.Count, Is.GreaterThanOrEqualTo(3), "next to nothing broke in four seconds at the Crescendo.");
            var nearest = lights.OrderBy(l => (transform.GetWorldPosition(l) - origin).Length()).Take(lights.Count / 2).ToHashSet();
            Assert.That(darkOnes.Count(nearest.Contains), Is.GreaterThan(darkOnes.Count / 2), "the lights far from the stone went first.");
            Assert.That(darkOnes.Count, Is.LessThanOrEqualTo(12), $"{darkOnes.Count} lights broke in four seconds: the rate has no cap.");

            // Crew put new bulbs in.
            foreach (var light in darkOnes)
            {
                var bulb = system.GetBulb(light);
                if (bulb != null)
                    entMan.GetComponent<LightBulbComponent>(bulb.Value).State = LightBulbState.Normal;
            }
        });
        await pair.RunSeconds(20f);

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<SharedPoweredLightSystem>();
            var again = lights.Count(light => system.GetBulb(light) is not { } bulb || entMan.GetComponent<LightBulbComponent>(bulb).State != LightBulbState.Normal);
            Assert.That(again, Is.GreaterThan(0), "the stone left replaced bulbs alone.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The files a sound can play: a path, or everything in a collection.</summary>
    private static IEnumerable<string> FilesOf(IPrototypeManager protos, SoundSpecifier sound)
    {
        switch (sound)
        {
            case SoundPathSpecifier path:
                yield return path.Path.ToString();
                break;
            case SoundCollectionSpecifier collection when collection.Collection is { } id:
                foreach (var file in protos.Index<SoundCollectionPrototype>(id).PickFiles)
                {
                    yield return file.ToString();
                }

                break;
        }
    }

    /// <summary>Sounds taken out of the dread pools for sounding comic or cartoonish.</summary>
    private static readonly string[] RemovedSounds =
    {
        "whisper_ghost_1", "whisper_ghost_2", "whisper_ghost_3", "menace_screech", "voice_i_see_you_1", "voice_i_see_you_2",
        "voice_im_here_1", "voice_im_here_2",
    };

    [Test]
    public async Task NoStockOrCartoonishSoundIsLeftInAnyDreadPool()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ProtoMan;
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var pools = new Dictionary<string, List<string>>();
            foreach (var id in new[] { "WFDreadWhispers", "WFDreadVoices", "WFDreadMenace" })
            {
                pools[id] = FilesOf(protos, new SoundCollectionSpecifier(id)).ToList();
            }

            // What the false-sound trick can play, at every stage.
            var director = entMan.System<WFRemadeDirectorSystem>();
            foreach (var stage in Enum.GetValues<WFMonolithStage>())
            {
                var voices = director.FalseSoundPool(stage).SelectMany(sound => FilesOf(protos, sound)).Distinct().ToList();
                pools[$"false sounds at {stage}"] = voices;
            }

            var component = new WFRemadeDirectorComponent();
            pools["director fallback"] = FilesOf(protos, component.FalseSound).ToList();
            pools["director far cry"] = FilesOf(protos, component.FarCry).ToList();
            pools["director alarm"] = FilesOf(protos, component.AlarmSound).ToList();

            Assert.That(pools["WFDreadWhispers"], Has.Count.GreaterThanOrEqualTo(12), "the whisper pool is thin.");
            foreach (var (name, files) in pools)
            {
                if (!name.StartsWith("false sounds"))
                    Assert.That(files, Is.Not.Empty, $"{name} has no sounds.");

                foreach (var file in files)
                {
                    Assert.That(file, Does.StartWith("/Audio/_WF/Remade/"), $"{name} plays {file}, which isn't a DS13 Remade recording.");
                    Assert.That(resources.ContentFileExists(file), Is.True, $"{name} plays {file}, which doesn't exist.");
                    foreach (var removed in RemovedSounds)
                    {
                        Assert.That(file, Does.Not.Contain(removed), $"{name} still plays {file}.");
                    }
                }
            }

            foreach (var removed in RemovedSounds)
            {
                Assert.That(resources.ContentFileExists($"/Audio/_WF/Remade/dread/{removed}.ogg"), Is.False, $"{removed}.ogg is still on disk.");
            }

            // The whisper bed's layers all exist.
            foreach (var layer in WFDreadBedSystem.Layers)
            {
                Assert.That(resources.ContentFileExists(layer), Is.True, $"the bed's {layer} is missing.");
            }
        });
        await pair.CleanReturnAsync();
    }
}
