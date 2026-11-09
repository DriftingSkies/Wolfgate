using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Content.Client._WF.JoinProgress;
using Content.Client._WF.Stylesheets;
using Content.Client.Resources;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.JoinProgress;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Launcher;

public sealed partial class LauncherConnectingGui
{
    private const float JoinUpdateSeconds = 0.1f;
    private const float JoinConsoleMinHeight = 300f;
    private const int JoinRowColumns = 72;
    private const float JoinRowMargins = 16f;
    private const int JoinBarWidth = 40;
    private const int JoinSegmentWidth = 12;
    private const int JoinLogRows = 3;
    private const double JoinCursorHz = 2;
    private const int JoinFallbackAdvance = 7;
    private const float JoinTipWidth = 578f;
    private const float JoinWatermarkAlpha = 0.06f;
    private const string JoinWatermarkPath = "/Textures/_WF/Branding/Logo/logo.png";

    // Quick joins shouldn't flash a button.
    private static readonly TimeSpan JoinCopyAfter = TimeSpan.FromSeconds(5);

    private readonly Dictionary<JoinStage, (Label Row, Label Bar, Label Detail)> _joinStageRows = new();
    private readonly List<Label> _joinLogRows = new();
    private Label _joinLinkRow = default!;
    private Label _joinChannelRow = default!;
    private Label _joinOverallRow = default!;
    private Control _joinOverallDivider = default!;
    private Label _joinCursorRow = default!;

    /// <summary>Wrapping copy of upstream's single-line disconnect reason.</summary>
    private RichTextLabel _joinDisconnectReason = default!;
    private string? _joinDisconnectText;

    private JoinConsoleStyle _joinStyle = default!;
    private float _joinUpdateTimer;
    private bool _joinStuckLogged;
    private bool _joinTipCentered;

    private void InitJoinProgress()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _joinStyle = new JoinConsoleStyle(WolfgateSkins.Get(_cfg.GetCVar(WolfgateCVars.UiStyle)), cache);

        Control window = ExitButton;
        while (window.Parent != null && window.Parent != this)
            window = window.Parent;

        window.Stylesheet = _joinStyle.Sheet;
        LoginTips.Stylesheet = _joinStyle.Sheet;

        // Over the panel, under the text.
        var watermark = new TextureRect
        {
            Texture = cache.GetTexture(JoinWatermarkPath),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            SetSize = new Vector2(390, 130),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Modulate = _joinStyle.Active.WithAlpha(JoinWatermarkAlpha),
        };
        window.AddChild(watermark);
        watermark.SetPositionInParent(1);

        // Last child, so the scanlines fall over the text as well.
        window.AddChild(new JoinConsoleCrt());

        var hazard = new JoinConsoleHazard(_joinStyle);
        JoinConsoleBox.AddChild(hazard);
        hazard.SetPositionInParent(2);

        JoinConsoleBox.MinHeight = JoinConsoleMinHeight;
        JoinConsoleTitle.Text = Loc.GetString("join-console-title");

        // The display font draws past its line height, so the title needs room around it.
        JoinConsoleTitle.Margin = new Thickness(8, 5, 0, 5);
        CopyButton.Text = CopyButton.Text?.ToUpperInvariant();
        foreach (var child in _symphonyRow!.Children)
        {
            if (child is Button button)
                button.Text = button.Text?.ToUpperInvariant();
        }

        CopyButtonDisconnected.Text = CopyButtonDisconnected.Text?.ToUpperInvariant();
        ExitButton.Text = Loc.GetString("join-console-exit");
        JoinConsoleHeader.Visible = false;
        ConnectStatus.Visible = false;

        BuildJoinRows();

        _joinDisconnectReason = new RichTextLabel { HorizontalAlignment = HAlignment.Center };
        Disconnected.AddChild(_joinDisconnectReason);
        _joinDisconnectReason.SetPositionInParent(DisconnectReason.GetPositionInParent() + 1);
        DisconnectReason.Visible = false;

        JoinStuckWarning.SetMessage(Loc.GetString("join-progress-stuck"), _joinStyle.Warn);
        JoinReconnectButton.OnPressed += _ => _state.ReconnectJoin();
        JoinCopyButton.OnPressed += _ => CopyText(JoinDetails());

        _state.ConnectionStateChanged += _ => _joinUpdateTimer = 0;
    }

    private void BuildJoinRows()
    {
        _joinLinkRow = AddJoinRow(_joinStyle.Done);
        AddJoinDivider();

        for (var stage = JoinStage.Connecting; stage <= JoinStage.EnteringGame; stage++)
        {
            _joinStageRows[stage] = (
                AddJoinRow(_joinStyle.Pending),
                AddJoinRow(_joinStyle.Active),
                AddJoinRow(_joinStyle.Done));
        }

        _joinChannelRow = AddJoinRow(_joinStyle.Done);
        _joinChannelRow.Visible = false;

        AddJoinDivider();
        _joinOverallRow = AddJoinRow(_joinStyle.Active);
        _joinOverallDivider = AddJoinDivider();

        for (var i = 0; i < JoinLogRows; i++)
        {
            _joinLogRows.Add(AddJoinRow(_joinStyle.Done));
        }

        _joinCursorRow = AddJoinRow(_joinStyle.Active);
    }

    /// <summary>Width of one console character in real pixels at the current UI scale.</summary>
    private int JoinAdvance()
    {
        return _joinStyle.Font.GetCharMetrics(new Rune('#'), UIScale)?.Advance ?? JoinFallbackAdvance;
    }

    private Label AddJoinRow(Color color)
    {
        var label = new Label { FontColorOverride = color };
        JoinConsoleRows.AddChild(label);
        return label;
    }

    /// <summary>Rich text can't centre its lines, so the tip is redrawn as centred labels of plain text.</summary>
    private void CenterJoinTip()
    {
        if (LoginTip.Parent is not { } parent || !LoginTips.Visible)
            return;

        var tip = FormattedMessage.RemoveMarkupPermissive(LoginTip.Text ?? string.Empty);
        if (tip.Length == 0)
            return;

        LoginTip.Visible = false;
        var columns = JoinConsoleText.ColumnsIn(JoinTipWidth, JoinAdvance(), UIScale);
        foreach (var line in JoinConsoleText.Wrap(tip, columns))
        {
            parent.AddChild(new Label { Text = line, Align = Label.AlignMode.Center, HorizontalExpand = true });
        }
    }

    private Control AddJoinDivider()
    {
        var divider = new PanelContainer
        {
            Margin = new Thickness(0, 2),
            PanelOverride = new StyleBoxFlat { BackgroundColor = _joinStyle.Pending, ContentMarginTopOverride = 1 },
        };
        JoinConsoleRows.AddChild(divider);
        return divider;
    }

    private void UpdateJoinProgress(FrameEventArgs args)
    {
        // Upstream sets these every frame in its own words, so this runs after it.
        RetryButton.Text = RetryButton.Text?.ToUpperInvariant();
        ReconnectButton.Text = ReconnectButton.Text?.ToUpperInvariant();

        if (DisconnectReason.Text != _joinDisconnectText)
        {
            _joinDisconnectText = DisconnectReason.Text;
            _joinDisconnectReason.SetMessage(FormattedMessage.FromUnformatted(_joinDisconnectText ?? ""));
        }

        // Upstream picks the tip after this file's setup has run.
        if (!_joinTipCentered)
        {
            _joinTipCentered = true;
            CenterJoinTip();
        }

        _joinUpdateTimer -= args.DeltaSeconds;
        if (_joinUpdateTimer > 0)
            return;

        _joinUpdateTimer = JoinUpdateSeconds;

        // Sized to its rows, as the columns take more UI width at low UI scales.
        JoinConsoleBox.SetWidth = JoinConsoleText.WidthOf(JoinRowColumns, JoinAdvance(), UIScale) + JoinRowMargins;

        var tracker = _state.JoinTracker;
        var now = _state.JoinNow;

        var stuck = tracker.IsStuck(now);
        if (stuck && !_joinStuckLogged)
            tracker.AddLog(Loc.GetString("join-console-log-stuck"), now);

        _joinStuckLogged = stuck;

        var dropped = tracker.Stage == JoinStage.None && tracker.LastAttempt != null;
        var connectingPage = _state.CurrentPage == LauncherConnecting.Page.Connecting;

        // A disconnect from in-game lands here with no attempt to show.
        var showRows = connectingPage || dropped;
        JoinConsoleRows.Visible = showRows;
        if (showRows)
        {
            UpdateJoinHeader();
            UpdateJoinStageRows(now);
            UpdateJoinChannelRow();
            UpdateJoinOverall(now);
            UpdateJoinLog(now);
        }

        JoinStuckWarning.Visible = stuck;
        JoinReconnectButton.Visible = (stuck || dropped) && _state.CanReconnectJoin;
        JoinCopyButton.Visible = stuck || dropped || tracker.AttemptElapsed(now) >= JoinCopyAfter;
    }

    private void UpdateJoinHeader()
    {
        var stats = _state.JoinNetStats;
        _joinLinkRow.Text = Loc.GetString("join-console-link",
            ("ping", _state.JoinPing?.ToString() ?? "--"),
            ("received", JoinBytes(stats.ReceivedBytes)),
            ("sent", JoinBytes(stats.SentBytes)),
            ("transport", JoinTransportName()));
    }

    private static string JoinBytes(long bytes)
    {
        return ByteHelpers.FormatBytes(bytes).ToUpperInvariant();
    }

    private string JoinTransportName()
    {
        if (_state.JoinDownloadInfo is not { } info)
            return Loc.GetString("join-console-transport-unknown");

        return Loc.GetString(info.OverGameConnection
            ? "join-console-transport-game"
            : "join-console-transport-socket");
    }

    /// <summary>
    /// The fast file channel's result: green when the websocket connected, red when this client fell back to the slower
    /// game connection. Hidden until the server says, and when the server has the fast channel off.
    /// </summary>
    private void UpdateJoinChannelRow()
    {
        if (_state.JoinTransferMode is not { } mode || mode == JoinTransferMode.GameConnection)
        {
            _joinChannelRow.Visible = false;
            return;
        }

        var ok = mode == JoinTransferMode.WebSocket;
        var left = Loc.GetString("join-console-channel-row",
            ("tag", Loc.GetString(ok ? "join-console-tag-Done" : "join-console-tag-Failed")),
            ("result", Loc.GetString($"join-console-channel-{mode}")));

        _joinChannelRow.Text = JoinConsoleText.Row(left, string.Empty, JoinRowColumns);
        _joinChannelRow.FontColorOverride = ok ? _joinStyle.Good : _joinStyle.Fail;
        _joinChannelRow.Visible = true;
    }

    private void UpdateJoinStageRows(TimeSpan now)
    {
        var tracker = _state.JoinTracker;
        var timings = tracker.Stage != JoinStage.None ? tracker.Timings(now) : tracker.LastAttempt;

        foreach (var (stage, (row, bar, detail)) in _joinStageRows)
        {
            var state = tracker.RowState(stage, now);
            var left = Loc.GetString("join-console-row",
                ("tag", Loc.GetString($"join-console-tag-{state}")),
                ("step", (int) stage),
                ("total", JoinProgressTracker.StageCount),
                ("stage", Loc.GetString($"join-progress-stage-{stage}")));

            var right = string.Empty;
            if (timings != null)
            {
                foreach (var (timedStage, duration) in timings)
                {
                    if (timedStage == stage)
                        right = Loc.GetString("join-console-seconds", ("seconds", duration.TotalSeconds.ToString("0.0")));
                }
            }

            row.Text = JoinConsoleText.Row(left, right, JoinRowColumns);
            row.FontColorOverride = state switch
            {
                JoinRowState.Done => _joinStyle.Done,
                JoinRowState.Active => _joinStyle.Active,
                JoinRowState.Stuck => _joinStyle.Warn,
                JoinRowState.Failed => _joinStyle.Fail,
                _ => _joinStyle.Pending,
            };

            var current = state is JoinRowState.Active or JoinRowState.Stuck;
            bar.Visible = current;
            detail.Visible = current;
            if (current)
                UpdateJoinStageDetail(stage, bar, detail, now);
        }
    }

    private void UpdateJoinStageDetail(JoinStage stage, Label bar, Label detail, TimeSpan now)
    {
        var elapsed = _state.JoinTracker.StageElapsed(now);

        if (stage == JoinStage.Downloading
            && _state.JoinDownloadedBytes is { } received
            && _state.JoinDownloadInfo is { } info)
        {
            var fraction = JoinProgressTracker.DownloadFraction(received, info.TotalBytes);
            bar.Text = Loc.GetString("join-console-bar",
                ("bar", JoinConsoleText.Bar(fraction, JoinBarWidth)),
                ("percent", (int) (fraction * 100)));
            detail.Text = Loc.GetString("join-console-detail", ("text", Loc.GetString("join-progress-download",
                ("received", JoinBytes(Math.Min(received, info.TotalBytes))),
                ("total", JoinBytes(info.TotalBytes)),
                ("speed", JoinBytes(JoinProgressTracker.DownloadSpeed(received, elapsed))))));
            return;
        }

        bar.Text = Loc.GetString("join-console-bar-scan", ("bar", JoinConsoleText.Scanner(elapsed.TotalSeconds, JoinBarWidth)));
        detail.Text = Loc.GetString("join-console-detail", ("text", stage == JoinStage.Connecting
            ? Loc.GetString($"connecting-state-{_state.ConnectionState}").ToUpperInvariant()
            : Loc.GetString($"join-console-hint-{stage}")));
    }

    private void UpdateJoinOverall(TimeSpan now)
    {
        var tracker = _state.JoinTracker;
        float? fraction = _state.JoinDownloadedBytes is { } received && _state.JoinDownloadInfo is { } info
            ? JoinProgressTracker.DownloadFraction(received, info.TotalBytes)
            : null;

        // An ended attempt's timings are on its rows; there is no running total left to show.
        _joinOverallRow.Visible = tracker.Stage != JoinStage.None;
        _joinOverallDivider.Visible = _joinOverallRow.Visible;
        _joinOverallRow.Text = Loc.GetString("join-console-overall",
            ("bar", JoinConsoleText.StageBar(tracker.Stage, fraction, tracker.StageElapsed(now).TotalSeconds, JoinSegmentWidth)),
            ("seconds", (int) tracker.AttemptElapsed(now).TotalSeconds));
    }

    private void UpdateJoinLog(TimeSpan now)
    {
        var log = _state.JoinTracker.Log;
        for (var i = 0; i < _joinLogRows.Count; i++)
        {
            var index = log.Count - _joinLogRows.Count + i;
            _joinLogRows[i].Text = index < 0 ? string.Empty : FormatJoinLog(log[index]);
        }

        var cursorOn = (int) (now.TotalSeconds * JoinCursorHz) % 2 == 0;
        _joinCursorRow.Text = cursorOn ? "> _" : ">";
    }

    private static string FormatJoinLog((TimeSpan Offset, string Text) entry)
    {
        return Loc.GetString("join-console-log",
            ("offset", entry.Offset.TotalSeconds.ToString("00.00")),
            ("text", entry.Text));
    }

    /// <summary>The stage timings and log of the current or last attempt, for a bug report. Empty if there are none.</summary>
    private string JoinDetails()
    {
        var tracker = _state.JoinTracker;
        IReadOnlyList<(JoinStage Stage, TimeSpan Duration)>? timings = tracker.Stage != JoinStage.None
            ? tracker.Timings(_state.JoinNow)
            : tracker.LastAttempt;

        if (timings == null)
            return string.Empty;

        var details = new StringBuilder();
        details.Append(Loc.GetString("join-progress-details-header", ("address", _state.Address ?? string.Empty)));

        foreach (var (stage, duration) in timings)
        {
            details.Append('\n');
            details.Append(Loc.GetString("join-progress-details-stage",
                ("step", (int) stage),
                ("stage", Loc.GetString($"join-progress-stage-{stage}")),
                ("seconds", duration.TotalSeconds.ToString("0.0"))));
        }

        if (_state.JoinTransferMode is { } channel)
        {
            details.Append('\n');
            details.Append(Loc.GetString("join-progress-details-channel", ("result", Loc.GetString($"join-console-channel-{channel}"))));
        }

        if (_state.JoinDownloadInfo is { } info)
        {
            details.Append('\n');
            details.Append(Loc.GetString("join-progress-details-files",
                ("count", info.FileCount),
                ("size", ByteHelpers.FormatBytes(info.TotalBytes)),
                ("transport", JoinTransportName())));
        }

        foreach (var entry in tracker.Log)
        {
            details.Append('\n');
            details.Append(FormatJoinLog(entry));
        }

        return details.ToString();
    }

    private string? WithJoinDetails(string? text)
    {
        var details = JoinDetails();
        if (details.Length == 0)
            return text;

        return string.IsNullOrEmpty(text) ? details : $"{text}\n\n{details}";
    }
}
