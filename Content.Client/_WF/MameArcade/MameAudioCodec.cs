namespace Content.Client._WF.MameArcade;

/// <summary>
/// Shrinks the game's sound for nearby players: 24 kHz mono, 4-bit IMA ADPCM. Each packet carries its predictor and
/// step size, so one can be decoded without the ones before it.
/// </summary>
public static class MameAudioCodec
{
    public const int SampleRate = 24000;

    // The emulator's 48 kHz frames, one kept of every two after the low-pass.
    public const int Decimation = MameArcadeAudio.SampleRate / SampleRate;

    private const int HeaderBytes = 3;

    // Set on the step byte when the last nibble is padding; steps only reach 88.
    private const byte OddFlag = 0x80;

    private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

    private static readonly int[] StepTable =
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97,
        107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
        876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871,
        5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623,
        27086, 29794, 32767,
    };

    public static byte[] Encode(IReadOnlyList<short> samples)
    {
        var index = 0;
        return Encode(samples, ref index);
    }

    /// <summary>Encodes from step <paramref name="index"/> and leaves it where the packet ends, for the next one.</summary>
    public static byte[] Encode(IReadOnlyList<short> samples, ref int index)
    {
        var packet = new byte[HeaderBytes + (samples.Count + 1) / 2];
        int predictor = samples.Count > 0 ? samples[0] : 0;
        index = Math.Clamp(index, 0, StepTable.Length - 1);
        packet[0] = (byte) predictor;
        packet[1] = (byte) (predictor >> 8);
        packet[2] = (byte) (index | (samples.Count % 2 == 1 ? OddFlag : 0));

        for (var i = 0; i < samples.Count; i++)
        {
            var step = StepTable[index];
            var diff = samples[i] - predictor;
            var code = 0;
            if (diff < 0)
            {
                code = 8;
                diff = -diff;
            }

            var delta = step >> 3;
            if (diff >= step)
            {
                code |= 4;
                diff -= step;
                delta += step;
            }

            if (diff >= step >> 1)
            {
                code |= 2;
                diff -= step >> 1;
                delta += step >> 1;
            }

            if (diff >= step >> 2)
            {
                code |= 1;
                delta += step >> 2;
            }

            predictor = Math.Clamp((code & 8) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue);
            index = Math.Clamp(index + IndexTable[code], 0, StepTable.Length - 1);
            packet[HeaderBytes + i / 2] |= (byte) (i % 2 == 0 ? code : code << 4);
        }

        return packet;
    }

    /// <summary>The samples in a packet, or none if it is malformed.</summary>
    public static short[] Decode(byte[] packet)
    {
        if (packet.Length < HeaderBytes)
            return Array.Empty<short>();

        int predictor = (short) (packet[0] | packet[1] << 8);
        var index = Math.Clamp(packet[2] & ~OddFlag, 0, StepTable.Length - 1);
        var count = (packet.Length - HeaderBytes) * 2;
        if ((packet[2] & OddFlag) != 0 && count > 0)
            count--;

        var samples = new short[count];

        for (var i = 0; i < samples.Length; i++)
        {
            var b = packet[HeaderBytes + i / 2];
            var code = i % 2 == 0 ? b & 0xf : b >> 4;
            var step = StepTable[index];
            var delta = step >> 3;
            if ((code & 4) != 0)
                delta += step;
            if ((code & 2) != 0)
                delta += step >> 1;
            if ((code & 1) != 0)
                delta += step >> 2;

            predictor = Math.Clamp((code & 8) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue);
            index = Math.Clamp(index + IndexTable[code], 0, StepTable.Length - 1);
            samples[i] = (short) predictor;
        }

        return samples;
    }
}

/// <summary>
/// Turns the emulator's 48 kHz stereo into <see cref="MameAudioCodec"/> packets. Low-passes before dropping samples,
/// so the high end doesn't fold back as noise, and carries the ADPCM step from packet to packet, so a packet doesn't
/// open with a burst of distortion. Not thread-safe.
/// </summary>
public sealed class MameAudioEncoder
{
    // Windowed sinc, flat to about 9 kHz and well down by the 12 kHz the codec can carry.
    private static readonly float[] Taps = LowPass(63, 11000f / MameArcadeAudio.SampleRate);

    private readonly float[] _history = new float[Taps.Length];
    private int _at;
    private int _phase;
    private int _index;

    /// <summary>Moves interleaved stereo into mono at <see cref="MameAudioCodec.SampleRate"/>.</summary>
    public void Downmix(List<short> stereo, List<short> into)
    {
        var frames = stereo.Count / 2;
        for (var frame = 0; frame < frames; frame++)
        {
            _history[_at] = (stereo[frame * 2] + stereo[frame * 2 + 1]) * 0.5f;
            _at = (_at + 1) % _history.Length;
            if (++_phase < MameAudioCodec.Decimation)
                continue;

            _phase = 0;
            var sum = 0f;
            for (var i = 0; i < Taps.Length; i++)
            {
                sum += Taps[i] * _history[(_at + i) % _history.Length];
            }

            into.Add((short) Math.Clamp(MathF.Round(sum), short.MinValue, short.MaxValue));
        }

        stereo.RemoveRange(0, frames * 2);
    }

    public byte[] Encode(IReadOnlyList<short> samples) => MameAudioCodec.Encode(samples, ref _index);

    /// <summary>A Blackman-windowed sinc cutting off at <paramref name="cutoff"/> of the sample rate, with unit gain.</summary>
    private static float[] LowPass(int length, float cutoff)
    {
        var taps = new float[length];
        var middle = (length - 1) / 2.0;
        var sum = 0.0;
        for (var i = 0; i < length; i++)
        {
            var x = i - middle;
            var sinc = x == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
            var window = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (length - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (length - 1));
            taps[i] = (float) (sinc * window);
            sum += taps[i];
        }

        for (var i = 0; i < length; i++)
        {
            taps[i] = (float) (taps[i] / sum);
        }

        return taps;
    }
}
