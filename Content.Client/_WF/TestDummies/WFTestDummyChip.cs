using Content.Client.Message;
using Content.Shared._WF.TestDummies;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.TestDummies;

/// <summary>
/// The bottom-right prompt asking the player to be a test subject: what is being tested, the no-compensation
/// disclaimer, a countdown and yes/no buttons.
/// </summary>
public sealed class WFTestDummyChip : PanelContainer
{
    /// <summary>Raised once, with whether the player said yes.</summary>
    public event Action<bool>? Answered;

    public readonly int CallId;

    /// <summary>When the prompt runs out, or zero for never. The system takes it down then.</summary>
    public readonly TimeSpan Until;

    private readonly Label? _countdown;
    private readonly IGameTiming _timing;
    private bool _done;

    public WFTestDummyChip(WFTestDummyPromptEvent ev, IGameTiming timing)
    {
        CallId = ev.CallId;
        _timing = timing;
        Until = ev.Duration > 0 ? timing.RealTime + TimeSpan.FromSeconds(ev.Duration) : TimeSpan.Zero;

        MaxWidth = 420;
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#1b1f26").WithAlpha(0.95f),
            BorderColor = Color.FromHex("#c08a2a"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 8,
            ContentMarginBottomOverride = 8,
        };

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        AddChild(box);

        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        box.AddChild(header);
        header.AddChild(new Label
        {
            Text = Loc.GetString("wf-test-dummy-chip-title"),
            StyleClasses = { "LabelHeading" },
            HorizontalExpand = true,
        });
        if (Until != TimeSpan.Zero)
        {
            _countdown = new Label { StyleClasses = { "LabelSubText" } };
            header.AddChild(_countdown);
        }

        var text = new RichTextLabel();
        // The description is admin-typed text, so it must not be parsed as markup.
        var key = ev.Description.Length > 0 ? "wf-test-dummy-chip-text" : "wf-test-dummy-chip-text-plain";
        text.SetMarkup(Loc.GetString(key, ("description", FormattedMessage.EscapeText(ev.Description))));
        box.AddChild(text);

        var disclaimer = new RichTextLabel();
        disclaimer.SetMarkup(Loc.GetString("wf-test-dummy-chip-disclaimer"));
        box.AddChild(disclaimer);

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        box.AddChild(buttons);

        var yes = new Button { Text = Loc.GetString("wf-test-dummy-chip-yes"), HorizontalExpand = true };
        yes.OnPressed += _ => Answer(true);
        buttons.AddChild(yes);

        var no = new Button { Text = Loc.GetString("wf-test-dummy-chip-no"), HorizontalExpand = true };
        no.OnPressed += _ => Answer(false);
        buttons.AddChild(no);

        UpdateCountdown();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        if (_countdown == null || _done)
            return;

        var left = Until - _timing.RealTime;
        if (left <= TimeSpan.Zero)
            return;

        _countdown.Text = Loc.GetString("wf-test-dummy-chip-countdown", ("seconds", (int) Math.Ceiling(left.TotalSeconds)));
    }

    private void Answer(bool optedIn)
    {
        if (_done)
            return;

        _done = true;
        Answered?.Invoke(optedIn);
    }
}
