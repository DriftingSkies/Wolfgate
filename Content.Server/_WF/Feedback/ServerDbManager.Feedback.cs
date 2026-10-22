using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.Feedback;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>
    /// Stores a bug report or suggestion and returns its number, or null when the player has already sent
    /// <paramref name="dailyLimit"/> in the day before it. 0 for no limit.
    /// </summary>
    Task<int?> AddFeedbackReportAsync(WolfgateFeedbackReport report, int dailyLimit, CancellationToken cancel = default);

    /// <summary>The report with this number, context, screenshot and all, or null.</summary>
    Task<WolfgateFeedbackReport?> GetFeedbackReportAsync(int id, CancellationToken cancel = default);

    /// <summary>A player's own reports, newest first.</summary>
    /// <param name="unseenOnly">Only those with a status the player has yet to be told of.</param>
    Task<List<FeedbackReportSummary>> GetFeedbackReportsAsync(Guid userId, bool unseenOnly, int limit, CancellationToken cancel = default);

    /// <summary>
    /// Sets a report's status and the message staff have for the player, replacing the last one. Returns who sent
    /// the report, or null when no report has this number.
    /// </summary>
    /// <param name="notify">Whether the player is to be told.</param>
    Task<Guid?> SetFeedbackStatusAsync(int id, string status, string? message, bool notify, CancellationToken cancel = default);

    /// <summary>Notes that the player has seen the status of these reports of theirs.</summary>
    Task MarkFeedbackSeenAsync(Guid userId, int[] ids, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<int?> AddFeedbackReportAsync(WolfgateFeedbackReport report, int dailyLimit, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddFeedbackReportAsync(report, dailyLimit, cancel));
    }

    public Task<WolfgateFeedbackReport?> GetFeedbackReportAsync(int id, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetFeedbackReportAsync(id, cancel));
    }

    public Task<List<FeedbackReportSummary>> GetFeedbackReportsAsync(Guid userId, bool unseenOnly, int limit, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetFeedbackReportsAsync(userId, unseenOnly, limit, cancel));
    }

    public Task<Guid?> SetFeedbackStatusAsync(int id, string status, string? message, bool notify, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetFeedbackStatusAsync(id, status, message, notify, cancel));
    }

    public Task MarkFeedbackSeenAsync(Guid userId, int[] ids, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.MarkFeedbackSeenAsync(userId, ids, cancel));
    }
}
