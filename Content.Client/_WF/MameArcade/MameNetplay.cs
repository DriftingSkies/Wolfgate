namespace Content.Client._WF.MameArcade;

/// <summary>
/// Lockstep for a 2-player game. Both clients run the same emulator from the same inputs: each frame's input is
/// decided <see cref="Delay"/> frames ahead and sent to the other player, and a frame only runs once both are known.
/// <para>
/// Both players get the same delay: player 1 picks it from both sides' slack (<see cref="Margin"/>) and sends it with
/// its inputs, and player 2 follows. Which side waits depends on which one runs ahead, so the side with less slack
/// also holds back the odd frame until both have the same; otherwise the one ahead would wait on every frame.
/// </para>
/// </summary>
public sealed class MameNetplay
{
    /// <summary>How often the two games compare a checksum of their state.</summary>
    public const int ChecksumInterval = 120;

    public const int MinDelay = 1;
    public const int MaxDelay = 30;

    /// <summary>Slack reported before any of the partner's inputs have arrived.</summary>
    public const int UnknownMargin = sbyte.MinValue;

    // Each adjustment looks at this long a stretch of play.
    private const float AdaptSeconds = 1f;

    // Frames of slack each side aims to leave the partner, to ride out jitter.
    private const int TargetMargin = 2;

    // The delay rises by at most this much at once, and falls a frame after this many stretches of spare slack.
    private const int MaxRaise = 6;
    private const int SpareStretches = 3;

    // Holding back for balance skips at most one frame in this many, so it never shows as a stutter.
    private const int FramesPerHold = 6;

    // The partner runs at most one delay ahead and queues one more, so inputs past this many frames ahead are bogus.
    public const int MaxAhead = 4 * MaxDelay;

    // Checksums this many frames old that never found their pair are dropped.
    private const int ChecksumLifetime = 4 * ChecksumInterval;

    private readonly Dictionary<int, MameControls> _local = new();
    private readonly Dictionary<int, MameControls> _remote = new();
    private readonly Dictionary<int, uint> _localChecksums = new();
    private readonly Dictionary<int, uint> _remoteChecksums = new();
    private readonly List<MameControls> _outbox = new();
    private int _outboxStart;
    private int _nextLocal;
    private float _stretch;
    private bool _stalledThisStretch;
    private int _lowestThisStretch = int.MaxValue;
    private int _spare;
    private int _holdsOwed;
    private int _framesSinceHold;

    /// <summary>0 plays as player 1, 1 as player 2.</summary>
    public readonly int Seat;

    /// <summary>Frames between pressing a key and the game seeing it, so the other player's inputs arrive in time.</summary>
    public int Delay { get; private set; }

    /// <summary>The next frame to emulate.</summary>
    public int Frame { get; private set; }

    /// <summary>
    /// How many frames early the partner's inputs arrived at worst over the last stretch, or -1 if the game had to
    /// wait. Sent to the partner, whose delay it tunes.
    /// </summary>
    public int Margin { get; private set; } = UnknownMargin;

    /// <summary>The partner's last reported <see cref="Margin"/>: how early this side's inputs reach them.</summary>
    public int PartnerMargin { get; private set; } = UnknownMargin;

    /// <summary>Player 1 picks the delay for both.</summary>
    public bool SetsDelay => Seat == 0;

    /// <summary>The partner is gone; their controls stay released from here on.</summary>
    public bool Alone { get; private set; }

    /// <summary>The two games no longer match.</summary>
    public bool Desynced { get; private set; }

    public MameNetplay(int seat, int delay)
    {
        Seat = seat;
        Delay = Math.Clamp(delay, MinDelay, MaxDelay);

        for (var frame = 0; frame < Delay; frame++)
        {
            _local[frame] = MameControls.None;
            _remote[frame] = MameControls.None;
        }

        _nextLocal = Delay;
        _outboxStart = Delay;
    }

    /// <summary>
    /// Queues this frame's keys for frame <see cref="Frame"/> + <see cref="Delay"/> and, if both players' input for
    /// <see cref="Frame"/> is known, returns it and moves on. After the delay grows, the new frames between get the
    /// same keys; after it shrinks, no new frame is queued until the game catches up.
    /// </summary>
    public bool TryAdvance(MameControls held, out MameControls playerOne, out MameControls playerTwo)
    {
        while (_nextLocal <= Frame + Delay)
        {
            _local[_nextLocal++] = held;
            _outbox.Add(held);
        }

        playerOne = playerTwo = MameControls.None;
        if (!_remote.TryGetValue(Frame, out var remote))
        {
            if (!Alone)
            {
                _stalledThisStretch = true;
                return false;
            }

            remote = MameControls.None;
        }

        var local = _local[Frame];
        playerOne = Seat == 0 ? local : remote;
        playerTwo = Seat == 0 ? remote : local;

        _local.Remove(Frame);
        _remote.Remove(Frame);
        Frame++;
        _framesSinceHold++;
        return true;
    }

    /// <summary>
    /// True if this side should skip running a due frame to let the partner catch up. The caller still spends the
    /// frame's time, which moves this game a frame later against the partner's.
    /// </summary>
    public bool TakeHold()
    {
        if (Alone || _holdsOwed <= 0 || _framesSinceHold < FramesPerHold)
            return false;

        _holdsOwed--;
        _framesSinceHold = 0;
        return true;
    }

    /// <summary>Once a stretch, updates <see cref="Margin"/>, the delay and how many frames to hold back.</summary>
    public void Adapt(float deltaSeconds)
    {
        if (Alone)
            return;

        _stretch += deltaSeconds;
        if (_stretch < AdaptSeconds)
            return;

        if (_stalledThisStretch)
            Margin = -1;
        else if (_lowestThisStretch != int.MaxValue)
            Margin = Math.Min(_lowestThisStretch, sbyte.MaxValue);

        if (PartnerMargin != UnknownMargin && Margin != UnknownMargin)
        {
            if (SetsDelay)
                PickDelay(Math.Min(Margin, PartnerMargin));

            // Each frame held back gives this side one more frame of slack and takes one from the partner.
            _holdsOwed = Margin != UnknownMargin && PartnerMargin - Margin >= 2 ? (PartnerMargin - Margin) / 2 : 0;
        }

        _stretch = 0f;
        _stalledThisStretch = false;
        _lowestThisStretch = int.MaxValue;
        PruneChecksums(_localChecksums);
        PruneChecksums(_remoteChecksums);
    }

    private void PruneChecksums(Dictionary<int, uint> checksums)
    {
        foreach (var frame in checksums.Keys)
        {
            if (frame < Frame - ChecksumLifetime)
                checksums.Remove(frame);
        }
    }

    /// <summary>
    /// Inputs from the partner, starting at <paramref name="firstFrame"/>, with the slack they report and their delay,
    /// which player 2 adopts.
    /// </summary>
    public void ReceiveInputs(int firstFrame, IReadOnlyList<ushort> inputs, int partnerMargin, int partnerDelay)
    {
        if (!SetsDelay && partnerDelay >= MinDelay)
            Delay = Math.Min(partnerDelay, MaxDelay);

        for (var i = 0; i < inputs.Count; i++)
        {
            var frame = firstFrame + i;
            if (frame >= Frame && frame <= Frame + MaxAhead)
                _remote[frame] = (MameControls) inputs[i];
        }

        if (inputs.Count > 0)
            _lowestThisStretch = Math.Min(_lowestThisStretch, firstFrame + inputs.Count - 1 - Frame);

        if (partnerMargin != UnknownMargin)
            PartnerMargin = partnerMargin;
    }

    /// <summary>Local inputs not yet sent.</summary>
    public bool TakeOutbox(out int firstFrame, out ushort[] inputs)
    {
        firstFrame = _outboxStart;
        inputs = new ushort[_outbox.Count];
        for (var i = 0; i < inputs.Length; i++)
        {
            inputs[i] = (ushort) _outbox[i];
        }

        _outboxStart += _outbox.Count;
        _outbox.Clear();
        return inputs.Length > 0;
    }

    /// <summary>Records this game's checksum after <paramref name="frame"/>; true if it already disagrees.</summary>
    public bool AddLocalChecksum(int frame, uint checksum)
    {
        _localChecksums[frame] = checksum;
        return Compare(frame);
    }

    /// <summary>Records the partner's checksum after <paramref name="frame"/>; true if it already disagrees.</summary>
    public bool AddRemoteChecksum(int frame, uint checksum)
    {
        if (frame % ChecksumInterval != 0 || frame < Frame - ChecksumLifetime || frame > Frame + ChecksumLifetime)
            return false;

        _remoteChecksums[frame] = checksum;
        return Compare(frame);
    }

    /// <summary>Plays on without the partner.</summary>
    public void PartnerLeft()
    {
        Alone = true;
        _remote.Clear();
    }

    /// <summary>Raises the shared delay if either side is short of slack, and lowers it after spare slack lasts.</summary>
    private void PickDelay(int slack)
    {
        if (slack < TargetMargin)
        {
            Delay = Math.Min(Delay + Math.Clamp(TargetMargin - slack, 1, MaxRaise), MaxDelay);
            _spare = 0;
        }
        else if (slack > TargetMargin + 1 && Delay > MinDelay)
        {
            if (++_spare >= SpareStretches)
            {
                Delay--;
                _spare = 0;
            }
        }
        else
        {
            _spare = 0;
        }
    }

    private bool Compare(int frame)
    {
        if (Alone || !_localChecksums.TryGetValue(frame, out var local) || !_remoteChecksums.TryGetValue(frame, out var remote))
            return false;

        _localChecksums.Remove(frame);
        _remoteChecksums.Remove(frame);
        if (local == remote)
            return false;

        Desynced = true;
        return true;
    }
}
