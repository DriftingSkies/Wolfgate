using System.Collections.Generic;

namespace Content.Client._WG.JoinProgress;

public enum JoinStage : byte
{
    None,
    Connecting,
    Handshake,
    Downloading,
    EnteringGame,
}

public enum JoinRowState : byte
{
    Pending,
    Active,
    Stuck,
    Done,
    Failed,
}

/// <summary>Tracks the current stage of one join attempt and how long each stage took.</summary>
public sealed class JoinProgressTracker
{
    public const int StageCount = 4;

    /// <summary>A handshake this long is treated as hung; it normally takes well under a second.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(15);

    // The download bar stops here, since game traffic inflates the byte count and the stage outlasts the files.
    private const float MaxDownloadFraction = 0.99f;

    private const int MaxLogEntries = 64;

    private readonly List<(JoinStage Stage, TimeSpan Duration)> _finished = new();
    private readonly List<(TimeSpan Offset, string Text)> _log = new();
    private TimeSpan _attemptStart;
    private TimeSpan _stageStart;

    public JoinStage Stage { get; private set; }

    /// <summary>Timings of the attempt that last ended without a join, until the next attempt starts.</summary>
    public IReadOnlyList<(JoinStage Stage, TimeSpan Duration)>? LastAttempt { get; private set; }

    /// <summary>What happened during the current or last attempt, oldest first.</summary>
    public IReadOnlyList<(TimeSpan Offset, string Text)> Log => _log;

    /// <summary>Moves forward to a stage. Stages never go back within an attempt.</summary>
    public void Advance(JoinStage stage, TimeSpan now)
    {
        if (stage <= Stage)
            return;

        if (Stage == JoinStage.None)
        {
            LastAttempt = null;
            _attemptStart = now;
            _log.Clear();
        }
        else
        {
            _finished.Add((Stage, now - _stageStart));
        }

        Stage = stage;
        _stageStart = now;
    }

    /// <summary>Adds a line to the attempt's log, dropping the oldest once it is full.</summary>
    public void AddLog(string text, TimeSpan now)
    {
        if (_log.Count >= MaxLogEntries)
            _log.RemoveAt(0);

        // Failures can report just after the attempt ended, so they keep its last time.
        var offset = Stage != JoinStage.None
            ? now - _attemptStart
            : _log.Count > 0 ? _log[^1].Offset : TimeSpan.Zero;

        _log.Add((offset, text));
    }

    /// <summary>How a stage's row reads in the current attempt, or in the last one once it has ended.</summary>
    public JoinRowState RowState(JoinStage stage, TimeSpan now)
    {
        if (Stage != JoinStage.None)
        {
            if (stage < Stage)
                return JoinRowState.Done;

            if (stage > Stage)
                return JoinRowState.Pending;

            return IsStuck(now) ? JoinRowState.Stuck : JoinRowState.Active;
        }

        if (LastAttempt == null || LastAttempt.Count == 0 || stage > LastAttempt[^1].Stage)
            return JoinRowState.Pending;

        return stage == LastAttempt[^1].Stage ? JoinRowState.Failed : JoinRowState.Done;
    }

    /// <summary>Ends the attempt, keeping its timings for a bug report. Safe to call when already ended.</summary>
    public void Reset(TimeSpan now)
    {
        if (Stage == JoinStage.None)
            return;

        LastAttempt = Timings(now);
        _finished.Clear();
        Stage = JoinStage.None;
    }

    public TimeSpan StageElapsed(TimeSpan now)
    {
        return Stage == JoinStage.None ? TimeSpan.Zero : now - _stageStart;
    }

    public TimeSpan AttemptElapsed(TimeSpan now)
    {
        return Stage == JoinStage.None ? TimeSpan.Zero : now - _attemptStart;
    }

    public bool IsStuck(TimeSpan now)
    {
        return Stage == JoinStage.Handshake && StageElapsed(now) >= StuckAfter;
    }

    /// <summary>Every stage reached so far in this attempt, with the current one's time so far.</summary>
    public List<(JoinStage Stage, TimeSpan Duration)> Timings(TimeSpan now)
    {
        var timings = new List<(JoinStage Stage, TimeSpan Duration)>(_finished);
        if (Stage != JoinStage.None)
            timings.Add((Stage, StageElapsed(now)));

        return timings;
    }

    public static float DownloadFraction(long received, long total)
    {
        return total <= 0 ? 0f : Math.Clamp((float) received / total, 0f, MaxDownloadFraction);
    }

    /// <summary>Average bytes per second over the stage so far.</summary>
    public static long DownloadSpeed(long received, TimeSpan elapsed)
    {
        return elapsed <= TimeSpan.Zero ? 0 : (long) (received / elapsed.TotalSeconds);
    }
}
