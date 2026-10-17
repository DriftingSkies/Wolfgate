#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.ShipPa;
using Content.Shared._WF.ShipPa;
using Content.Shared._WF.Audio.InternetSound;
using Robust.Shared.ContentPack;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.ShipPa;

[TestFixture]
public sealed class ShipPaListenerTest
{

    [Test]
    public async Task ListenerHandoffsPowerLossPriorityAndReleaseStayBounded()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var em = client.EntMan;
        var pa = em.System<ShipPaMeshSystem>();
        var eye = new FixedEye();
        var timing = client.ResolveDependency<IGameTiming>();
        EntityUid map = default;
        EntityUid grid = default;
        MapId mapId = default;
        var speakers = new List<EntityUid>();

        await client.WaitAssertion(() =>
        {
            client.ResolveDependency<IEyeManager>().CurrentEye = eye;
            var maps = em.System<SharedMapSystem>();
            map = maps.CreateMap(out mapId);
            var gridEntity = client.System<SharedMapSystem>().CreateGridEntity(mapId);
            grid = gridEntity.Owner;
            var xform = em.System<SharedTransformSystem>();
            for (var i = 0; i < 25; i++)
            {
                maps.SetTile(gridEntity, new Vector2i(i * 4, 0), new Tile(1));
                var uid = em.SpawnEntity(null, new EntityCoordinates(grid, i * 4 + 0.5f, 0.5f));
                var speaker = em.AddComponent<ShipPaSpeakerComponent>(uid);
                speaker.Enabled = true;
                xform.AnchorEntity(uid);
                speakers.Add(uid);
            }

            var state = em.AddComponent<ShipPaBroadcastComponent>(grid);
            state.Broadcasts.Add(new ShipPaBroadcast
            {
                Id = 1, Key = "music", Path = "/Audio/Announcements/attention.ogg",
                Start = timing.CurTime - TimeSpan.FromSeconds(0.2), Length = 0.91f, Loop = true, Priority = 10,
                Params = AudioParams.Default.WithLoop(true),
            });
            eye.Position = new MapCoordinates(new Vector2(0.5f, 0.5f), mapId);
            pa.FrameUpdate(0.3f);
            Assert.That(Voices(em), Has.Count.EqualTo(1));
            Assert.That(Voices(em)[0].Speaker, Is.EqualTo(speakers[0]));
        });

        // A listener at the stern makes its own selection; there is no server-wide nearest-12 subset.
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
        {
            eye.Position = new MapCoordinates(new Vector2(96.5f, 0.5f), mapId);
            pa.FrameUpdate(0.1f);
            Assert.That(Voices(em).Count, Is.LessThanOrEqualTo(2));
            Assert.That(Voices(em).Any(v => v.Speaker == speakers[^1]), Is.True);
            pa.FrameUpdate(0.3f);
            Assert.That(Voices(em), Has.Count.EqualTo(1));
            Assert.That(Voices(em)[0].Speaker, Is.EqualTo(speakers[^1]));

            eye.Position = new MapCoordinates(new Vector2(48.5f, 0.5f), mapId);
            pa.FrameUpdate(0.1f);
            Assert.That(Voices(em).Count, Is.LessThanOrEqualTo(2));
            eye.Position = new MapCoordinates(new Vector2(0.5f, 0.5f), mapId);
            pa.FrameUpdate(0.1f);
            Assert.That(Voices(em).Count, Is.LessThanOrEqualTo(2));
            eye.Position = new MapCoordinates(new Vector2(96.5f, 0.5f), mapId);
            pa.FrameUpdate(0.3f);

            // Breaking the selected speaker hands playback to a neighbour without changing the timeline.
            em.GetComponent<ShipPaSpeakerComponent>(speakers[^1]).Enabled = false;
        });

        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
        {
            pa.FrameUpdate(0.3f);
            Assert.That(Voices(em).Any(v => v.Speaker == speakers[^1]), Is.False);
            Assert.That(Voices(em).Any(v => v.Speaker == speakers[^2]), Is.True);

            var state = em.GetComponent<ShipPaBroadcastComponent>(grid);
            state.Broadcasts.Add(new ShipPaBroadcast
            {
                Id = 2, Key = "alarm", Path = "/Audio/Announcements/attention.ogg",
                Start = timing.CurTime, Length = 0.91f, Loop = true, Priority = 20,
                Params = AudioParams.Default.WithLoop(true),
            });
        });

        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
        {
            pa.FrameUpdate(0.3f);
            Assert.That(Voices(em), Has.Count.EqualTo(1));
            Assert.That(Voices(em)[0].BroadcastId, Is.EqualTo(2));
            em.GetComponent<ShipPaBroadcastComponent>(grid).Broadcasts.RemoveAll(b => b.Id == 2);
        });

        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
        {
            pa.FrameUpdate(0.3f);
            Assert.That(Voices(em)[0].BroadcastId, Is.EqualTo(1), "Music resumes its existing timeline after the alarm.");

            // A release that overtakes replicated state must dispose and never recreate the source.
            pa.ReleasePath("/Audio/Announcements/attention.ogg");
            pa.FrameUpdate(0.3f);
            Assert.That(Voices(em), Is.Empty);
            em.DeleteEntity(map);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReplicatedTimelineWaitsForAssetAndReleaseCannotRestartIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var client = pair.Client;
        var em = client.EntMan;
        var pa = em.System<ShipPaMeshSystem>();
        var eye = new FixedEye();
        const int id = 9101;
        var path = InternetSoundResources.PathFor(id);
        byte[] bytes = [];
        TimeSpan start = default;

        await server.WaitAssertion(() =>
        {
            var resources = server.ResolveDependency<IResourceManager>();
            using var input = resources.ContentFileRead("/Audio/Announcements/attention.ogg");
            using var output = new System.IO.MemoryStream();
            input.CopyTo(output);
            bytes = output.ToArray();
            InternetSoundResources.For(resources).Store(id, bytes);
            var speaker = server.EntMan.SpawnEntity("WFWallmountShipPaSpeaker", map.GridCoords);
            server.EntMan.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(speaker).NeedsPower = false;
        });

        await pair.RunTicksSync(20);
        await server.WaitAssertion(() =>
        {
            server.System<Content.Server._WF.ShipPa.ShipPaSystem>().StartAlarm(map.Grid.Owner, "test", new SoundPathSpecifier(path), message: "Test announcement");
            start = server.EntMan.GetComponent<ShipPaBroadcastComponent>(map.Grid.Owner).Broadcasts.Single().Start;
        });

        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            client.ResolveDependency<IEyeManager>().CurrentEye = eye;
            eye.Position = map.MapCoords;
            var grid = em.GetEntity(server.EntMan.GetNetEntity(map.Grid.Owner));
            var state = em.GetComponent<ShipPaBroadcastComponent>(grid);
            Assert.That(state.Broadcasts, Has.Count.EqualTo(1), "Timeline must survive network serialization.");
            Assert.That(state.Broadcasts[0].Start, Is.EqualTo(start));
            var speakers = new List<ShipPaSpeakerComponent>();
            var speakerQuery = em.EntityQueryEnumerator<ShipPaSpeakerComponent>();
            while (speakerQuery.MoveNext(out var speaker))
            {
                speakers.Add(speaker);
                speaker.Enabled = false;
            }
            pa.FrameUpdate(0.3f);
            Assert.That(Voices(em), Is.Empty, "An asset that has not arrived must not create a fallback sound.");

            var earlySubtitle = client.ResolveDependency<IUserInterfaceManager>().PopupRoot.Children.OfType<PanelContainer>()
                .Where(p => p.Name == "ShipPaSubtitle").SelectMany(p => p.Children).OfType<Label>()
                .Single(l => l.Text != null && l.Text.Contains("Test announcement"));
            Assert.That(earlySubtitle.Parent!.Visible, Is.True, "Grid captions must not need audible speakers or completed downloads.");
            foreach (var speaker in speakers)
                speaker.Enabled = true;

            var resources = client.ResolveDependency<IResourceCache>();
            InternetSoundResources.For(resources).Store(id, bytes);
            pa.FrameUpdate(0.3f);
            Assert.That(Voices(em), Has.Count.EqualTo(1));
            var subtitle = client.ResolveDependency<IUserInterfaceManager>().PopupRoot.Children.OfType<PanelContainer>()
                .Where(p => p.Name == "ShipPaSubtitle").SelectMany(p => p.Children).OfType<Label>()
                .Single(l => l.Text != null && l.Text.Contains("Test announcement"));
            Assert.That(subtitle.Parent!.Visible, Is.True);
        });

        // Release deliberately arrives while the old broadcast still exists on both ends.
        await server.WaitPost(() => server.EntMan.EntityNetManager!.SendSystemNetworkMessage(new InternetSoundReleaseEvent(id)));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            pa.FrameUpdate(0.3f);
            em.System<Content.Client._WF.Audio.InternetSound.InternetSoundSystem>().FrameUpdate(0.3f);
            Assert.That(Voices(em), Is.Empty);
            Assert.That(InternetSoundResources.For(client.ResolveDependency<IResourceCache>()).Has(id), Is.False,
                "The compressed resource must be released after its local voices are disposed.");
        });

        await server.WaitPost(() =>
        {
            server.System<Content.Server._WF.ShipPa.ShipPaSystem>().StopAlarm(map.Grid.Owner, "test");
            InternetSoundResources.For(server.ResolveDependency<IResourceManager>()).Remove(id);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ScheduledAnnouncementIsPreparedWithoutConsumingItsOpening()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var em = client.EntMan;
        var pa = em.System<ShipPaMeshSystem>();
        var timing = client.ResolveDependency<IGameTiming>();
        ShipPaBroadcast broadcast = default!;
        EntityUid voice = default;

        await client.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var grid = client.System<SharedMapSystem>().CreateGridEntity(mapId);
            maps.SetTile(grid, Vector2i.Zero, new Tile(1));
            var speaker = em.SpawnEntity(null, new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
            em.AddComponent<ShipPaSpeakerComponent>(speaker).Enabled = true;
            em.System<SharedTransformSystem>().AnchorEntity(speaker);
            client.ResolveDependency<IEyeManager>().CurrentEye = new FixedEye
            {
                Position = new MapCoordinates(new Vector2(0.5f, 0.5f), mapId),
            };
            broadcast = new ShipPaBroadcast
            {
                Id = 900, Key = "announcement", Path = "/Audio/Announcements/attention.ogg",
                Start = timing.CurTime + TimeSpan.FromSeconds(ShipPaPlaybackPolicy.StartLeadSeconds),
                Length = 0.91f, Priority = 30, Kind = ShipPaBroadcastKind.Announcement,
                Params = AudioParams.Default,
            };
            em.AddComponent<ShipPaBroadcastComponent>(grid.Owner).Broadcasts.Add(broadcast);
            pa.FrameUpdate(0.11f);
            var query = em.EntityQueryEnumerator<ShipPaAudioComponent, AudioComponent>();
            Assert.That(query.MoveNext(out voice, out _, out var audio), Is.True,
                "The source must be prepared before the scheduled start, not after it.");
            Assert.That(audio!.Gain, Is.Zero);
            Assert.That(audio.PlaybackPosition, Is.Zero.Within(0.001f));
            Assert.That(em.HasComponent<Robust.Shared.Spawners.TimedDespawnComponent>(voice), Is.False,
                "The preparation delay must not shorten the clip's lifetime.");
        });

        await pair.RunTicksSync((int) Math.Ceiling((ShipPaPlaybackPolicy.StartLeadSeconds + 0.1f) * timing.TickRate));
        await client.WaitAssertion(() =>
        {
            Assert.That(timing.CurTime, Is.GreaterThanOrEqualTo(broadcast.Start));
            pa.FrameUpdate(0.01f);
            Assert.That(em.GetComponent<AudioComponent>(voice).Gain, Is.GreaterThan(0.9f),
                "A prepared announcement starts at full gain instead of fading over its first syllable.");
            Assert.That(Voices(em), Has.Count.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CaptionsKeepUrgentTextButAllowEscalationAndGridChanges()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var em = client.EntMan;
            var pa = em.System<ShipPaMeshSystem>();
            var maps = em.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var grid = client.System<SharedMapSystem>().CreateGridEntity(mapId);
            maps.SetTile(grid, Vector2i.Zero, new Tile(1));
            var eye = new FixedEye { Position = new MapCoordinates(new Vector2(0.5f, 0.5f), mapId) };
            client.ResolveDependency<IEyeManager>().CurrentEye = eye;
            var state = em.AddComponent<ShipPaBroadcastComponent>(grid.Owner);
            state.Broadcasts.Add(new ShipPaBroadcast { Id = 9301, Caption = "Urgent warning", Priority = 40, Loop = true });
            state.Broadcasts.Add(new ShipPaBroadcast { Id = 9302, Caption = "Song title", Priority = 10, Loop = true });
            pa.FrameUpdate(0.11f);
            var label = client.ResolveDependency<IUserInterfaceManager>().PopupRoot.Children.OfType<PanelContainer>()
                .Single(p => p.Name == "ShipPaSubtitle").Children.OfType<Label>().Single();
            Assert.That(label.Text, Does.Contain("Urgent warning"));
            pa.FrameUpdate(0.11f);
            Assert.That(label.Text, Does.Contain("Urgent warning"), "An unseen song must not displace an urgent caption on the next selection.");
            state.Broadcasts.Add(new ShipPaBroadcast { Id = 9303, Caption = "Collision imminent", Priority = 60, Loop = true });
            pa.FrameUpdate(0.11f);
            Assert.That(label.Text, Does.Contain("Collision imminent"));

            var other = client.System<SharedMapSystem>().CreateGridEntity(mapId);
            em.System<SharedTransformSystem>().SetWorldPosition(other.Owner, new Vector2(20, 0));
            maps.SetTile(other, Vector2i.Zero, new Tile(1));
            em.AddComponent<ShipPaBroadcastComponent>(other.Owner).Broadcasts.Add(new ShipPaBroadcast
                { Id = 9304, Caption = "Other ship", Priority = 10, Loop = true });
            eye.Position = new MapCoordinates(new Vector2(20.5f, 0.5f), mapId);
            pa.FrameUpdate(0.11f);
            Assert.That(label.Text, Does.Contain("Other ship"), "Changing ships must release the previous caption's priority.");
        });
        await pair.CleanReturnAsync();
    }

    private static List<ShipPaAudioComponent> Voices(IEntityManager em)
    {
        var result = new List<ShipPaAudioComponent>();
        var query = em.EntityQueryEnumerator<ShipPaAudioComponent, AudioComponent>();
        while (query.MoveNext(out var pa, out _))
            result.Add(pa);
        return result;
    }
}



