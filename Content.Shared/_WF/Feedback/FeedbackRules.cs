namespace Content.Shared._WF.Feedback;

/// <summary>The limits a report is held to, checked by the window before sending and by the server on arrival.</summary>
public static class FeedbackRules
{
    public const int MinTitle = 5;
    public const int MaxTitle = 100;
    public const int MinDescription = 15;
    public const int MaxText = 4000;

    /// <summary>Most bytes of screenshot the server keeps; a larger one is dropped and the report kept.</summary>
    public const int MaxScreenshotBytes = 768 * 1024;

    /// <summary>The longest side of a screenshot the client sends.</summary>
    public const int MaxScreenshotSide = 1600;

    public const int MaxClientInfoEntries = 64;
    public const int MaxClientInfoKey = 48;
    public const int MaxClientInfoValue = 512;
    public const int MaxClientLogLines = 60;
    public const int MaxClientLogLine = 800;

    /// <summary>Longest message staff can attach to a status.</summary>
    public const int MaxStaffMessage = 1000;

    /// <summary>Most of a player's own reports their list shows.</summary>
    public const int MaxHistory = 50;

    public const string DefaultCategory = "other";

    /// <summary>The top of the survey's enjoyment scale.</summary>
    public const int MaxRating = 5;

    /// <summary>The kinds as the database and the panel write them, in <see cref="FeedbackKind"/> order.</summary>
    public static readonly string[] Kinds = { "bug", "suggestion", "survey" };

    /// <summary>The statuses as the database and the panel write them, in <see cref="FeedbackStatus"/> order.</summary>
    public static readonly string[] Statuses = { "pending", "checked", "fixed", "non_issue", "accepted", "declined", "read", "resolved" };

    /// <summary>The areas a report can be filed under. Each has a wf-feedback-category-* string.</summary>
    public static readonly string[] Categories =
    {
        "gameplay",
        "ships",
        "combat",
        "medical",
        "interface",
        "map",
        "visual",
        "audio",
        "performance",
        DefaultCategory,
    };

    /// <summary>The kind as the database holds it.</summary>
    public static string KindId(FeedbackKind kind)
    {
        return Kinds[(int) kind];
    }

    /// <summary>Reads a stored kind; a word this build doesn't know reads as a suggestion.</summary>
    public static FeedbackKind ParseKind(string? id)
    {
        var index = Array.IndexOf(Kinds, id);
        return index < 0 ? FeedbackKind.Suggestion : (FeedbackKind) index;
    }

    /// <summary>The status as the database holds it.</summary>
    public static string StatusId(FeedbackStatus status)
    {
        return Statuses[(int) status];
    }

    /// <summary>Reads a stored status. False for a word this build doesn't know.</summary>
    public static bool TryParseStatus(string? id, out FeedbackStatus status)
    {
        var index = Array.IndexOf(Statuses, id);
        status = index < 0 ? FeedbackStatus.Pending : (FeedbackStatus) index;
        return index >= 0;
    }

    /// <summary>The Fluent id of what is wrong with the report's text, or null when it may be sent.</summary>
    public static string? Validate(FeedbackSubmitEvent report)
    {
        // The client builds this event, so any field may arrive null.
        if (report.Category == null || report.Title == null || report.Description == null || report.Steps == null
            || report.Expected == null || report.Frustrations == null || report.ClientInfo == null || report.ClientLog == null)
        {
            return "wf-feedback-error-invalid";
        }

        if (!Enum.IsDefined(report.Kind) || !Enum.IsDefined(report.Severity) || Array.IndexOf(Categories, report.Category) < 0)
            return "wf-feedback-error-invalid";

        // A survey has no title; the rating is the one answer it needs.
        if (report.Kind == FeedbackKind.Survey)
        {
            if (report.Rating < 1 || report.Rating > MaxRating || !Enum.IsDefined(report.Trigger))
                return "wf-feedback-error-rating";
            if (report.Description.Length > MaxText || report.Frustrations.Length > MaxText)
                return "wf-feedback-error-text-long";
            return null;
        }

        var title = report.Title.Trim().Length;
        if (title < MinTitle)
            return "wf-feedback-error-title-short";
        if (title > MaxTitle)
            return "wf-feedback-error-title-long";

        if (report.Description.Trim().Length < MinDescription)
            return "wf-feedback-error-description-short";
        if (report.Description.Length > MaxText || report.Steps.Length > MaxText || report.Expected.Length > MaxText)
            return "wf-feedback-error-text-long";

        return null;
    }
}
