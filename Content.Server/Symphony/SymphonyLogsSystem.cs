using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using Content.Shared.Symphony;
using Robust.Server.ServerStatus;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Serilog.Events;

namespace Content.Server.Symphony;

/// <summary>
/// The server's own log, the lines its console prints, kept in memory so the SSymphony panel can show maintainers a live
/// tail without file access to the box. GET /symphony/logs?after=&lt;seq&gt; answers what came since; behind the admin API token.
/// </summary>
public sealed class SymphonyLogsSystem : EntitySystem
{
    /// <summary>Status host path that answers the latest log lines.</summary>
    public const string LogsPath = "/symphony/logs";

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILogManager _logs = default!;

    private readonly SymphonyLogBuffer _buffer = new();

    public override void Initialize()
    {
        base.Initialize();
        _logs.RootSawmill.AddHandler(_buffer);
        _statusHost.AddHandler(HandleAsync);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _logs.RootSawmill.RemoveHandler(_buffer);
    }

    // Runs on the status host's thread; the buffer is locked, so the game's main thread is never asked for anything.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        if (context.Url.AbsolutePath != LogsPath)
            return false;
        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;
        if (context.RequestMethod != HttpMethod.Get)
        {
            await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
            return true;
        }

        var query = HttpUtility.ParseQueryString(context.Url.Query);
        long.TryParse(query["after"], out var after);
        var limit = int.TryParse(query["limit"], out var l) ? Math.Clamp(l, 1, SymphonyLogBuffer.Capacity) : 1000;
        var (latest, oldest, entries) = _buffer.Since(after, limit);
        await context.RespondJsonAsync(new LogsResponse
        {
            SymphonyModule = SharedSymphony.ModuleVersion,
            Latest = latest,
            Oldest = oldest,
            Capacity = SymphonyLogBuffer.Capacity,
            Entries = entries.Select(e => new LogLine
            {
                Id = e.Id,
                Seq = e.Seq,
                Time = e.Time,
                Level = e.Level,
                Sawmill = e.Sawmill,
                Message = e.Message,
                Repeats = e.Repeats,
            }).ToList(),
        });
        return true;
    }

    private sealed class LogsResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("latest")] public long Latest { get; set; }
        [JsonPropertyName("oldest")] public long Oldest { get; set; }
        [JsonPropertyName("capacity")] public int Capacity { get; set; }
        [JsonPropertyName("entries")] public List<LogLine> Entries { get; set; } = new();
    }

    private sealed class LogLine
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("seq")] public long Seq { get; set; }
        [JsonPropertyName("time")] public DateTime Time { get; set; }
        [JsonPropertyName("level")] public string Level { get; set; } = "";
        [JsonPropertyName("sawmill")] public string Sawmill { get; set; } = "";
        [JsonPropertyName("message")] public string Message { get; set; } = "";
        [JsonPropertyName("repeats")] public int Repeats { get; set; }
    }
}

/// <summary>
/// The latest log lines, every level the server logs at. Each change gets the next sequence number, so a reader asks
/// for what came after the last one it saw. A line logged again straight after itself is counted on the same entry
/// (same id, new sequence number) rather than kept twice, so one error every tick does not push the rest out.
/// </summary>
public sealed class SymphonyLogBuffer : ILogHandler
{
    public const int Capacity = 5000;
    private const int MaxMessage = 8000;

    private readonly LinkedList<Entry> _entries = new();
    private long _seq;
    private long _id;

    public sealed record Entry(long Id, string Level, string Sawmill, string Message)
    {
        public long Seq;
        public DateTime Time;
        public int Repeats = 1;
    }

    // Called from any thread.
    public void Log(string sawmillName, LogEvent message)
    {
        var text = message.RenderMessage();
        // The panel asking for the log is not news in it: every poll would be a line.
        if (sawmillName == "statushost.http" && text.Contains(SymphonyLogsSystem.LogsPath))
            return;
        if (message.Exception != null)
            text += "\n" + message.Exception;
        if (text.Length > MaxMessage)
            text = text[..MaxMessage];
        var level = message.Level switch
        {
            LogEventLevel.Verbose => "verbose",
            LogEventLevel.Debug => "debug",
            LogEventLevel.Information => "info",
            LogEventLevel.Warning => "warning",
            LogEventLevel.Error => "error",
            _ => "fatal",
        };
        var time = message.Timestamp.UtcDateTime;
        lock (_entries)
        {
            if (_entries.Last?.Value is { } last && last.Sawmill == sawmillName && last.Message == text && last.Level == level)
            {
                last.Repeats++;
                last.Time = time;
                last.Seq = ++_seq;
                return;
            }

            if (_entries.Count >= Capacity)
                _entries.RemoveFirst();
            _entries.AddLast(new Entry(++_id, level, sawmillName, text) { Seq = ++_seq, Time = time });
        }
    }

    /// <summary>Lines changed after a sequence number, oldest first, at most <paramref name="limit"/> of the newest.</summary>
    public (long Latest, long Oldest, List<Entry> Entries) Since(long after, int limit)
    {
        lock (_entries)
        {
            var result = new List<Entry>();
            for (var node = _entries.Last; node != null && result.Count < limit; node = node.Previous)
            {
                if (node.Value.Seq <= after)
                    continue;
                result.Add(node.Value with { Seq = node.Value.Seq, Time = node.Value.Time, Repeats = node.Value.Repeats });
            }

            result.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            var oldest = _entries.First?.Value.Seq ?? 0;
            return (_seq, oldest, result);
        }
    }
}
