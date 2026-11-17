using Content.Client._WG.Shuttles.Systems;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Audio.InternetSound;
using Robust.Client.Audio;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WG.Audio.InternetSound;

/// <summary>
/// Plays the internet sound an item such as a boombox is carrying, from the item, at the item's place on its
/// timeline. The voice starts once the audio has arrived and stops out of earshot or when the track changes.
/// The jukebox volume option scales it, and at zero mutes it.
/// </summary>
public sealed partial class InternetSoundSourceSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ShuttleCameraListenerSystem _listener = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private const float CheckInterval = 0.25f;

    /// <summary>Earshot plus a little, so a listener on the edge doesn't restart the track over and over.</summary>
    private const float Hysteresis = 2f;

    private readonly Dictionary<EntityUid, (EntityUid Stream, int Track)> _voices = new();
    private readonly HashSet<int> _released = new();
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<InternetSoundStopEvent>(ev => Release(ev.Id));
        SubscribeNetworkEvent<InternetSoundReleaseEvent>(ev => Release(ev.Id));
        SubscribeLocalEvent<InternetSoundSourceComponent, ComponentShutdown>((uid, _, _) => StopVoice(uid));
    }

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var uid in new List<EntityUid>(_voices.Keys))
            StopVoice(uid);

        _released.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _accumulator += frameTime;
        if (_accumulator < CheckInterval)
            return;
        _accumulator = 0f;

        var listener = _listener.GetListener();
        var query = EntityQueryEnumerator<InternetSoundSourceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var source, out var xform))
        {
            var playing = _voices.TryGetValue(uid, out var voice) && Exists(voice.Stream);
            if (playing && voice.Track != source.TrackId)
                StopVoice(uid);

            var gain = _cfg.GetCVar(JukeboxVolumeCVars.Volume);
            var wanted = gain > 0f && source.Path != null && source.IsPlaying(_timing.CurTime) && _timing.CurTime >= source.Start
                         && !_released.Contains(source.TrackId)
                         && xform.MapID == listener.MapId
                         && (_xform.GetWorldPosition(xform) - listener.Position).Length()
                            < source.Range + (playing ? Hysteresis : 0f);

            if (!wanted)
            {
                StopVoice(uid);
                continue;
            }

            if (_voices.TryGetValue(uid, out voice) && Exists(voice.Stream))
            {
                // The owner can turn it up or down mid-track.
                _audio.SetVolume(voice.Stream, Volume(source, gain));
                continue;
            }

            StartVoice(uid, source, gain);
        }
    }

    /// <summary>The source's own volume with this client's jukebox volume applied.</summary>
    private static float Volume(InternetSoundSourceComponent source, float gain)
    {
        return source.Volume + SharedAudioSystem.GainToVolume(gain);
    }

    private void StartVoice(EntityUid uid, InternetSoundSourceComponent source, float gain)
    {
        // Querying the cache before the file arrives would cache it as missing and poison later playback.
        if (source.Path is not { } path || !_resources.ContentFileExists(new ResPath(path)))
            return;

        AudioResource resource;
        try
        {
            resource = _resources.GetResource<AudioResource>(path, useFallback: false);
        }
        catch (Exception e)
        {
            _released.Add(source.TrackId);
            Log.Warning($"Cannot play internet sound {path} from {ToPrettyString(uid)}: {e.Message}");
            return;
        }

        var offset = (float) (_timing.CurTime - source.Start).TotalSeconds;
        var parameters = AudioParams.Default
            .WithVolume(Volume(source, gain))
            .WithMaxDistance(source.Range)
            .WithReferenceDistance(2f)
            .WithRolloffFactor(1.5f)
            .WithPlayOffset(offset);

        var stream = _audio.PlayEntity(resource.AudioStream, uid, _audio.ResolveSound(new SoundPathSpecifier(path)), parameters);
        if (stream == null)
            return;

        // The engine's local despawn timer then runs on the remaining length, not the whole track.
        _audio.SetPlaybackPosition(stream.Value.Entity, offset);
        _voices[uid] = (stream.Value.Entity, source.TrackId);
    }

    private void StopVoice(EntityUid uid)
    {
        if (!_voices.Remove(uid, out var voice))
            return;

        if (Exists(voice.Stream))
            _audio.Stop(voice.Stream);
    }

    private void Release(int id)
    {
        if (id == 0)
            return;

        _released.Add(id);
        foreach (var (uid, voice) in new List<KeyValuePair<EntityUid, (EntityUid Stream, int Track)>>(_voices))
        {
            if (voice.Track == id)
                StopVoice(uid);
        }
    }
}
