using System.Globalization;
using Content.Client.Message;
using Content.Shared._WF.Feedback;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Feedback;

/// <summary>One of the player's reports: its number, date and status, its title, and what staff wrote back.</summary>
public sealed class FeedbackEntryRow : BoxContainer
{
    public FeedbackEntryRow(FeedbackEntry entry)
    {
        Orientation = LayoutOrientation.Vertical;
        Margin = new Thickness(0, 0, 0, 8);

        var header = new RichTextLabel();
        header.SetMarkup(Loc.GetString("wf-feedback-entry-header",
            ("id", entry.Id.ToString()),
            ("kind", Loc.GetString($"wf-feedback-entry-kind-{FeedbackRules.KindId(entry.Kind)}")),
            ("date", entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("color", StatusColor(entry.Status)),
            ("status", Loc.GetString($"wf-feedback-status-{entry.Status.ToString().ToLowerInvariant()}"))));
        AddChild(header);

        var title = new RichTextLabel();
        title.SetMarkup(Loc.GetString("wf-feedback-entry-title", ("title", FormattedMessage.EscapeText(entry.Title))));
        AddChild(title);

        if (entry.StaffMessage is not { } message)
            return;

        var staff = new RichTextLabel();
        staff.SetMarkup(Loc.GetString("wf-feedback-entry-staff", ("message", FormattedMessage.EscapeText(message))));
        AddChild(staff);
    }

    private static string StatusColor(FeedbackStatus status)
    {
        return status switch
        {
            FeedbackStatus.Checked => "#e5c07b",
            FeedbackStatus.Fixed => "#7fd17f",
            FeedbackStatus.NonIssue => "#c08080",
            FeedbackStatus.Accepted => "#7fd17f",
            FeedbackStatus.Declined => "#c08080",
            FeedbackStatus.Read => "#8fb7e0",
            FeedbackStatus.Resolved => "#7fd17f",
            _ => "#999999",
        };
    }
}
