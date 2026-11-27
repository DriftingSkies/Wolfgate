using Content.Client._WG.DoomArcade;
using Content.Client._WG.GameHub;
using Content.Client._WG.JoinProgress;
using Content.Client.Launcher;
using Content.Client._WG.Stylesheets;
using Content.Client.Parallax;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.GameHub;
using Content.Shared._WG.JoinQueue;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.JoinQueue;

/// <summary>
/// The join queue as the player sees it, in the join console's terminal look: how full the server is, their place,
/// how long they have waited and roughly how long is left. Freedoom and Freedoom Phase 2 can be played while waiting, and
/// the moproach from the connect screen keeps them company.
/// </summary>
public sealed class JoinQueueScreen : Control
{
    private const int Columns = 56;
    private const int BarWidth = Columns - 2;
    private const float UpdateSeconds = 0.25f;

    private readonly JoinQueueManager _queue;
    private readonly IGameTiming _timing;
    private readonly JoinConsoleStyle _style;

    private readonly Label _server;
    private readonly Label _place;
    private readonly Label _bar;
    private readonly Label _waited;
    private readonly Label _estimate;
    private readonly BoxContainer _notes;

    private float _timer;
    private int _notesHold = -1;
    private DoomArcadeWindow? _arcade;

    public event Action? LeavePressed;

    public JoinQueueScreen(JoinQueueManager queue, IGameTiming timing, IConfigurationManager cfg, IResourceCache cache)
    {
        _queue = queue;
        _timing = timing;
        _style = new JoinConsoleStyle(WolfgateSkins.Get(cfg.GetCVar(WolfgateCVars.UiStyle)), cache);

        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);
        AddChild(new ParallaxControl());
        AddChild(new SymphonyMoproach());

        var leave = new Button
        {
            Text = Loc.GetString("join-queue-leave"),
            HorizontalAlignment = HAlignment.Right,
            HorizontalExpand = true,
        };
        leave.OnPressed += _ => LeavePressed?.Invoke();

        var rows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8, 4, 8, 8) };
        _server = AddRow(rows, _style.Warn);
        AddDivider(rows);
        _place = AddRow(rows, _style.Active);
        _bar = AddRow(rows, _style.Active);
        _waited = AddRow(rows, _style.Done);
        _estimate = AddRow(rows, _style.Done);
        AddDivider(rows);
        _notes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        rows.AddChild(_notes);
        AddDivider(rows);
        rows.AddChild(new Label { Text = Loc.GetString("join-queue-arcade"), FontColorOverride = _style.Done });
        var arcade = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Align = BoxContainer.AlignMode.Center };
        arcade.AddChild(ArcadeButton(WGGameHubSystem.DoomGame));
        arcade.AddChild(ArcadeButton(WGGameHubSystem.Doom2Game));
        rows.AddChild(arcade);

        var window = new Control
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Stylesheet = _style.Sheet,
            Children =
            {
                new PanelContainer { StyleClasses = { StyleBase.ClassAngleRect } },
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    Children =
                    {
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            Children =
                            {
                                new Label
                                {
                                    Text = Loc.GetString("join-queue-title"),
                                    StyleClasses = { StyleBase.StyleClassLabelHeading },
                                    // The display font draws past its line height.
                                    Margin = new Thickness(8, 5, 0, 5),
                                    VAlign = Label.VAlignMode.Center,
                                },
                                leave,
                            },
                        },
                        new HighDivider(),
                        new JoinConsoleHazard(_style),
                        rows,
                    },
                },
                // Last, so the scanlines fall over the text.
                new JoinConsoleCrt(),
            },
        };
        AddChild(window);

        _queue.Updated += Refresh;
        Refresh();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _queue.Updated -= Refresh;
        // Let in or gone: the game was only for the wait.
        _arcade?.Close();
    }

    private Button ArcadeButton(string game)
    {
        var name = Loc.GetString(IoCManager.Resolve<IPrototypeManager>().Index<WGGamePrototype>(game).Name);
        var button = new Button { Text = Loc.GetString("join-queue-arcade-play", ("game", name)), Margin = new Thickness(4, 4) };
        button.OnPressed += _ =>
        {
            _arcade?.Close();
            _arcade = new DoomArcadeWindow();
            _arcade.OnClose += () => _arcade = null;
            _arcade.OpenCentered();
            _arcade.Begin(null, game);
        };
        return button;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _timer -= args.DeltaSeconds;
        if (_timer > 0f)
            return;

        _timer = UpdateSeconds;
        Refresh();
    }

    private void Refresh()
    {
        if (_queue.Entering)
        {
            _place.FontColorOverride = _style.Good;
            _place.Text = Loc.GetString("join-queue-entering");
            _bar.Text = JoinConsoleText.Bar(1f, BarWidth);
            _estimate.Text = string.Empty;
            return;
        }

        if (_queue.Last is not { } last)
            return;

        var since = (int) (_timing.RealTime - _queue.LastAt).TotalSeconds;
        var next = last.Position <= 1;

        _server.Text = Row("join-queue-server",
            Loc.GetString("join-queue-server-value", ("players", last.Players), ("cap", last.Cap)));

        _place.FontColorOverride = next ? _style.Good : _style.Active;
        _place.Text = Row("join-queue-place", next
            ? Loc.GetString("join-queue-place-next")
            : Loc.GetString("join-queue-place-value", ("position", last.Position), ("total", last.Total)));

        // Fills as the places ahead clear.
        var start = Math.Max(_queue.StartPosition, 1);
        _bar.Text = JoinConsoleText.Bar((start - last.Position) / (float) start, BarWidth);

        _waited.Text = Row("join-queue-waited", JoinQueueText.Clock(last.WaitedSeconds + since));
        _estimate.Text = Row("join-queue-estimate", Estimate(last.EstimateSeconds, since));

        if (_notesHold != last.HoldSeconds)
            BuildNotes(last.HoldSeconds);
    }

    private static string Estimate(int seconds, int since)
    {
        if (seconds == MsgJoinQueueUpdate.NoEstimate)
            return Loc.GetString("join-queue-estimate-unknown");

        var minutes = JoinQueueText.MinutesLeft(seconds - since);
        return minutes == 0
            ? Loc.GetString("join-queue-estimate-soon")
            : Loc.GetString("join-queue-estimate-minutes", ("minutes", minutes));
    }

    private void BuildNotes(int holdSeconds)
    {
        _notesHold = holdSeconds;
        _notes.RemoveAllChildren();

        var text = Loc.GetString("join-queue-note-auto");
        if (holdSeconds > 0)
        {
            var time = holdSeconds < 120
                ? Loc.GetString("join-queue-hold-seconds", ("seconds", holdSeconds))
                : Loc.GetString("join-queue-hold-minutes", ("minutes", holdSeconds / 60));
            text = $"{text} {Loc.GetString("join-queue-note-hold", ("time", time))}";
        }

        foreach (var line in JoinConsoleText.Wrap(text, Columns))
        {
            _notes.AddChild(new Label { Text = line, FontColorOverride = _style.Pending });
        }
    }

    private static string Row(string label, string value)
    {
        return JoinConsoleText.Row(Loc.GetString(label), value, Columns);
    }

    private static Label AddRow(Control parent, Color color)
    {
        var label = new Label { FontColorOverride = color };
        parent.AddChild(label);
        return label;
    }

    private void AddDivider(Control parent)
    {
        parent.AddChild(new PanelContainer
        {
            Margin = new Thickness(0, 2),
            PanelOverride = new StyleBoxFlat { BackgroundColor = _style.Pending, ContentMarginTopOverride = 1 },
        });
    }
}
