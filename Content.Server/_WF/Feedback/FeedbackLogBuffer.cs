using Robust.Shared.Log;
using Serilog.Events;

namespace Content.Server._WF.Feedback;

/// <summary>
/// Keeps the server's latest warnings and errors, so a report can carry what went wrong just before it. A line
/// that repeats is counted rather than kept again, so an error logged every tick doesn't push the rest out.
/// </summary>
public sealed class FeedbackLogBuffer : ILogHandler
{
    private const int Capacity = 120;
    private const int MaxMessage = 4000;

    private readonly LinkedList<Entry> _entries = new();

    /// <summary>One kept line: when it was last logged and how many times in a row.</summary>
    public sealed record Entry(string Level, string Sawmill, string Message)
    {
        public DateTime Time;
        public int Repeats = 1;
    }

    // Called from any thread.
    public void Log(string sawmillName, LogEvent message)
    {
        if (message.Level < LogEventLevel.Warning)
            return;

        var text = message.RenderMessage();
        if (message.Exception != null)
            text += "\n" + message.Exception;
        if (text.Length > MaxMessage)
            text = text[..MaxMessage];

        var level = message.Level.ToString().ToLowerInvariant();
        var time = message.Timestamp.UtcDateTime;
        lock (_entries)
        {
            if (_entries.Last?.Value is { } last && last.Sawmill == sawmillName && last.Message == text)
            {
                last.Repeats++;
                last.Time = time;
                return;
            }

            if (_entries.Count >= Capacity)
                _entries.RemoveFirst();
            _entries.AddLast(new Entry(level, sawmillName, text) { Time = time });
        }
    }

    /// <summary>The newest lines, oldest first.</summary>
    public List<Entry> Latest(int count)
    {
        lock (_entries)
        {
            var result = new List<Entry>(Math.Min(count, _entries.Count));
            for (var node = _entries.Last; node != null && result.Count < count; node = node.Previous)
            {
                result.Add(node.Value with { });
            }

            result.Reverse();
            return result;
        }
    }
}
