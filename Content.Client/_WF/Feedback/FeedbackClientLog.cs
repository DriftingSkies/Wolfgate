using Content.Shared._WF.Feedback;
using Robust.Client.Console;

namespace Content.Client._WF.Feedback;

/// <summary>
/// Keeps the client's latest warnings and errors for a bug report. The sandbox keeps content away from the log
/// handlers, so this reads the lines the engine writes to the debug console instead.
/// </summary>
public sealed class FeedbackClientLog
{
    private readonly Queue<string> _lines = new();

    // Raised from whichever thread logged.
    public void OnLine(object? sender, AddFormattedMessageArgs args)
    {
        var text = args.Message.ToString();
        if (!text.StartsWith("[WARN]") && !text.StartsWith("[ERRO]") && !text.StartsWith("[FATL]"))
            return;

        if (text.Length > FeedbackRules.MaxClientLogLine - 16)
            text = text[..(FeedbackRules.MaxClientLogLine - 16)];

        var line = $"{DateTime.UtcNow:HH:mm:ss}Z {text}";
        lock (_lines)
        {
            if (_lines.Count >= FeedbackRules.MaxClientLogLines)
                _lines.Dequeue();
            _lines.Enqueue(line);
        }
    }

    /// <summary>The kept lines, oldest first.</summary>
    public List<string> Snapshot()
    {
        lock (_lines)
        {
            return new List<string>(_lines);
        }
    }
}
