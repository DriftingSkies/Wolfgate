using Content.Shared._WF.RoundSetup;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

/// <summary>A mouse-transparent warning with a shared atmospheric-entry countdown.</summary>
public sealed class WFCrashCountdownHud : PanelContainer
{
    /// <summary>The preferred width of the horizontal warning banner.</summary>
    public const float PanelWidth = 400f;

    private static readonly Color Amber = Color.FromHex("#F1B85A");
    private static readonly Color Red = Color.FromHex("#FF786C");
    private readonly StyleBoxFlat _border;
    private readonly StyleBoxFlat _fill;
    private readonly Label _clock;
    private readonly Label _title;
    private readonly ProgressBar _progress;

    /// <summary>The remaining whole seconds currently shown by the banner.</summary>
    public int RemainingSeconds { get; private set; }

    /// <summary>Builds the warning once; countdown updates only change its readout and color.</summary>
    public WFCrashCountdownHud(IResourceCache resources)
    {
        SetWidth = PanelWidth;
        MouseFilter = MouseFilterMode.Ignore;
        _border = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#1D1711EE"),
            BorderColor = Amber,
            BorderThickness = new Thickness(1),
        };
        _border.SetContentMarginOverride(StyleBox.Margin.Horizontal, 14);
        _border.SetContentMarginOverride(StyleBox.Margin.Vertical, 8);
        PanelOverride = _border;

        var small = new VectorFont(resources.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 10);
        var large = new VectorFont(resources.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 28);
        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
            MouseFilter = MouseFilterMode.Ignore,
        };
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 16,
            MouseFilter = MouseFilterMode.Ignore,
        };
        var copy = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            SeparationOverride = 1,
            MouseFilter = MouseFilterMode.Ignore,
        };
        _title = MakeLabel(Loc.GetString("wf-round-setup-countdown-title"), small, Amber);
        copy.AddChild(_title);
        copy.AddChild(MakeLabel(Loc.GetString("wf-round-setup-countdown-entry"), small, Color.White));
        copy.AddChild(MakeLabel(Loc.GetString("wf-round-setup-countdown-controls"), small, Color.FromHex("#BEADA0")));
        _clock = MakeLabel(string.Empty, large, Amber);
        _clock.SetWidth = 140;
        _clock.ClipText = false;
        _clock.Name = "CrashCountdownClock";
        _clock.VerticalAlignment = VAlignment.Center;
        _clock.Align = Label.AlignMode.Right;
        row.AddChild(copy);
        row.AddChild(_clock);
        column.AddChild(row);
        _fill = new StyleBoxFlat { BackgroundColor = Amber };
        _progress = new ProgressBar
        {
            MinValue = 0,
            MaxValue = WFCrashCountdownEvent.DurationSeconds,
            SetHeight = 3,
            HorizontalExpand = true,
            BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#49392A") },
            ForegroundStyleBoxOverride = _fill,
            MouseFilter = MouseFilterMode.Ignore,
        };
        column.AddChild(_progress);
        AddChild(column);
    }

    /// <summary>Drains the time bar and highlights the final thirty seconds without flashing.</summary>
    public void SetRemaining(int seconds)
    {
        seconds = Math.Clamp(seconds, 0, WFCrashCountdownEvent.DurationSeconds);
        RemainingSeconds = seconds;
        var text = $"{seconds / 60:00}:{seconds % 60:00}";
        if (_clock.Text != text)
            _clock.Text = text;
        _progress.Value = seconds;
        var color = seconds <= 30 ? Red : Amber;
        _clock.FontColorOverride = color;
        _title.FontColorOverride = color;
        _border.BorderColor = color;
        _fill.BackgroundColor = color;
    }

    private static Label MakeLabel(string text, Font font, Color color) => new()
    {
        Text = text,
        FontOverride = font,
        FontColorOverride = color,
        ClipText = true,
        MouseFilter = MouseFilterMode.Ignore,
    };
}
