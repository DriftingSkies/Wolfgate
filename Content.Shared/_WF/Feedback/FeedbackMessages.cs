using Robust.Shared.Serialization;

namespace Content.Shared._WF.Feedback;

/// <summary>What a player is sending in.</summary>
[Serializable, NetSerializable]
public enum FeedbackKind : byte
{
    Bug,
    Suggestion,
    Survey,
}

/// <summary>What brought up the end of shift survey.</summary>
[Serializable, NetSerializable]
public enum FeedbackSurveyTrigger : byte
{
    None,
    Cryo,
    Leaving,
}

/// <summary>How bad the player says a bug is.</summary>
[Serializable, NetSerializable]
public enum FeedbackSeverity : byte
{
    Minor,
    Major,
    Blocking,
}

/// <summary>Where staff have got to with a report.</summary>
[Serializable, NetSerializable]
public enum FeedbackStatus : byte
{
    Pending,
    Checked,
    Fixed,
    NonIssue,
    // For suggestions: whether it is going in.
    Accepted,
    Declined,
    // For shift surveys: staff have read it, or acted on it.
    Read,
    Resolved,
}

/// <summary>A bug report or suggestion, sent by the client when the player presses Submit.</summary>
[Serializable, NetSerializable]
public sealed class FeedbackSubmitEvent : EntityEventArgs
{
    public FeedbackKind Kind;

    /// <summary>One of <see cref="FeedbackRules.Categories"/>.</summary>
    public string Category = FeedbackRules.DefaultCategory;

    /// <summary>Bugs only.</summary>
    public FeedbackSeverity Severity;

    public string Title = string.Empty;
    public string Description = string.Empty;

    /// <summary>Bugs only: how to make it happen again.</summary>
    public string Steps = string.Empty;

    /// <summary>Bugs only: what the player expected instead.</summary>
    public string Expected = string.Empty;

    /// <summary>Surveys only: how much the player enjoyed the shift, 1 to <see cref="FeedbackRules.MaxRating"/>.</summary>
    public int Rating;

    /// <summary>Surveys only: what frustrated the player. The issues they hit go in <see cref="Description"/>.</summary>
    public string Frustrations = string.Empty;

    /// <summary>Surveys only: what offered the survey.</summary>
    public FeedbackSurveyTrigger Trigger;

    /// <summary>What the client knows about itself: build, window, frame rate, open windows. Never trusted.</summary>
    public Dictionary<string, string> ClientInfo = new();

    /// <summary>The client's recent warnings and errors, oldest first.</summary>
    public List<string> ClientLog = new();

    /// <summary>A JPEG or PNG of the game as it was when the window opened, or null.</summary>
    public byte[]? Screenshot;
}

/// <summary>The server's answer to a <see cref="FeedbackSubmitEvent"/>.</summary>
[Serializable, NetSerializable]
public sealed class FeedbackResultEvent : EntityEventArgs
{
    /// <summary>The stored report's number, or 0 when it was refused.</summary>
    public int ReportId;

    /// <summary>Fluent id of why it was refused, or null.</summary>
    public string? Error;

    /// <summary>Seconds until the player may send again, for the cooldown refusal.</summary>
    public int RetrySeconds;
}

/// <summary>One of a player's own reports, as their list and the status notice show it.</summary>
[Serializable, NetSerializable]
public sealed class FeedbackEntry
{
    public int Id;
    public FeedbackKind Kind;
    public string Title = string.Empty;
    public DateTime CreatedAt;
    public FeedbackStatus Status;

    /// <summary>What staff wrote to the player about it, or null.</summary>
    public string? StaffMessage;
}

/// <summary>Asks the server for the player's own reports.</summary>
[Serializable, NetSerializable]
public sealed class FeedbackHistoryRequestEvent : EntityEventArgs;

/// <summary>The player's own reports, newest first.</summary>
[Serializable, NetSerializable]
public sealed class FeedbackHistoryEvent : EntityEventArgs
{
    public List<FeedbackEntry> Reports = new();
}

/// <summary>Reports of the player's that staff have moved on since the player last looked.</summary>
[Serializable, NetSerializable]
public sealed class FeedbackNoticeEvent : EntityEventArgs
{
    public List<FeedbackEntry> Reports = new();
}

/// <summary>Sent when the player has closed the notice, so the server stops showing these.</summary>
[Serializable, NetSerializable]
public sealed class FeedbackNoticeSeenEvent : EntityEventArgs
{
    public List<int> Ids = new();
}

/// <summary>Offers the player the end of shift survey: sent by the server when they go into cryo.</summary>
[Serializable, NetSerializable]
public sealed class FeedbackSurveyPromptEvent : EntityEventArgs
{
    public FeedbackSurveyTrigger Trigger;
}
