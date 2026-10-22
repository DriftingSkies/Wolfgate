#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Content.Client._WF.Feedback;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._NF.CryoSleep;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Feedback;
using Content.Shared.Symphony;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>
/// A report written in the feedback window reaches the database with its snapshot of the moment, the server refuses
/// the ones it should, the player can list what they sent, and a status staff set reaches them in game, at their
/// next login if they were away.
/// </summary>
[TestFixture]
public sealed class FeedbackTest : InteractionTest
{
    private const string Title = "Airlock ate my toolbox";
    private const string Description = "Walked through the bridge airlock and the toolbox in my hand was gone.";
    private const string LogMarker = "feedback test marker warning";

    private readonly List<FeedbackResultEvent> _answers = new();
    private readonly List<FeedbackHistoryEvent> _histories = new();

    [Test]
    public async Task ReportsAreStoredWithContext()
    {
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var db = Server.ResolveDependency<IServerDbManager>();
        var system = CEntMan.System<FeedbackSystem>();
        var controller = UiMan.GetUIController<FeedbackUIController>();
        var cooldown = cfg.GetCVar(FeedbackCVars.Cooldown);
        var dailyLimit = cfg.GetCVar(FeedbackCVars.DailyLimit);

        _answers.Clear();
        _histories.Clear();
        system.Answered += _answers.Add;
        system.History += _histories.Add;
        try
        {
            await Server.WaitPost(() =>
            {
                cfg.SetCVar(FeedbackCVars.Cooldown, 600);
                cfg.SetCVar(FeedbackCVars.DailyLimit, 0);
                Server.ResolveDependency<ILogManager>().GetSawmill("feedback.test").Warning(LogMarker);
            });

            // The window, opened the way the game menu opens it.
            await Client.WaitPost(() => controller.ToggleWindow());
            await RunTicks(10);
            FeedbackWindow window = default!;
            await Client.WaitAssertion(() =>
            {
                window = UiMan.WindowRoot.Children.OfType<FeedbackWindow>().Single();
            });

            // The frame from before the window opened, encoded off the main thread.
            var encoding = Stopwatch.StartNew();
            var ready = false;
            while (!ready && encoding.Elapsed < TimeSpan.FromSeconds(30))
            {
                await RunTicks(5);
                await Task.Delay(10);
                await Client.WaitPost(() => ready = window.AttachScreenshot);
            }

            Assert.That(ready, Is.True, "The screenshot never became ready.");

            // An empty form never leaves the client.
            await Client.WaitPost(() => window.Submit());
            await RunTicks(10);
            Assert.That(_answers, Is.Empty, "An empty form was sent.");

            await Client.WaitPost(() =>
            {
                window.FindControl<LineEdit>("TitleEdit").Text = Title;
                window.FindControl<TextEdit>("DescriptionEdit").TextRope = new Rope.Leaf(Description);
                window.FindControl<TextEdit>("StepsEdit").TextRope = new Rope.Leaf("1. Hold a toolbox.\n2. Walk through.");
                window.Submit();
            });

            var stored = await Answer();
            Assert.That(stored.Error, Is.Null);
            Assert.That(stored.ReportId, Is.GreaterThan(0));

            var report = await db.GetFeedbackReportAsync(stored.ReportId);
            Assert.That(report, Is.Not.Null);
            var roundId = Server.System<GameTicker>().RoundId;
            Assert.Multiple(() =>
            {
                Assert.That(report!.Kind, Is.EqualTo("bug"));
                Assert.That(report.Category, Is.EqualTo(FeedbackRules.DefaultCategory));
                Assert.That(report.Severity, Is.EqualTo("minor"));
                Assert.That(report.Title, Is.EqualTo(Title));
                Assert.That(report.Description, Is.EqualTo(Description));
                Assert.That(report.Steps, Is.EqualTo("1. Hold a toolbox.\n2. Walk through."));
                Assert.That(report.Expected, Is.Null);
                Assert.That(report.PlayerUserId, Is.EqualTo(ServerSession.UserId.UserId));
                Assert.That(report.PlayerName, Is.EqualTo(ServerSession.Name));
                Assert.That(report.RoundId, Is.EqualTo(roundId));
                Assert.That(report.Status, Is.EqualTo("pending"));
                Assert.That(report.NotifyPlayer, Is.False);
                Assert.That(report.Screenshot, Is.Not.Null);
                Assert.That(report.Screenshot!.Take(3), Is.EqualTo(new byte[] { 0xFF, 0xD8, 0xFF }), "The screenshot is not a JPEG.");
            });

            using var context = JsonDocument.Parse(report!.Context);
            var root = context.RootElement;
            var character = root.GetProperty("character");
            var position = character.GetProperty("position");
            var client = root.GetProperty("client");
            var serverLog = root.GetProperty("server_log").EnumerateArray().Select(l => l.GetProperty("message").GetString()).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("schema").GetInt32(), Is.EqualTo(1));
                Assert.That(root.GetProperty("build").GetProperty("symphony_module").GetInt32(), Is.EqualTo(SharedSymphony.ModuleVersion));
                Assert.That(root.GetProperty("server").GetProperty("round_id").GetInt32(), Is.EqualTo(roundId));
                Assert.That(root.GetProperty("reporter").GetProperty("user_id").GetString(), Is.EqualTo(ServerSession.UserId.UserId.ToString()));
                Assert.That(character.GetProperty("prototype").GetString(), Is.EqualTo(PlayerPrototype));
                Assert.That(character.GetProperty("entity").GetInt32(), Is.EqualTo(SEntMan.GetNetEntity(SPlayer).Id));
                Assert.That(character.GetProperty("components").EnumerateArray().Select(c => c.GetString()), Does.Contain("Hands"));
                Assert.That(position.GetProperty("grid").GetProperty("entity").GetInt32(), Is.EqualTo(SEntMan.GetNetEntity(MapData.Grid.Owner).Id));
                Assert.That(position.TryGetProperty("tile_type", out _), Is.True, "No tile under the player.");
                Assert.That(root.GetProperty("nearby").ValueKind, Is.EqualTo(JsonValueKind.Object));
                Assert.That(root.GetProperty("admin_log").ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(serverLog, Has.Some.Contains(LogMarker), "The server's warning is not in the report.");
                Assert.That(client.GetProperty("trusted").GetBoolean(), Is.False);
                Assert.That(client.GetProperty("screenshot").GetBoolean(), Is.True);
                Assert.That(client.GetProperty("info").GetProperty("state").GetString(), Is.Not.Empty);
                Assert.That(client.GetProperty("info").GetProperty("entity").GetString(), Is.EqualTo(SEntMan.GetNetEntity(SPlayer).Id.ToString()));
            });

            // Too soon after the last one.
            await Send(new FeedbackSubmitEvent { Title = Title, Description = Description });
            var early = await Answer();
            Assert.That(early.Error, Is.EqualTo("wf-feedback-error-cooldown"));
            Assert.That(early.RetrySeconds, Is.GreaterThan(0));

            await Server.WaitPost(() => cfg.SetCVar(FeedbackCVars.Cooldown, 0));

            // A report the window would never build.
            await Send(new FeedbackSubmitEvent { Title = Title, Description = Description, Category = "nonsense" });
            Assert.That((await Answer()).Error, Is.EqualTo("wf-feedback-error-invalid"));

            // A hand-built event with null fields is refused, not thrown on.
            await Send(new FeedbackSubmitEvent { Title = null!, Description = Description });
            Assert.That((await Answer()).Error, Is.EqualTo("wf-feedback-error-invalid"));

            // A suggestion with a screenshot, and text PostgreSQL would refuse as it is.
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 74, 70, 73, 70 };
            await Send(new FeedbackSubmitEvent
            {
                Kind = FeedbackKind.Suggestion,
                Category = "ships",
                Title = "Cup holders",
                Description = "Every pilot seat \0should have a cup holder.",
                Steps = "ignored for a suggestion",
                Screenshot = jpeg,
                ClientInfo = new Dictionary<string, string> { ["fps"] = "60.0" },
                ClientLog = new List<string> { "[ERRO] test: client line" },
            });

            var suggestion = await Answer();
            Assert.That(suggestion.Error, Is.Null);
            var second = await db.GetFeedbackReportAsync(suggestion.ReportId);
            Assert.Multiple(() =>
            {
                Assert.That(second!.Kind, Is.EqualTo("suggestion"));
                Assert.That(second.Category, Is.EqualTo("ships"));
                Assert.That(second.Severity, Is.Null);
                Assert.That(second.Steps, Is.Null);
                Assert.That(second.Description, Is.EqualTo("Every pilot seat should have a cup holder."));
                Assert.That(second.Screenshot, Is.EqualTo(jpeg));
                Assert.That(second.Context, Does.Contain("[ERRO] test: client line"));
            });

            // Not an image: the report is kept, the bytes are not.
            await Send(new FeedbackSubmitEvent { Title = Title, Description = Description, Screenshot = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 } });
            var plain = await Answer();
            Assert.That(plain.Error, Is.Null);
            Assert.That((await db.GetFeedbackReportAsync(plain.ReportId))!.Screenshot, Is.Null);

            await Server.WaitPost(() => cfg.SetCVar(FeedbackCVars.DailyLimit, 1));
            await Send(new FeedbackSubmitEvent { Title = Title, Description = Description });
            Assert.That((await Answer()).Error, Is.EqualTo("wf-feedback-error-daily"));

            await Server.WaitPost(() => cfg.SetCVar(FeedbackCVars.Enabled, false));
            await Send(new FeedbackSubmitEvent { Title = Title, Description = Description });
            Assert.That((await Answer()).Error, Is.EqualTo("wf-feedback-error-disabled"));

            // The player's own list, newest first, in the window's My reports tab.
            await Client.WaitPost(() => system.RequestHistory());
            await WaitFor(() => _histories.Count > 0, "The server did not send the player's reports.");
            var listed = _histories[0].Reports;
            Assert.That(listed.Select(r => r.Id), Is.EqualTo(new[] { plain.ReportId, suggestion.ReportId, stored.ReportId }));
            Assert.Multiple(() =>
            {
                Assert.That(listed[1].Kind, Is.EqualTo(FeedbackKind.Suggestion));
                Assert.That(listed[2].Title, Is.EqualTo(Title));
                Assert.That(listed.Select(r => r.Status), Is.All.EqualTo(FeedbackStatus.Pending));
            });
            await Client.WaitAssertion(() =>
            {
                Assert.That(window.FindControl<BoxContainer>("HistoryList").ChildCount, Is.EqualTo(3));
            });

            // Staff mark the bug fixed while the player is on: they are told now.
            var feedback = Server.System<Content.Server._WF.Feedback.FeedbackSystem>();
            Task<bool?> marking = default!;
            await Server.WaitPost(() => marking = feedback.SetStatusAsync(stored.ReportId, FeedbackStatus.Fixed, "Fixed in the next build.", true));
            Assert.That(await marking, Is.True, "The player is online but was not told.");

            var notice = await Notice();
            Assert.That(notice.Ids, Is.EqualTo(new[] { stored.ReportId }));
            var marked = await db.GetFeedbackReportAsync(stored.ReportId);
            Assert.Multiple(() =>
            {
                Assert.That(marked!.Status, Is.EqualTo("fixed"));
                Assert.That(marked.StaffMessage, Is.EqualTo("Fixed in the next build."));
                Assert.That(marked.StatusChangedAt, Is.Not.Null);
                Assert.That(marked.NotifyPlayer, Is.True, "Counted as seen before the player closed the notice.");
            });

            // Closing the notice is what counts as seeing it.
            await Client.WaitPost(() => notice.Close());
            var closed = Stopwatch.StartNew();
            while ((await db.GetFeedbackReportAsync(stored.ReportId))!.NotifyPlayer && closed.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Pair.RunTicksSync(5);
                await Task.Delay(10);
            }

            Assert.That((await db.GetFeedbackReportAsync(stored.ReportId))!.NotifyPlayer, Is.False,
                "Closing the notice did not mark the report seen.");

            // The panel writes a status itself while the player is away: they are told when they log in.
            var user = ServerSession.UserId.UserId;
            var name = ServerSession.Name;
            await Pair.Disconnect();
            Assert.That(await db.SetFeedbackStatusAsync(suggestion.ReportId, "non_issue", "Not planned.", true), Is.EqualTo(user));

            // Back in under the same name, so as the same account.
            Client.SetConnectTarget(Server);
            await Client.WaitPost(() => Client.ResolveDependency<IClientNetManager>().ClientConnect(null!, 0, name));
            await Pair.RunTicksSync(10);

            var atLogin = await Notice();
            Assert.That(atLogin.Ids, Is.EqualTo(new[] { suggestion.ReportId }));
        }
        finally
        {
            system.Answered -= _answers.Add;
            system.History -= _histories.Add;
            await Server.WaitPost(() =>
            {
                cfg.SetCVar(FeedbackCVars.Enabled, true);
                cfg.SetCVar(FeedbackCVars.Cooldown, cooldown);
                cfg.SetCVar(FeedbackCVars.DailyLimit, dailyLimit);
            });
        }
    }

    [Test]
    public async Task SurveyIsOfferedAndStored()
    {
        var clientCfg = Client.ResolveDependency<IConfigurationManager>();
        var db = Server.ResolveDependency<IServerDbManager>();
        var system = CEntMan.System<FeedbackSystem>();
        var controller = UiMan.GetUIController<FeedbackUIController>();
        var left = 0;

        _answers.Clear();
        system.Answered += _answers.Add;
        try
        {
            // Into cryo: the server offers the survey, and Not now just puts the chip away.
            await Server.WaitPost(() =>
                SEntMan.EventBus.RaiseLocalEvent(SPlayer, new CryosleepEnterEvent(SPlayer, ServerSession.UserId), true));
            var chip = await Chip();
            await Client.WaitPost(() => chip.Dismiss());
            await RunTicks(2);
            Assert.That(await FindChip(), Is.Null, "Not now left the chip up.");

            // Leaving: Never show again leaves at once and stops the offers.
            var offered = false;
            await Client.WaitPost(() => offered = controller.TryOfferSurveyBeforeLeaving(() => left++));
            Assert.That(offered, Is.True);
            Assert.That(left, Is.Zero, "Left before the player answered the chip.");
            chip = await Chip();
            await Client.WaitPost(() => chip.NeverAgain());
            Assert.That(left, Is.EqualTo(1));
            Assert.That(clientCfg.GetCVar(FeedbackCVars.SurveyNever), Is.True);
            await Client.WaitPost(() => offered = controller.TryOfferSurveyBeforeLeaving(() => left++));
            Assert.That(offered, Is.False, "Offered again after Never show again.");
            await Client.WaitPost(() => clientCfg.SetCVar(FeedbackCVars.SurveyNever, false));

            // Leaving again: Give feedback, and the player leaves once the survey is stored.
            await Client.WaitPost(() => offered = controller.TryOfferSurveyBeforeLeaving(() => left++));
            Assert.That(offered, Is.True);
            chip = await Chip();
            await Client.WaitPost(() => chip.Accept());
            await RunTicks(2);
            FeedbackSurveyWindow survey = default!;
            await Client.WaitAssertion(() => survey = UiMan.WindowRoot.Children.OfType<FeedbackSurveyWindow>().Single());

            // No rating: nothing is sent.
            await Client.WaitPost(() => survey.Submit());
            await RunTicks(5);
            Assert.That(_answers, Is.Empty);

            await Client.WaitPost(() =>
            {
                survey.SetRating(4);
                survey.FindControl<TextEdit>("IssuesEdit").TextRope = new Rope.Leaf("The shuttle console froze once.");
                survey.FindControl<TextEdit>("FrustrationsEdit").TextRope = new Rope.Leaf("Waiting for a medic.");
                survey.Submit();
            });

            var answer = await Answer();
            Assert.That(answer.Error, Is.Null);
            Assert.That(left, Is.EqualTo(2), "The player did not leave after sending the survey.");

            var stored = await db.GetFeedbackReportAsync(answer.ReportId);
            Assert.Multiple(() =>
            {
                Assert.That(stored!.Kind, Is.EqualTo("survey"));
                Assert.That(stored.Rating, Is.EqualTo(4));
                Assert.That(stored.Title, Is.EqualTo("Shift survey: 4/5"));
                Assert.That(stored.Description, Is.EqualTo("The shuttle console froze once."));
                Assert.That(stored.Frustrations, Is.EqualTo("Waiting for a medic."));
                Assert.That(stored.Severity, Is.Null);
                Assert.That(stored.Status, Is.EqualTo("pending"));
            });

            using (var context = JsonDocument.Parse(stored!.Context))
            {
                var root = context.RootElement;
                Assert.That(root.GetProperty("survey").GetProperty("trigger").GetString(), Is.EqualTo("leaving"));
                Assert.That(root.GetProperty("character").GetProperty("prototype").GetString(), Is.EqualTo(PlayerPrototype));
            }

            // One survey a round.
            await Client.WaitPost(() => offered = controller.TryOfferSurveyBeforeLeaving(() => left++));
            Assert.That(offered, Is.False, "Offered again after a survey was sent.");
            await Send(new FeedbackSubmitEvent { Kind = FeedbackKind.Survey, Rating = 2 });
            Assert.That((await Answer()).Error, Is.EqualTo("wf-feedback-error-survey-done"));

            await Server.WaitPost(() =>
                SEntMan.EventBus.RaiseLocalEvent(SPlayer, new CryosleepEnterEvent(SPlayer, ServerSession.UserId), true));
            await RunTicks(10);
            Assert.That(await FindChip(), Is.Null, "Offered on cryo after a survey was sent.");
        }
        finally
        {
            system.Answered -= _answers.Add;
            await Client.WaitPost(() => clientCfg.SetCVar(FeedbackCVars.SurveyNever, false));
        }
    }

    private async Task<FeedbackSurveyChip?> FindChip()
    {
        FeedbackSurveyChip? chip = null;
        await Client.WaitPost(() => chip = UiMan.WindowRoot.Children.OfType<FeedbackSurveyChip>().FirstOrDefault());
        return chip;
    }

    /// <summary>The survey chip, once the client has put it up.</summary>
    private async Task<FeedbackSurveyChip> Chip()
    {
        FeedbackSurveyChip? chip = null;
        var watch = Stopwatch.StartNew();
        while (chip == null && watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Pair.RunTicksSync(5);
            chip = await FindChip();
        }

        Assert.That(chip, Is.Not.Null, "The client did not show the survey chip.");
        return chip!;
    }

    private async Task Send(FeedbackSubmitEvent report)
    {
        await Client.WaitPost(() => CEntMan.System<FeedbackSystem>().Submit(report));
    }

    /// <summary>The server's next answer.</summary>
    private async Task<FeedbackResultEvent> Answer()
    {
        await WaitFor(() => _answers.Count > 0, "The server did not answer the report.");
        Assert.That(_answers, Has.Count.EqualTo(1));
        var answer = _answers[0];
        _answers.Clear();
        return answer;
    }

    /// <summary>The status notice, once the client has opened it.</summary>
    private async Task<FeedbackNoticeWindow> Notice()
    {
        FeedbackNoticeWindow? notice = null;
        var watch = Stopwatch.StartNew();
        while (notice == null && watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Pair.RunTicksSync(5);
            await Task.Delay(10);
            await Client.WaitPost(() => notice = UiMan.WindowRoot.Children.OfType<FeedbackNoticeWindow>().FirstOrDefault());
        }

        Assert.That(notice, Is.Not.Null, "The client did not show the status notice.");
        return notice!;
    }

    /// <summary>Storing and reading reports is real work, so waits are bounded by time, not ticks.</summary>
    private async Task WaitFor(Func<bool> done, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Pair.RunTicksSync(5);
            await Task.Delay(10);
        }

        Assert.That(done(), Is.True, message);
    }
}
