using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.Feedback;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

// Player bug reports and suggestions.
public abstract partial class ServerDbBase
{
    public async Task<int?> AddFeedbackReportAsync(WolfgateFeedbackReport report, int dailyLimit, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        if (dailyLimit > 0)
        {
            var since = report.CreatedAt - TimeSpan.FromDays(1);
            var today = await db.DbContext.WolfgateFeedbackReport
                .CountAsync(r => r.PlayerUserId == report.PlayerUserId && r.CreatedAt > since, cancel);

            if (today >= dailyLimit)
                return null;
        }

        db.DbContext.WolfgateFeedbackReport.Add(report);
        await db.DbContext.SaveChangesAsync(cancel);
        return report.Id;
    }

    public async Task<WolfgateFeedbackReport?> GetFeedbackReportAsync(int id, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        return await db.DbContext.WolfgateFeedbackReport
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, cancel);
    }

    public async Task<List<FeedbackReportSummary>> GetFeedbackReportsAsync(Guid userId, bool unseenOnly, int limit, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var rows = await db.DbContext.WolfgateFeedbackReport
            .Where(r => r.PlayerUserId == userId && (!unseenOnly || r.NotifyPlayer))
            .OrderByDescending(r => r.Id)
            .Take(limit)
            .Select(r => new { r.Id, r.Kind, r.Title, r.CreatedAt, r.Status, r.StaffMessage })
            .ToListAsync(cancel);

        return rows
            .Select(r => new FeedbackReportSummary(r.Id, r.Kind, r.Title, NormalizeDatabaseTime(r.CreatedAt), r.Status, r.StaffMessage))
            .ToList();
    }

    public async Task<Guid?> SetFeedbackStatusAsync(int id, string status, string? message, bool notify, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        // Asked for on its own, so the context and screenshot are never loaded to change four columns.
        var owner = await db.DbContext.WolfgateFeedbackReport
            .Where(r => r.Id == id)
            .Select(r => (Guid?) r.PlayerUserId)
            .SingleOrDefaultAsync(cancel);

        if (owner == null)
            return null;

        var now = DateTime.UtcNow;
        await db.DbContext.WolfgateFeedbackReport
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.StaffMessage, message)
                .SetProperty(r => r.StatusChangedAt, (DateTime?) now)
                .SetProperty(r => r.NotifyPlayer, notify), cancel);

        return owner;
    }

    public async Task MarkFeedbackSeenAsync(Guid userId, int[] ids, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        await db.DbContext.WolfgateFeedbackReport
            .Where(r => r.PlayerUserId == userId && r.NotifyPlayer && ids.Contains(r.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.NotifyPlayer, false), cancel);
    }
}
