using Content.Client.Message;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Feedback;

/// <summary>The small prompt that offers the end of shift survey: give feedback, not now, or never again.</summary>
public sealed class FeedbackSurveyChip : PanelContainer
{
    /// <summary>The player wants to fill in the survey.</summary>
    public event Action? Accepted;

    /// <summary>The player said not now, or leave now.</summary>
    public event Action? Dismissed;

    /// <summary>The player never wants to be asked again.</summary>
    public event Action? Never;

    public FeedbackSurveyChip(bool leaving)
    {
        MaxWidth = 520;
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#1b1f26").WithAlpha(0.95f),
            BorderColor = Color.FromHex("#4a6a8a"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 8,
            ContentMarginBottomOverride = 8,
        };

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        AddChild(box);

        var text = new RichTextLabel();
        text.SetMarkup(Loc.GetString(leaving ? "wf-feedback-survey-chip-leaving" : "wf-feedback-survey-chip"));
        box.AddChild(text);

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        box.AddChild(buttons);

        var give = new Button { Text = Loc.GetString("wf-feedback-survey-chip-give") };
        give.OnPressed += _ => Accept();
        buttons.AddChild(give);

        var later = new Button { Text = Loc.GetString(leaving ? "wf-feedback-survey-chip-leave" : "wf-feedback-survey-chip-later") };
        later.OnPressed += _ => Dismiss();
        buttons.AddChild(later);

        buttons.AddChild(new Control { HorizontalExpand = true });

        var never = new Button { Text = Loc.GetString("wf-feedback-survey-chip-never"), StyleClasses = { "ButtonSmall" } };
        never.OnPressed += _ => NeverAgain();
        buttons.AddChild(never);
    }

    /// <summary>As the Give feedback button.</summary>
    public void Accept() => Accepted?.Invoke();

    /// <summary>As the Not now or Leave now button.</summary>
    public void Dismiss() => Dismissed?.Invoke();

    /// <summary>As the Never show again button.</summary>
    public void NeverAgain() => Never?.Invoke();
}
