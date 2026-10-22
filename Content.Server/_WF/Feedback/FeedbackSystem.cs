using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server._NF.CryoSleep;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Feedback;
using Content.Shared.GameTicking;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Feedback;

/// <summary>
/// Takes the bug reports and suggestions players send from the feedback window and stores each with a snapshot of
/// the moment (see FeedbackSystem.Context.cs), in the table the SSymphony panel reads. Shows players their own
/// reports, and tells them when staff move one on: at once if they are online, otherwise when they next log in.
/// Offers the end of shift survey to a player going into cryo; the client offers it again when they leave.
/// </summary>
public sealed partial class FeedbackSystem : EntitySystem
{
    private const int AdminLogLines = 40;
    private const int MaxNotices = 20;
    private static readonly TimeSpan HistoryInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logs = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IAdminLogManager _adminLogs = default!;

    private readonly FeedbackLogBuffer _buffer = new();
    // When each player last sent a report, for the cooldown.
    private readonly Dictionary<NetUserId, TimeSpan> _lastSent = new();
    private readonly HashSet<NetUserId> _sending = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextHistory = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextSeen = new();
    // Who has sent a survey this round: one each.
    private readonly HashSet<NetUserId> _surveyed = new();

    public override void Initialize()
    {
        base.Initialize();
        _logs.RootSawmill.AddHandler(_buffer);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        SubscribeNetworkEvent<FeedbackSubmitEvent>(OnSubmit);
        SubscribeNetworkEvent<FeedbackHistoryRequestEvent>(OnHistoryRequest);
        SubscribeNetworkEvent<FeedbackNoticeSeenEvent>(OnNoticeSeen);
        SubscribeLocalEvent<CryosleepEnterEvent>(OnCryosleepEnter);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _lastSent.Clear();
            _nextHistory.Clear();
            _nextSeen.Clear();
            _surveyed.Clear();
        });
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        _logs.RootSawmill.RemoveHandler(_buffer);
        base.Shutdown();
    }

    /// <summary>
    /// Sets a report's status and the message staff have for its player, and tells the player if asked to. Null
    /// when no report has this number; otherwise whether the player is in game, told or not.
    /// </summary>
    public async Task<bool?> SetStatusAsync(int id, FeedbackStatus status, string? message, bool notify)
    {
        var text = NullIfEmpty(Clean(message, FeedbackRules.MaxStaffMessage));
        if (await _db.SetFeedbackStatusAsync(id, FeedbackRules.StatusId(status), text, notify) is not { } owner)
            return null;

        return notify ? NotifyPlayer(owner) : InGame(owner, out _);
    }

    /// <summary>
    /// Shows a player the reports of theirs that staff have moved on, if they are online. For after the panel has
    /// written the rows itself. Returns whether they were online.
    /// </summary>
    public bool NotifyPlayer(Guid user)
    {
        if (!InGame(user, out var session))
            return false;

        SendNotices(session);
        return true;
    }

    private bool InGame(Guid user, [NotNullWhen(true)] out ICommonSession? session)
    {
        return _players.TryGetSessionById(new NetUserId(user), out session) && session.Status == SessionStatus.InGame;
    }

    /// <summary>Whether the player has already sent a survey this round.</summary>
    public bool HasSurveyed(NetUserId user)
    {
        return _surveyed.Contains(user);
    }

    private void OnCryosleepEnter(CryosleepEnterEvent ev)
    {
        if (ev.User is not { } user
            || !_cfg.GetCVar(FeedbackCVars.Enabled)
            || !_cfg.GetCVar(FeedbackCVars.Surveys)
            || _surveyed.Contains(user)
            || !_players.TryGetSessionById(user, out var session)
            || session.Status != SessionStatus.InGame)
        {
            return;
        }

        RaiseNetworkEvent(new FeedbackSurveyPromptEvent { Trigger = FeedbackSurveyTrigger.Cryo }, session);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame)
            SendNotices(args.Session);
    }

    private async void SendNotices(ICommonSession session)
    {
        try
        {
            var reports = await _db.GetFeedbackReportsAsync(session.UserId, true, MaxNotices);
            if (reports.Count == 0 || session.Status != SessionStatus.InGame)
                return;

            RaiseNetworkEvent(new FeedbackNoticeEvent { Reports = reports.Select(ToEntry).ToList() }, session);
        }
        catch (Exception e)
        {
            Log.Error($"Could not read the report notices of {session.Name}: {e}");
        }
    }

    private async void OnHistoryRequest(FeedbackHistoryRequestEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var now = _timing.RealTime;
        if (_nextHistory.GetValueOrDefault(session.UserId) > now)
            return;

        _nextHistory[session.UserId] = now + HistoryInterval;
        try
        {
            var reports = await _db.GetFeedbackReportsAsync(session.UserId, false, FeedbackRules.MaxHistory);
            if (session.Status != SessionStatus.Disconnected)
                RaiseNetworkEvent(new FeedbackHistoryEvent { Reports = reports.Select(ToEntry).ToList() }, session);
        }
        catch (Exception e)
        {
            Log.Error($"Could not read the reports of {session.Name}: {e}");
        }
    }

    private async void OnNoticeSeen(FeedbackNoticeSeenEvent ev, EntitySessionEventArgs args)
    {
        if (ev.Ids is not { Count: > 0 and <= MaxNotices })
            return;

        // One write a second at most, however often a client sends this.
        var user = args.SenderSession.UserId;
        var now = _timing.RealTime;
        if (_nextSeen.GetValueOrDefault(user) > now)
            return;

        _nextSeen[user] = now + HistoryInterval;

        try
        {
            await _db.MarkFeedbackSeenAsync(args.SenderSession.UserId, ev.Ids.ToArray());
        }
        catch (Exception e)
        {
            Log.Error($"Could not mark the report notices of {args.SenderSession.Name} seen: {e}");
        }
    }

    private static FeedbackEntry ToEntry(FeedbackReportSummary report)
    {
        // A status this build doesn't know reads as pending.
        FeedbackRules.TryParseStatus(report.Status, out var status);
        return new FeedbackEntry
        {
            Id = report.Id,
            Kind = FeedbackRules.ParseKind(report.Kind),
            Title = report.Title,
            CreatedAt = report.CreatedAt,
            Status = status,
            // The panel may have written the row itself, so the message is cut here too.
            StaffMessage = NullIfEmpty(Clean(report.StaffMessage, FeedbackRules.MaxStaffMessage)),
        };
    }

    private async void OnSubmit(FeedbackSubmitEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var user = session.UserId;

        if (!_cfg.GetCVar(FeedbackCVars.Enabled))
        {
            Answer(session, 0, "wf-feedback-error-disabled");
            return;
        }

        if (FeedbackRules.Validate(ev) is { } problem)
        {
            Answer(session, 0, problem);
            return;
        }

        var survey = ev.Kind == FeedbackKind.Survey;
        if (survey && (!_cfg.GetCVar(FeedbackCVars.Surveys) || _surveyed.Contains(user)))
        {
            Answer(session, 0, _surveyed.Contains(user) ? "wf-feedback-error-survey-done" : "wf-feedback-error-disabled");
            return;
        }

        // A survey comes when the player is leaving, so the cooldown between reports doesn't hold it up.
        var wait = !survey && _lastSent.TryGetValue(user, out var last)
            ? last + TimeSpan.FromSeconds(_cfg.GetCVar(FeedbackCVars.Cooldown)) - _timing.RealTime
            : TimeSpan.Zero;
        if (wait > TimeSpan.Zero || !_sending.Add(user))
        {
            Answer(session, 0, "wf-feedback-error-cooldown", Math.Max(1, (int) Math.Ceiling(wait.TotalSeconds)));
            return;
        }

        try
        {
            // Everything read from the game is read here, before the first await, while the moment still stands.
            var screenshot = AcceptScreenshot(ev.Screenshot);
            var context = BuildContext(session, ev, screenshot != null);
            var report = new WolfgateFeedbackReport
            {
                Kind = FeedbackRules.KindId(ev.Kind),
                Category = survey ? FeedbackRules.DefaultCategory : ev.Category,
                Severity = ev.Kind == FeedbackKind.Bug ? ev.Severity.ToString().ToLowerInvariant() : null,
                Title = survey
                    ? Loc.GetString("wf-feedback-survey-stored-title", ("rating", ev.Rating), ("max", FeedbackRules.MaxRating))
                    : Clean(ev.Title, FeedbackRules.MaxTitle, false),
                Description = Clean(ev.Description, FeedbackRules.MaxText),
                Steps = ev.Kind == FeedbackKind.Bug ? NullIfEmpty(Clean(ev.Steps, FeedbackRules.MaxText)) : null,
                Expected = ev.Kind == FeedbackKind.Bug ? NullIfEmpty(Clean(ev.Expected, FeedbackRules.MaxText)) : null,
                Rating = survey ? ev.Rating : null,
                Frustrations = survey ? NullIfEmpty(Clean(ev.Frustrations, FeedbackRules.MaxText)) : null,
                PlayerUserId = user.UserId,
                PlayerName = Clean(session.Name, 64, false),
                CreatedAt = DateTime.UtcNow,
                RoundId = _ticker.RoundId,
                ServerName = Clean(_cfg.GetCVar(CVars.GameHostName), 128, false),
                BuildVersion = Clean(_cfg.GetCVar(CVars.BuildVersion), 128, false),
                Screenshot = screenshot,
                Status = FeedbackRules.StatusId(FeedbackStatus.Pending),
            };

            if (survey)
                context["survey"] = new JsonObject { ["trigger"] = ev.Trigger.ToString().ToLowerInvariant(), ["rating"] = ev.Rating };

            context["admin_log"] = await AdminLogAsync(user);
            report.Context = context.ToJsonString();

            var id = await _db.AddFeedbackReportAsync(report, _cfg.GetCVar(FeedbackCVars.DailyLimit));
            if (id == null)
            {
                Answer(session, 0, "wf-feedback-error-daily");
                return;
            }

            if (survey)
                _surveyed.Add(user);
            else
                _lastSent[user] = _timing.RealTime;
            Log.Info($"{session.Name} sent {report.Kind} report #{id}: {report.Title}");
            Answer(session, id.Value, null);

            if (_cfg.GetCVar(FeedbackCVars.AdminNotice))
            {
                _chat.SendAdminAnnouncement(Loc.GetString("wf-feedback-admin-notice",
                    ("player", session.Name),
                    ("kind", Loc.GetString($"wf-feedback-kind-{report.Kind}")),
                    ("id", id.Value.ToString()),
                    ("title", FormattedMessage.EscapeText(report.Title))));
            }
        }
        catch (Exception e)
        {
            Log.Error($"Could not store a report from {session.Name}: {e}");
            Answer(session, 0, "wf-feedback-error-failed");
        }
        finally
        {
            _sending.Remove(user);
        }
    }

    private void Answer(ICommonSession session, int id, string? error, int retrySeconds = 0)
    {
        // The player may have left while the report was being stored.
        if (session.Status == SessionStatus.Disconnected)
            return;

        RaiseNetworkEvent(new FeedbackResultEvent { ReportId = id, Error = error, RetrySeconds = retrySeconds }, session);
    }

    /// <summary>The screenshot when screenshots are on and it is a JPEG or PNG of a size the server keeps.</summary>
    private byte[]? AcceptScreenshot(byte[]? data)
    {
        if (data == null || data.Length < 8 || data.Length > FeedbackRules.MaxScreenshotBytes || !_cfg.GetCVar(FeedbackCVars.Screenshots))
            return null;

        var jpeg = data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;
        var png = data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
        return jpeg || png ? data : null;
    }

    /// <summary>What the admin log holds on this player this round, oldest first.</summary>
    private async Task<JsonArray> AdminLogAsync(NetUserId user)
    {
        var lines = new JsonArray();
        try
        {
            var logs = await _adminLogs.CurrentRoundLogs(new LogFilter { AnyPlayers = new[] { user.UserId }, Limit = AdminLogLines });
            logs.Sort((a, b) => a.Date.CompareTo(b.Date));
            foreach (var log in logs)
            {
                lines.Add(new JsonObject
                {
                    ["time_utc"] = log.Date.ToUniversalTime().ToString("o"),
                    ["type"] = log.Type.ToString(),
                    ["impact"] = log.Impact.ToString(),
                    ["message"] = Clean(log.Message, 1000),
                });
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Could not read the admin log for a report: {e.Message}");
        }

        return lines;
    }

    private static string? NullIfEmpty(string text)
    {
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Text fit to store: trimmed, cut to length, and without the characters PostgreSQL refuses (NUL, lone
    /// surrogates) or that have no place in a report (other control characters).
    /// </summary>
    private static string Clean(string? text, int max, bool multiline = true)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(Math.Min(text.Length, max));
        for (var i = 0; i < text.Length && builder.Length < max; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) && builder.Length + 2 <= max)
                    builder.Append(c).Append(text[++i]);
                continue;
            }

            if (char.IsLowSurrogate(c) || c == '\r')
                continue;

            if (c is '\n' or '\t')
                builder.Append(multiline ? c : ' ');
            else if (!char.IsControl(c))
                builder.Append(c);
        }

        return builder.ToString().Trim();
    }
}
