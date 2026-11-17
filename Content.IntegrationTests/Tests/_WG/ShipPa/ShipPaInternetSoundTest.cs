#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Audio.InternetSound;
using Content.Server._WG.ShipPa;
using Content.Server.Power.Components;
using Content.Shared._WG.Audio.InternetSound;
using Content.Shared._WG.ShipPa;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.ShipPa;

/// <summary>
/// Internet sounds are mounted as ordinary resources so the PA can play them like anything else. Fetching
/// needs yt-dlp, so these tests mount a shipped Ogg by hand and exercise everything downstream of that.
/// </summary>
[TestFixture]
[TestOf(typeof(InternetSoundSystem))]
public sealed class ShipPaInternetSoundTest
{
    /// <summary>A short shipped sound, standing in for a fetched track.</summary>
    public sealed class FinishedObserver : EntitySystem
    {
        public new int Count;
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ShipPaTrackFinishedEvent>(OnFinished);
        }
        private void OnFinished(ref ShipPaTrackFinishedEvent ev) => Count++;
    }

    private const string SourceSound = "/Audio/Effects/Arcade/newgame.ogg";

    private const string SpeakerProto = "WGWallmountShipPaSpeaker";

    [Test]
    public async Task ATrackHasOneTimelineAndClearsItself()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var pa = entMan.System<ShipPaSystem>();
        var grid = map.Grid.Owner;

        var speakers = new List<EntityUid>();
        var path = string.Empty;
        var observer = server.System<FinishedObserver>();

        await server.WaitAssertion(() =>
        {
            var resourceManager = server.ResolveDependency<IResourceManager>();
            var resources = InternetSoundResources.For(resourceManager);

            const int id = 9002;
            resources.Store(id, Read(resourceManager, SourceSound));
            path = InternetSoundResources.PathFor(id);

            Assert.That(resourceManager.ContentFileExists(new ResPath(path)), Is.True,
                "A stored track should resolve through the mounted content root.");
            Assert.That(server.System<SharedAudioSystem>().GetAudioLength(new ResolvedPathSpecifier(path)), Is.GreaterThan(TimeSpan.Zero),
                "The audio system must decode a track mounted at runtime.");

            var mapSys = entMan.System<SharedMapSystem>();

            for (var i = 0; i < 3; i++)
            {
                mapSys.SetTile(map.Grid, new Vector2i(i, 0), map.Tile.Tile);

                var speaker = entMan.SpawnEntity(SpeakerProto, new EntityCoordinates(grid, i + 0.5f, 0.5f));
                entMan.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
                speakers.Add(speaker);
            }

        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            observer.Count = 0;
            Assert.That(pa.StartTrack(grid, InternetSoundSystem.TrackKey, new SoundPathSpecifier(path)), Is.True,
                "A mounted track should start on the ship's PA.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(StreamsFor(entMan, path), Is.Empty);
            var broadcasts = entMan.GetComponent<ShipPaBroadcastComponent>(grid).Broadcasts;
            Assert.That(broadcasts, Has.Count.EqualTo(1));
            Assert.That(broadcasts[0].Path, Is.EqualTo(path));
            Assert.That(broadcasts[0].Kind, Is.EqualTo(ShipPaBroadcastKind.Track));
            broadcasts[0].Priority = 73; // Lifecycle must not depend on ordering.
            Assert.That(speakers.All(uid => entMan.GetComponent<ShipPaSpeakerComponent>(uid).Enabled), Is.True);
        });

        // The sound is a couple of seconds long; the PA notices it has finished on its next reconcile.
        await pair.RunTicksSync(240);

        await server.WaitAssertion(() =>
        {
            Assert.That(StreamsFor(entMan, path), Is.Empty,
                "A track should stop itself once it has played out, rather than looping like an alarm.");

            // Clearing the key is what tells the sound system the audio is safe to free; a looping alarm
            // in the same slot would still be active here.
            Assert.That(observer.Count, Is.EqualTo(1), "Track completion must survive a priority change.");
            Assert.That(entMan.HasComponent<ShipPaBroadcastComponent>(grid), Is.False,
                "Idle grids must leave the broadcast update query.");
            Assert.That(pa.IsAlarmActive(grid, InternetSoundSystem.TrackKey), Is.False,
                "A finished track should leave its PA slot free for the next one.");

            var resourceManager = server.ResolveDependency<IResourceManager>();
            InternetSoundResources.For(resourceManager).Remove(9002);
            Assert.That(resourceManager.ContentFileExists(new ResPath(path)), Is.False,
                "Removing a track should take it back out of the content root.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ManySpeakersShareOneServerBroadcast()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var pa = entMan.System<ShipPaSystem>();
        var grid = map.Grid.Owner;

        // Comfortably more speakers than the engine will give one client sources for.
        const int speakerCount = 25;
        var path = string.Empty;

        await server.WaitAssertion(() =>
        {
            var resourceManager = server.ResolveDependency<IResourceManager>();
            var resources = InternetSoundResources.For(resourceManager);

            const int id = 9003;
            resources.Store(id, Read(resourceManager, SourceSound));
            path = InternetSoundResources.PathFor(id);

            var mapSys = entMan.System<SharedMapSystem>();

            for (var i = 0; i < speakerCount; i++)
            {
                mapSys.SetTile(map.Grid, new Vector2i(i, 0), map.Tile.Tile);

                var speaker = entMan.SpawnEntity(SpeakerProto, new EntityCoordinates(grid, i + 0.5f, 0.5f));
                entMan.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
            }
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(pa.CountSpeakers(grid).Online, Is.EqualTo(speakerCount),
                "Every speaker should still be part of the ship's PA.");

            Assert.That(pa.StartTrack(grid, InternetSoundSystem.TrackKey, new SoundPathSpecifier(path)), Is.True);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(StreamsFor(entMan, path), Is.Empty, "Speaker count must never allocate server audio.");
            Assert.That(entMan.GetComponent<ShipPaBroadcastComponent>(grid).Broadcasts, Has.Count.EqualTo(1));
            Assert.That(pa.GetSpeakers(grid).Count(s => s.Comp.Enabled), Is.EqualTo(speakerCount),
                "Every area retains coverage; there is no ship-wide source budget.");
        });

        await pair.CleanReturnAsync();
    }

    private static byte[] Read(IResourceManager resources, string path)
    {
        using var stream = resources.ContentFileRead(path);
        using var memory = new System.IO.MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static List<EntityUid> StreamsFor(IEntityManager entMan, string path)
    {
        var found = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<AudioComponent>();

        while (query.MoveNext(out var uid, out var audio))
        {
            if (audio.FileName == path)
                found.Add(uid);
        }

        return found;
    }
}
