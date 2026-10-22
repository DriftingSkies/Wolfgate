using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Feedback;
using Content.Shared.CCVar;
using JetBrains.Annotations;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Content.Client._WF.Feedback;

/// <summary>
/// Opens the feedback window from the game menu and sends what the player writes, along with what the client can
/// say about itself: a screenshot from before the window opened, its build, window and frame rate, the windows
/// that were open and its latest warnings and errors. Also shows the notice when staff move a report on, and offers
/// the end of shift survey: when the server says the player went into cryo, and when they leave from the game menu.
/// </summary>
[UsedImplicitly]
public sealed partial class FeedbackUIController : UIController, IOnSystemChanged<FeedbackSystem>
{
    /// <summary>Frames between the menu button and the window, so the screenshot is taken without either.</summary>
    private const int OpenDelayFrames = 2;

    /// <summary>How long the survey chip stays up when nobody answers it.</summary>
    private static readonly TimeSpan ChipLifetime = TimeSpan.FromMinutes(2);

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IClientConsoleHost _console = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ILocalizationManager _loc = default!;
    [Dependency] private ILogManager _logManager = default!;

    private readonly FeedbackClientLog _log = new();
    private FeedbackSystem? _feedback;
    private FeedbackWindow? _window;
    private FeedbackNoticeWindow? _notice;
    private FeedbackSurveyChip? _chip;
    private FeedbackSurveyWindow? _survey;
    private TimeSpan _chipUntil;
    // What leaving was put off for the survey, run once the survey is answered or turned down.
    private Action? _leave;
    // A survey has been sent this connection; it isn't offered again.
    private bool _surveyed;
    // The next answer from the server is for the survey, not the report window.
    private bool _awaitingSurvey;
    private ISawmill _sawmill = default!;

    private int _openIn;
    private byte[]? _screenshot;
    // Counts captures, so a screenshot that finishes encoding after a newer one was asked for is dropped.
    private int _capture;
    // What the client looked like when the player asked for the window, before the window itself was in the way.
    private Dictionary<string, string> _info = new();

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("feedback");
        _console.AddFormatted += _log.OnLine;
    }

    public void OnSystemLoaded(FeedbackSystem system)
    {
        _feedback = system;
        system.Answered += OnAnswered;
        system.History += OnHistory;
        system.Notice += OnNotice;
        system.SurveyPrompt += OnSurveyPrompt;
    }

    public void OnSystemUnloaded(FeedbackSystem system)
    {
        system.Answered -= OnAnswered;
        system.History -= OnHistory;
        system.Notice -= OnNotice;
        system.SurveyPrompt -= OnSurveyPrompt;
        _leave = null;
        _surveyed = false;
        _awaitingSurvey = false;
        RemoveChip();
        _survey?.Close();
        // Closed first: closing the notice tells the server it was seen, and there is no server to tell.
        _feedback = null;
        _window?.Close();
        _notice?.Close();
    }

    /// <summary>Opens the feedback window, or closes it if it is open.</summary>
    public void ToggleWindow()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        if (_openIn > 0)
            return;

        _info = GatherInfo();
        _screenshot = null;
        _capture++;
        if (_cfg.GetCVar(FeedbackCVars.Screenshots))
        {
            var capture = _capture;
            _clyde.Screenshot(ScreenshotType.Final, image => OnScreenshot(image, capture));
        }

        _openIn = OpenDelayFrames;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_openIn > 0 && --_openIn == 0)
            OpenWindow();

        // Only a chip offered on the way into cryo times out; one put up on leaving waits for an answer.
        if (_chip != null && _leave == null && _timing.RealTime > _chipUntil)
            RemoveChip();
    }

    /// <summary>Whether the end of shift survey would be offered now.</summary>
    public bool SurveyOffered => !_surveyed
        && _feedback != null
        && _net.IsConnected
        && _cfg.GetCVar(FeedbackCVars.Enabled)
        && _cfg.GetCVar(FeedbackCVars.Surveys)
        && !_cfg.GetCVar(FeedbackCVars.SurveyNever);

    /// <summary>
    /// Offers the survey before the player leaves, and leaves once they have answered or turned it down. False when
    /// it isn't offered, and the caller should leave now.
    /// </summary>
    public bool TryOfferSurveyBeforeLeaving(Action leave)
    {
        if (!SurveyOffered)
            return false;

        ShowChip(FeedbackSurveyTrigger.Leaving, leave);
        return true;
    }

    private void OnSurveyPrompt(FeedbackSurveyPromptEvent ev)
    {
        if (SurveyOffered && _survey == null)
            ShowChip(ev.Trigger, null);
    }

    private void ShowChip(FeedbackSurveyTrigger trigger, Action? leave)
    {
        RemoveChip();
        _leave = leave;
        _chipUntil = _timing.RealTime + ChipLifetime;

        var chip = new FeedbackSurveyChip(leave != null);
        chip.Accepted += () =>
        {
            RemoveChip();
            OpenSurvey(trigger);
        };
        chip.Dismissed += () =>
        {
            RemoveChip();
            Leave();
        };
        chip.Never += () =>
        {
            _cfg.SetCVar(FeedbackCVars.SurveyNever, true);
            _cfg.SaveToFile();
            RemoveChip();
            Leave();
        };

        _chip = chip;
        UIManager.WindowRoot.AddChild(chip);
        LayoutContainer.SetAnchorAndMarginPreset(chip, LayoutContainer.LayoutPreset.CenterBottom, margin: 160);
        LayoutContainer.SetGrowHorizontal(chip, LayoutContainer.GrowDirection.Both);
        LayoutContainer.SetGrowVertical(chip, LayoutContainer.GrowDirection.Begin);
    }

    private void RemoveChip()
    {
        _chip?.Orphan();
        _chip = null;
    }

    private void OpenSurvey(FeedbackSurveyTrigger trigger)
    {
        // Closing an unsent survey calls off the leaving, but this one is opened for that same leaving.
        var leave = _leave;
        _survey?.Close();
        _leave = leave;
        _awaitingSurvey = false;
        var window = new FeedbackSurveyWindow { Trigger = trigger };
        window.Submitted += survey =>
        {
            if (_feedback == null)
                return;

            survey.ClientInfo = GatherInfo();
            survey.ClientLog = _log.Snapshot();
            _awaitingSurvey = true;
            _feedback.Submit(survey);
        };
        // Closing a survey that wasn't sent calls off the leaving it was put up for.
        window.OnClose += () =>
        {
            if (_survey != window)
                return;

            _survey = null;
            _awaitingSurvey = false;
            if (!window.Sent)
                _leave = null;
        };

        _survey = window;
        window.OpenCentered();
    }

    /// <summary>Runs the leaving that waited on the survey, if any.</summary>
    private void Leave()
    {
        var leave = _leave;
        _leave = null;
        leave?.Invoke();
    }

    private void OpenWindow()
    {
        if (_window != null)
            return;

        _window = new FeedbackWindow();
        _window.OnClose += () => _window = null;
        _window.Submitted += OnSubmitted;
        _window.HistoryRequested += () => _feedback?.RequestHistory();
        _window.SetScreenshotReady(_screenshot != null);
        _window.OpenCentered();
    }

    private void OnSubmitted(FeedbackSubmitEvent report)
    {
        if (_feedback == null || _window == null)
            return;

        report.ClientInfo = _info;
        report.ClientLog = _log.Snapshot();
        report.Screenshot = _window.AttachScreenshot ? _screenshot : null;
        _feedback.Submit(report);
    }

    private void OnAnswered(FeedbackResultEvent result)
    {
        if (!_awaitingSurvey)
        {
            _window?.ShowResult(result);
            return;
        }

        _awaitingSurvey = false;
        _survey?.ShowResult(result);
        if (result.Error != null)
            return;

        _surveyed = true;
        Leave();
    }

    private void OnHistory(FeedbackHistoryEvent history)
    {
        _window?.ShowHistory(history.Reports);
    }

    /// <summary>Staff moved some reports on. The server keeps sending them until the notice has been closed.</summary>
    private void OnNotice(FeedbackNoticeEvent notice)
    {
        if (notice.Reports.Count == 0)
            return;

        // A newer notice lists everything the old one did, so the old one goes without being counted as seen.
        if (_notice is { } old)
        {
            _notice = null;
            old.Close();
        }

        var window = new FeedbackNoticeWindow(notice.Reports);
        window.OnClose += () =>
        {
            if (_notice != window)
                return;

            _notice = null;
            _feedback?.MarkSeen(window.Ids);
        };

        _notice = window;
        window.OpenCentered();
    }

    private async void OnScreenshot(Image<Rgb24> image, int capture)
    {
        // Encoding a full frame takes a while, so it stays off the main thread.
        var data = await Task.Run(() => Encode(image));
        if (capture != _capture)
            return;

        _screenshot = data;
        _window?.SetScreenshotReady(data != null);
    }

    /// <summary>The frame as a JPEG no larger than the server keeps, or null.</summary>
    private byte[]? Encode(Image<Rgb24> image)
    {
        try
        {
            using (image)
            {
                using var stream = new MemoryStream();
                var longest = Math.Max(image.Width, image.Height);
                if (longest > FeedbackRules.MaxScreenshotSide)
                {
                    var scale = (float) FeedbackRules.MaxScreenshotSide / longest;
                    var width = Math.Max(1, (int) (image.Width * scale));
                    var height = Math.Max(1, (int) (image.Height * scale));
                    using var small = image.Clone(context => context.Resize(width, height));
                    small.SaveAsJpeg(stream);
                }
                else
                {
                    image.SaveAsJpeg(stream);
                }

                return stream.Length <= FeedbackRules.MaxScreenshotBytes ? stream.ToArray() : null;
            }
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Could not encode the screenshot for a report: {e.Message}");
            return null;
        }
    }

    private Dictionary<string, string> GatherInfo()
    {
        var info = new Dictionary<string, string>();
        var invariant = CultureInfo.InvariantCulture;

        info["time_utc"] = DateTime.UtcNow.ToString("o", invariant);
        info["build.fork_id"] = _cfg.GetCVar(CVars.BuildForkId);
        info["build.version"] = _cfg.GetCVar(CVars.BuildVersion);
        info["build.engine_version"] = _cfg.GetCVar(CVars.BuildEngineVersion);
        info["state"] = _state.CurrentState.GetType().Name;
        info["locale"] = _loc.DefaultCulture?.Name ?? string.Empty;

        var size = _clyde.ScreenSize;
        info["window.size"] = $"{size.X}x{size.Y}";
        info["window.mode"] = _cfg.GetCVar(CVars.DisplayWindowMode).ToString(invariant);
        info["fps"] = _timing.FramesPerSecondAvg.ToString("F1", invariant);
        if (_net.ServerChannel is { } channel)
            info["ping"] = channel.Ping.ToString(invariant);

        info["ui.scale"] = UIManager.DefaultUIScale.ToString("F2", invariant);
        info["ui.layout"] = _cfg.GetCVar(CCVars.UILayout);
        info["ui.theme"] = _cfg.GetCVar(CVars.InterfaceTheme);
        info["ui.style"] = _cfg.GetCVar(WolfgateCVars.UiStyle);
        info["ui.focused"] = UIManager.KeyboardFocused?.GetType().Name ?? string.Empty;
        info["ui.windows"] = string.Join(", ", UIManager.WindowRoot.Children
            .OfType<BaseWindow>()
            .Where(window => window.Visible)
            .Select(window => window.GetType().Name));

        var eye = _eye.CurrentEye;
        info["eye.map"] = ((int) eye.Position.MapId).ToString(invariant);
        info["eye.position"] = $"{eye.Position.X.ToString("F2", invariant)}, {eye.Position.Y.ToString("F2", invariant)}";
        info["eye.rotation"] = eye.Rotation.Degrees.ToString("F1", invariant);
        info["eye.zoom"] = $"{eye.Zoom.X.ToString("F2", invariant)}, {eye.Zoom.Y.ToString("F2", invariant)}";

        if (_player.LocalEntity is { } entity && EntityManager.TryGetNetEntity(entity, out var netEntity))
            info["entity"] = netEntity.Value.Id.ToString(invariant);

        return info;
    }
}
