using System.Diagnostics;

namespace Content.Server._WG.Performance;

/// <summary>
/// Charges each client message the time its handlers took, from timestamps taken after each of its two raises.
/// </summary>
/// <remarks>
/// The engine raises a client message twice, plain then wrapped with its sender, and a hook added after the engine's
/// own sees each raise only once its handlers have run. A message's time is therefore from the end of the message
/// before it to the end of its own wrapped raise. The first message of a tick has nothing before it, so it is charged
/// at the end of the tick with whatever of the tick's EntityNet time the others didn't take.
/// </remarks>
public sealed class WGDispatchClock
{
    /// <summary>Receives one message's charge.</summary>
    public delegate void Charge(string label, double seconds, object message, object? wrapper);

    private readonly double _secondsPerTick;

    private string? _label;
    private object? _message;

    private bool _any;
    private long _end;
    private string _firstLabel = string.Empty;
    private object _firstMessage = default!;
    private object? _firstWrapper;
    private long _firstEnd;

    public WGDispatchClock(long frequency = 0)
    {
        _secondsPerTick = 1.0 / (frequency > 0 ? frequency : Stopwatch.Frequency);
    }

    /// <summary>A message's plain raise has run.</summary>
    public void Raised(string label, object message)
    {
        _label = label;
        _message = message;
    }

    /// <summary>A message's wrapped raise has run at <paramref name="now"/>, so the message is done.</summary>
    public void Finished(long now, object? wrapper, Charge charge)
    {
        // A handler that threw skips the wrapped raise; its time then goes to the next message.
        if (_label is not { } label || _message is not { } message)
            return;

        _label = null;
        _message = null;

        if (!_any)
        {
            _any = true;
            _firstLabel = label;
            _firstMessage = message;
            _firstWrapper = wrapper;
            _firstEnd = now;
            _end = now;
            return;
        }

        charge(label, (now - _end) * _secondsPerTick, message, wrapper);
        _end = now;
    }

    /// <summary>
    /// The tick's dispatch is over. Charges its first message with <paramref name="entityNetSeconds"/> less what the
    /// rest took, or drops it when the total is unknown.
    /// </summary>
    public void EndTick(double? entityNetSeconds, Charge charge)
    {
        _label = null;
        _message = null;
        if (!_any)
            return;

        _any = false;
        if (entityNetSeconds is { } total)
            charge(_firstLabel, Math.Max(0.0, total - (_end - _firstEnd) * _secondsPerTick), _firstMessage, _firstWrapper);

        _firstMessage = default!;
        _firstWrapper = null;
    }
}
