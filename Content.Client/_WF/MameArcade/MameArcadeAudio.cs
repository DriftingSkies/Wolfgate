using System.Numerics;
using Robust.Client.Audio;
using Robust.Shared.Audio.Sources;

namespace Content.Client._WF.MameArcade;

/// <summary>
/// Plays a continuous stream of game sound: the emulator's 48 kHz stereo in the player's ears, or a cabinet's mono
/// stream at its place in the world. Content can't stream audio, so it plays short clips that overlap by a crossfade,
/// which hides the frame-long jitter in when each one starts.
/// </summary>
public sealed class MameArcadeAudio : IDisposable
{
    public const int SampleRate = 48000;

    private readonly IAudioManager _audio;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly int _chunkFrames;
    private readonly int _overlapFrames;
    private readonly float _chunkSeconds;
    private readonly int _startFrames;

    // Past this the source is running ahead of playback; drop the oldest sound rather than drift further behind.
    private readonly int _maxBacklogFrames;

    private readonly List<short> _pending = new();
    private readonly short[] _chunk;
    private Clip? _current;
    private Clip? _previous;
    private Placement? _placement;

    // Set when playback ran dry mid-game; it then waits for half a clip more, so short waits don't turn into crackle.
    private bool _starved;

    /// <summary>Linear volume, applied from the next <see cref="Update"/>.</summary>
    public float Gain = 1f;

    /// <param name="startFrames">Sound to gather before the first clip, on top of one clip, to ride out late packets.</param>
    public MameArcadeAudio(IAudioManager audio, int channels = 2, int sampleRate = SampleRate, int startFrames = 0)
    {
        _audio = audio;
        _channels = channels;
        _sampleRate = sampleRate;
        _chunkFrames = sampleRate / 10;
        _overlapFrames = sampleRate * 3 / 100;
        _chunkSeconds = (float) _chunkFrames / sampleRate;
        _maxBacklogFrames = sampleRate / 2 + startFrames;
        _chunk = new short[(_chunkFrames + _overlapFrames) * channels];
        _startFrames = startFrames;
        _starved = startFrames > 0;
    }

    /// <summary>
    /// Plays mono sound from <paramref name="position"/> in world space, fading out by
    /// <paramref name="maxDistance"/>, instead of in the listener's ears.
    /// </summary>
    public void Place(Vector2 position, float occlusion, float maxDistance, float referenceDistance)
    {
        _placement = new Placement(position, occlusion, maxDistance, referenceDistance);
        if (_current != null)
            Apply(_current.Source);
        if (_previous != null)
            Apply(_previous.Source);
    }

    /// <summary>Adds interleaved 16-bit samples.</summary>
    public void Feed(IReadOnlyCollection<short> samples)
    {
        _pending.AddRange(samples);

        var excess = _pending.Count - _maxBacklogFrames * _channels;
        if (excess > 0)
            _pending.RemoveRange(0, excess - excess % _channels);
    }

    /// <summary>Starts the next clip when the playing one reaches its fade.</summary>
    public void Update()
    {
        if (_current != null)
        {
            Apply(_current.Source);
            if (_current.Source.Playing && _current.Source.PlaybackPosition < _chunkSeconds)
                return;
        }

        var needed = _starved ? _chunk.Length + Math.Max(_chunkFrames / 2, _startFrames) * _channels : _chunk.Length;
        if (_pending.Count < needed)
        {
            if (_current is { Source.Playing: false })
                _starved = true;
            return;
        }

        _starved = false;

        // Waiting out an underrun leaves extra sound queued; drop it rather than keep the added lag.
        var excess = _pending.Count - (_chunk.Length + Math.Max(_chunkFrames, _startFrames) * _channels);
        if (excess > 0)
            _pending.RemoveRange(0, excess - excess % _channels);

        _pending.CopyTo(0, _chunk, 0, _chunk.Length);
        _pending.RemoveRange(0, _chunkFrames * _channels);
        Fade(_chunk);

        var stream = _audio.LoadAudioRaw(_chunk, _channels, _sampleRate);
        if (_audio.CreateAudioSource(stream) is not { } source)
        {
            stream.Dispose();
            return;
        }

        Apply(source);
        source.StartPlaying();

        _previous?.Dispose();
        _previous = _current;
        _current = new Clip(stream, source);
    }

    public void Dispose()
    {
        _previous?.Dispose();
        _current?.Dispose();
        _previous = _current = null;
    }

    private void Apply(IAudioSource source)
    {
        source.Gain = Gain;
        if (_placement is not { } place)
        {
            source.Global = true;
            source.RolloffFactor = 0;
            return;
        }

        source.Global = false;
        source.Position = place.Position;
        source.Occlusion = place.Occlusion;
        source.MaxDistance = place.MaxDistance;
        source.ReferenceDistance = place.ReferenceDistance;
        source.RolloffFactor = 1;
    }

    /// <summary>Ramps the overlapping head and tail so two clips sum to the original signal.</summary>
    private void Fade(short[] chunk)
    {
        var frames = chunk.Length / _channels;
        for (var i = 0; i < _overlapFrames; i++)
        {
            var rise = (float) i / _overlapFrames;
            ScaleFrame(chunk, i, rise);
            ScaleFrame(chunk, frames - 1 - i, rise);
        }
    }

    private void ScaleFrame(short[] chunk, int frame, float gain)
    {
        for (var c = 0; c < _channels; c++)
        {
            chunk[frame * _channels + c] = (short) (chunk[frame * _channels + c] * gain);
        }
    }

    private readonly record struct Placement(Vector2 Position, float Occlusion, float MaxDistance, float ReferenceDistance);

    private sealed record Clip(AudioStream Stream, IAudioSource Source) : IDisposable
    {
        public void Dispose()
        {
            Source.Dispose();
            Stream.Dispose();
        }
    }
}
