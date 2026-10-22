namespace Content.Server._WF.Feedback;

/// <summary>A stored report without its context or screenshot: what the player's own list and notices need.</summary>
public sealed record FeedbackReportSummary(
    int Id,
    string Kind,
    string Title,
    DateTime CreatedAt,
    string Status,
    string? StaffMessage);
