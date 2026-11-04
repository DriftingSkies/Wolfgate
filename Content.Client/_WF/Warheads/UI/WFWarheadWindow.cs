using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Warheads;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Warheads.UI;

/// <summary>
/// The warhead control panel: a status lamp row, a seven-segment countdown, and a section per fuze it has.
/// </summary>
public sealed class WFWarheadWindow : FancyWindow
{
    [Dependency] private IGameTiming _timing = default!;

    public event Action<float>? OnSetTimer;
    public event Action? OnArm;
    public event Action? OnTurnKey;
    public event Action<bool>? OnSafety;

    private WFWarheadBoundUserInterfaceState? _state;

    private readonly WFHazardStripes _topStripes = new();
    private readonly WFHazardStripes _bottomStripes = new();
    private readonly WFRadiationSymbol _symbol = new();
    private readonly Label _classLabel = new();
    private readonly Label _statusLabel = new();
    private readonly WFSegmentDisplay _display = new() { HorizontalExpand = true };
    private readonly Label _displayCaption = new() { HorizontalAlignment = HAlignment.Center };

    private readonly WFLamp _lampSafe = new() { Color = WFWarheadPalette.Green };
    private readonly WFLamp _lampArmed = new() { Color = WFWarheadPalette.Amber };
    private readonly WFLamp _lampCount = new() { Color = WFWarheadPalette.Red };
    private readonly WFLamp _lampFinal = new() { Color = WFWarheadPalette.Red, Blink = true };

    private readonly BoxContainer _timerSection = Section();
    private readonly BoxContainer _presetRow = new() { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
    private readonly Button _coverButton = new() { ToggleMode = true, HorizontalExpand = true };
    private readonly Button _armButton = new() { HorizontalExpand = true, MinHeight = 40 };
    private readonly List<Button> _timerButtons = new();

    private readonly BoxContainer _keySection = Section();
    private readonly WFKeyCylinder _keyA = new();
    private readonly WFKeyCylinder _keyB = new();
    private readonly Label _keyALabel = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Label _keyBLabel = new() { HorizontalAlignment = HAlignment.Center };
    private readonly ProgressBar _keyWindow = new() { MinHeight = 6, MinValue = 0, MaxValue = 1, HorizontalExpand = true };
    private readonly Button _turnKeyButton = new() { HorizontalExpand = true };

    private readonly BoxContainer _remoteSection = Section();
    private readonly WFSafetyLever _lever = new();
    private readonly Label _remoteStatus = new();
    private readonly Label _remoteLinks = new();
    private readonly Button _safetyButton = new() { HorizontalExpand = true };

    private readonly BoxContainer _impactSection = Section();

    private readonly GridContainer _specs = new() { Columns = 2, HorizontalExpand = true };
    private readonly Label _footer = new() { HorizontalAlignment = HAlignment.Center };

    public WFWarheadWindow()
    {
        IoCManager.InjectDependencies(this);
        MinSize = new Vector2(440, 640);
        SetSize = new Vector2(460, 720);

        _classLabel.FontColorOverride = WFWarheadPalette.Hazard;
        _displayCaption.FontColorOverride = Color.Gray;
        _footer.FontColorOverride = Color.Gray;
        _footer.Text = Loc.GetString("wf-warhead-ui-footer");

        BuildTimerSection();
        BuildKeySection();
        BuildRemoteSection();
        _impactSection.AddChild(Heading("wf-warhead-ui-impact-heading"));
        _impactSection.AddChild(new Label { Text = Loc.GetString("wf-warhead-ui-impact-body"), FontColorOverride = Color.Gray });

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            Children =
            {
                _symbol,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    VerticalAlignment = VAlignment.Center,
                    HorizontalExpand = true,
                    Children = { _classLabel, _statusLabel },
                },
            },
        };

        var lamps = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center,
            SeparationOverride = 14,
            Children =
            {
                LampWithLabel(_lampSafe, "wf-warhead-ui-lamp-safe"),
                LampWithLabel(_lampArmed, "wf-warhead-ui-lamp-armed"),
                LampWithLabel(_lampCount, "wf-warhead-ui-lamp-countdown"),
                LampWithLabel(_lampFinal, "wf-warhead-ui-lamp-final"),
            },
        };

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(10, 6),
            Children =
            {
                header,
                lamps,
                _display,
                _displayCaption,
                _timerSection,
                _keySection,
                _remoteSection,
                _impactSection,
                Heading("wf-warhead-ui-specs-heading"),
                _specs,
                _footer,
            },
        };

        ContentsContainer.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children =
            {
                _topStripes,
                new ScrollContainer
                {
                    VerticalExpand = true,
                    HScrollEnabled = false,
                    Children = { body },
                },
                _bottomStripes,
            },
        });
    }

    private static readonly (float Delta, string Loc)[] TimerSteps =
    {
        (-60f, "wf-warhead-ui-timer-minus-minute"),
        (-10f, "wf-warhead-ui-timer-minus-ten"),
        (10f, "wf-warhead-ui-timer-plus-ten"),
        (60f, "wf-warhead-ui-timer-plus-minute"),
    };

    private static BoxContainer Section()
    {
        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Visible = false,
        };
    }

    private static Control Heading(string loc)
    {
        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children =
            {
                new Label { Text = Loc.GetString(loc), FontColorOverride = WFWarheadPalette.Hazard },
                new PanelContainer
                {
                    MinHeight = 2,
                    PanelOverride = new StyleBoxFlat { BackgroundColor = WFWarheadPalette.PanelEdge },
                },
            },
        };
    }

    private static Control LampWithLabel(WFLamp lamp, string loc)
    {
        lamp.HorizontalAlignment = HAlignment.Center;
        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children =
            {
                lamp,
                new Label { Text = Loc.GetString(loc), HorizontalAlignment = HAlignment.Center, FontColorOverride = Color.Gray },
            },
        };
    }

    private void BuildTimerSection()
    {
        _timerSection.AddChild(Heading("wf-warhead-ui-timer-heading"));

        var adjust = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        foreach (var (delta, loc) in TimerSteps)
        {
            var button = new Button
            {
                Text = Loc.GetString(loc),
                HorizontalExpand = true,
            };
            var d = delta;
            button.OnPressed += _ =>
            {
                if (_state != null)
                    OnSetTimer?.Invoke(_state.Timer + d);
            };
            adjust.AddChild(button);
            _timerButtons.Add(button);
        }

        _timerSection.AddChild(adjust);
        _timerSection.AddChild(_presetRow);

        _coverButton.Text = Loc.GetString("wf-warhead-ui-cover-closed");
        _coverButton.OnToggled += args =>
        {
            _coverButton.Text = Loc.GetString(args.Pressed ? "wf-warhead-ui-cover-open" : "wf-warhead-ui-cover-closed");
            Refresh();
        };

        _armButton.Text = Loc.GetString("wf-warhead-ui-arm");
        _armButton.StyleClasses.Add("Caution");
        _armButton.OnPressed += _ =>
        {
            _coverButton.Pressed = false;
            _coverButton.Text = Loc.GetString("wf-warhead-ui-cover-closed");
            OnArm?.Invoke();
        };

        _timerSection.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Children = { _coverButton, _armButton },
        });
    }

    private void BuildKeySection()
    {
        _keySection.AddChild(Heading("wf-warhead-ui-keys-heading"));
        _keyA.HorizontalAlignment = HAlignment.Center;
        _keyB.HorizontalAlignment = HAlignment.Center;

        _keySection.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Children =
            {
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    Children = { _keyA, _keyALabel },
                },
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    Children = { _keyB, _keyBLabel },
                },
            },
        });

        _keySection.AddChild(_keyWindow);
        _turnKeyButton.Text = Loc.GetString("wf-warhead-ui-turn-key");
        _turnKeyButton.OnPressed += _ => OnTurnKey?.Invoke();
        _keySection.AddChild(_turnKeyButton);
    }

    private void BuildRemoteSection()
    {
        _remoteSection.AddChild(Heading("wf-warhead-ui-remote-heading"));
        _safetyButton.OnPressed += _ =>
        {
            if (_state != null)
                OnSafety?.Invoke(_state.State != WFWarheadState.Safe);
        };

        _remoteSection.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            Children =
            {
                _lever,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    VerticalAlignment = VAlignment.Center,
                    Children = { _remoteStatus, _remoteLinks, _safetyButton },
                },
            },
        });
    }

    public void UpdateState(WFWarheadBoundUserInterfaceState state)
    {
        var presetsChanged = _state == null || !_state.TimerOptions.SequenceEqual(state.TimerOptions);
        _state = state;

        _classLabel.Text = Loc.GetString(state.YieldClass);

        _timerSection.Visible = state.HasTimerFuze;
        _keySection.Visible = state.HasTwoPerson;
        _remoteSection.Visible = state.HasRemote;
        _impactSection.Visible = state.HasImpact;

        if (presetsChanged)
        {
            _presetRow.RemoveAllChildren();
            foreach (var option in state.TimerOptions)
            {
                var seconds = option;
                var button = new Button { Text = FormatTime((int) seconds), HorizontalExpand = true };
                button.OnPressed += _ => OnSetTimer?.Invoke(seconds);
                _presetRow.AddChild(button);
            }
        }

        _specs.RemoveAllChildren();
        AddSpec("wf-warhead-ui-spec-blast", state.BlastRadius > 0f
            ? Loc.GetString("wf-warhead-ui-spec-tiles", ("tiles", (int) MathF.Round(state.BlastRadius)))
            : Loc.GetString("wf-warhead-ui-spec-none"));
        AddSpec("wf-warhead-ui-spec-flash", Loc.GetString("wf-warhead-ui-spec-tiles", ("tiles", (int) state.FlashRange)));
        AddSpec("wf-warhead-ui-spec-fallout", state.FalloutSources > 0
            ? Loc.GetString("wf-warhead-ui-spec-fallout-value", ("sources", state.FalloutSources), ("tiles", (int) state.FalloutRadius))
            : Loc.GetString("wf-warhead-ui-spec-none"));
        AddSpec("wf-warhead-ui-spec-emp", state.EmpRange > 0f
            ? Loc.GetString("wf-warhead-ui-spec-tiles", ("tiles", (int) state.EmpRange))
            : Loc.GetString("wf-warhead-ui-spec-none"));

        if (state.DeletesGrid)
        {
            _specs.AddChild(new Label { Text = Loc.GetString("wf-warhead-ui-spec-grid"), FontColorOverride = WFWarheadPalette.Red });
            _specs.AddChild(new Label { Text = Loc.GetString("wf-warhead-ui-spec-grid-value"), FontColorOverride = WFWarheadPalette.Red });
        }

        Refresh();
    }

    private void AddSpec(string label, string value)
    {
        _specs.AddChild(new Label { Text = Loc.GetString(label), FontColorOverride = Color.Gray, MinWidth = 160 });
        _specs.AddChild(new Label { Text = value });
    }

    /// <summary>Applies the parts of the state that change with time or with the cover.</summary>
    private void Refresh()
    {
        if (_state is not { } state)
            return;

        var counting = state.State is WFWarheadState.Countdown or WFWarheadState.Final;
        var safe = state.State == WFWarheadState.Safe;

        _lampSafe.On = safe;
        _lampArmed.On = state.State == WFWarheadState.Armed;
        _lampCount.On = state.State == WFWarheadState.Countdown;
        _lampFinal.On = state.State == WFWarheadState.Final;

        _statusLabel.Text = Loc.GetString("wf-warhead-ui-status-" + state.State.ToString().ToLowerInvariant());
        _statusLabel.FontColorOverride = state.State switch
        {
            WFWarheadState.Safe => WFWarheadPalette.Green,
            WFWarheadState.Armed => WFWarheadPalette.Amber,
            _ => WFWarheadPalette.Red,
        };

        _symbol.Spin = state.State switch
        {
            WFWarheadState.Final => 240f,
            WFWarheadState.Countdown => 60f,
            _ => 0f,
        };
        _symbol.Glow = state.State switch
        {
            WFWarheadState.Safe => Color.Transparent,
            WFWarheadState.Armed => WFWarheadPalette.Amber,
            _ => WFWarheadPalette.Red,
        };
        _topStripes.Animate = !safe;
        _bottomStripes.Animate = !safe;

        _display.Lit = state.State switch
        {
            WFWarheadState.Final => WFWarheadPalette.Red,
            WFWarheadState.Countdown => WFWarheadPalette.Green,
            WFWarheadState.Armed => WFWarheadPalette.Amber,
            _ => Color.FromHex("#6FA0C0"),
        };
        _display.BlinkColon = counting;
        _display.Flash = state.State == WFWarheadState.Final;
        _displayCaption.Text = Loc.GetString(counting ? "wf-warhead-ui-caption-counting" : "wf-warhead-ui-caption-timer");

        foreach (var button in _timerButtons)
        {
            button.Disabled = !safe;
        }

        foreach (var child in _presetRow.Children)
        {
            if (child is Button preset)
                preset.Disabled = !safe;
        }

        _coverButton.Disabled = !safe;
        _armButton.Disabled = !safe || !_coverButton.Pressed;

        var keyOpen = state.FirstKeyHolder != null;
        var keysTurned = counting || state.State == WFWarheadState.Armed && !state.HasRemote;
        _keyA.Turned = keyOpen || keysTurned;
        _keyB.Turned = keysTurned;
        _keyALabel.Text = keyOpen
            ? Loc.GetString("wf-warhead-ui-key-turned-by", ("name", state.FirstKeyHolder!))
            : Loc.GetString(keysTurned ? "wf-warhead-ui-key-turned" : "wf-warhead-ui-key-off");
        _keyBLabel.Text = Loc.GetString(keysTurned ? "wf-warhead-ui-key-turned" : keyOpen ? "wf-warhead-ui-key-waiting" : "wf-warhead-ui-key-off");
        _turnKeyButton.Disabled = !(safe || state.State == WFWarheadState.Armed);

        _lever.SafetyOn = safe;
        _remoteStatus.Text = Loc.GetString(safe ? "wf-warhead-ui-remote-safe" : state.State == WFWarheadState.Armed
            ? "wf-warhead-ui-remote-armed"
            : "wf-warhead-ui-remote-counting");
        _remoteStatus.FontColorOverride = safe ? WFWarheadPalette.Green : WFWarheadPalette.Red;
        _remoteLinks.Text = Loc.GetString("wf-warhead-ui-remote-links", ("links", state.RemoteLinks), ("seconds", (int) state.SignalTimer));
        _safetyButton.Text = Loc.GetString(safe ? "wf-warhead-ui-safety-off" : "wf-warhead-ui-safety-on");
        _safetyButton.Disabled = counting;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_state is not { } state)
            return;

        var counting = state.State is WFWarheadState.Countdown or WFWarheadState.Final;
        _display.Seconds = counting
            ? Math.Max(0, (int) Math.Ceiling((state.DetonateAt - _timing.CurTime).TotalSeconds))
            : (int) state.Timer;

        if (state.FirstKeyHolder != null && state.KeyWindow > 0f)
        {
            var left = state.KeyWindow - (float) (_timing.CurTime - state.FirstKeyTime).TotalSeconds;
            _keyWindow.Value = Math.Clamp(left / state.KeyWindow, 0f, 1f);
            if (left <= 0f)
            {
                state.FirstKeyHolder = null;
                Refresh();
            }
        }
        else
        {
            _keyWindow.Value = 0f;
        }
    }

    private static string FormatTime(int seconds)
    {
        return seconds >= 60 && seconds % 60 == 0
            ? Loc.GetString("wf-warhead-ui-minutes", ("minutes", seconds / 60))
            : Loc.GetString("wf-warhead-ui-seconds", ("seconds", seconds));
    }
}
