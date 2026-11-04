using Content.Shared._WF.MameArcade;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Client._WF.MameArcade;

/// <summary>Plays the sound of games on cabinets near the player, from the cabinets.</summary>
public sealed class MameArcadeNearbySystem : EntitySystem
{
    // Under the player's own game, like other games heard nearby.
    private const float Volume = -4f;

    // Sound gathered before playing, so late packets don't leave gaps.
    private const int BufferFrames = MameAudioCodec.SampleRate / 5;

    // A cabinet that sent nothing for this long has stopped.
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(2);

    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private IAudioManager _audioManager = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MameArcadeNetManager _net = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, Nearby> _cabinets = new();
    private readonly List<EntityUid> _stale = new();

    /// <summary>Cabinets whose sound this client is playing.</summary>
    public int Playing => _cabinets.Count;

    public override void Initialize()
    {
        base.Initialize();
        _net.StreamReceived += OnStream;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _net.StreamReceived -= OnStream;

        foreach (var nearby in _cabinets.Values)
        {
            nearby.Audio.Dispose();
        }

        _cabinets.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_cabinets.Count == 0)
            return;

        // Where the engine puts the listener.
        var listener = _eye.CurrentEye.Position;
        var now = _timing.RealTime;
        var share = SharedAudioSystem.VolumeToGain(Volume - 10f * MathF.Log10(_cabinets.Count));

        foreach (var (uid, nearby) in _cabinets)
        {
            if (TerminatingOrDeleted(uid) || now - nearby.Heard > SilenceTimeout)
            {
                _stale.Add(uid);
                continue;
            }

            var position = _transform.GetMapCoordinates(uid);
            var delta = position.Position - listener.Position;
            var distance = delta.Length();
            var audible = position.MapId == listener.MapId && distance <= MameArcadeProtocol.HearRange;

            nearby.Audio.Gain = audible ? share : 0f;
            nearby.Audio.Place(
                position.Position,
                audible ? _audioSystem.GetOcclusion(listener, delta, distance, uid) : 0f,
                _audioSystem.GetAudioDistance(MameArcadeProtocol.HearRange),
                _audioSystem.GetAudioDistance(1f));
            nearby.Audio.Update();
        }

        foreach (var uid in _stale)
        {
            _cabinets[uid].Audio.Dispose();
            _cabinets.Remove(uid);
        }

        _stale.Clear();
    }

    private void OnStream(MsgMameArcadeStream message)
    {
        if (message.Kind != MameArcadeStreamKind.Audio || !TryGetEntity(message.Cabinet, out var cabinet))
            return;

        var samples = MameAudioCodec.Decode(message.Data);
        if (samples.Length == 0)
            return;

        if (!_cabinets.TryGetValue(cabinet.Value, out var nearby))
        {
            nearby = new Nearby(new MameArcadeAudio(_audioManager, 1, MameAudioCodec.SampleRate, BufferFrames));
            _cabinets[cabinet.Value] = nearby;
        }

        nearby.Heard = _timing.RealTime;
        nearby.Audio.Feed(samples);
    }

    private sealed class Nearby(MameArcadeAudio audio)
    {
        public readonly MameArcadeAudio Audio = audio;
        public TimeSpan Heard;
    }
}
