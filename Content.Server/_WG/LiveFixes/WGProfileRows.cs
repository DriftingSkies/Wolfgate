using Content.Server.Database;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.LiveFixes;

/// <summary>Builds a character's database rows so a save can't break their unique indexes.</summary>
public static class WGProfileRows
{
    /// <summary>Builds job rows, keeping the first high priority job and saving any others as medium.</summary>
    public static List<Job> Jobs(IEnumerable<KeyValuePair<ProtoId<JobPrototype>, JobPriority>> priorities)
    {
        var rows = new List<Job>();
        var hasHigh = false;

        foreach (var (job, priority) in priorities)
        {
            if (priority == JobPriority.Never)
                continue;

            var saved = priority == JobPriority.High && hasHigh ? JobPriority.Medium : priority;
            hasHigh |= saved == JobPriority.High;
            rows.Add(new Job { JobName = job, Priority = (DbJobPriority) saved });
        }

        return rows;
    }
}
