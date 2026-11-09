using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Client._WF.Census;
using Content.Server._WF.Census;
using Content.Server.Database;
using Content.Shared._WF.Census;
using Robust.Client.UserInterface;
using ClientCensusSystem = Content.Client._WF.Census.CensusSystem;
using ServerCensusSystem = Content.Server._WF.Census.CensusSystem;

namespace Content.IntegrationTests.Tests._WF.Census;

/// <summary>
/// A census the panel opens reaches a player in the lobby, who has the window opened for them once, answers it
/// once and is refused what breaks the rules; questions are normalised before the panel's has_responses check.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerCensusSystem))]
public sealed class CensusTest
{
    private const string Questions = """
        [{"id":"q1","kind":"single","prompt":"Favourite map?","options":["Frontier","Coda"],"required":true},
         {"id":"q2","kind":"multi","prompt":"Which do you play?","options":["Mining","Salvage","Trade"],"min":1,"max":2,"extra":"ignored"},
         {"id":"q3","kind":"text","prompt":"Anything else?","maxLength":10}]
        """;

    [Test]
    public void QuestionsAreCheckedAndNormalised()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Parse(Questions, out var problem), Is.Not.Null, problem);
            Assert.That(Parse("[]", out _), Is.Null, "No questions.");
            Assert.That(Parse("""[{"id":"Q1","kind":"text","prompt":"x"}]""", out _), Is.Null, "Upper case id.");
            Assert.That(Parse("""[{"id":"a","kind":"text","prompt":"x"},{"id":"a","kind":"text","prompt":"y"}]""", out _), Is.Null, "Duplicate id.");
            Assert.That(Parse("""[{"id":"a","kind":"single","prompt":"x","options":["one"]}]""", out _), Is.Null, "One option.");
            Assert.That(Parse("""[{"id":"a","kind":"single","prompt":"x","options":["one","one"]}]""", out _), Is.Null, "Duplicate option.");
            Assert.That(Parse("""[{"id":"a","kind":"multi","prompt":"x","options":["a","b"],"min":2,"max":1}]""", out _), Is.Null, "min > max.");
            Assert.That(Parse("""[{"id":"a","kind":"text","prompt":"x","maxLength":2001}]""", out _), Is.Null, "maxLength too long.");
        });

        // The panel sends defaults filled in; that is the same census.
        var bare = Parse("""[{"id":"a","kind":"multi","prompt":"x","options":["a","b"]},{"id":"b","kind":"text","prompt":"y"}]""", out _)!;
        var full = Parse("""[{"id":"a","kind":"multi","prompt":"x","options":["a","b"],"required":false,"min":0,"max":2},{"id":"b","kind":"text","prompt":"y","required":false,"maxLength":500,"multiline":false}]""", out _)!;
        Assert.That(CensusQuestions.Write(bare), Is.EqualTo(CensusQuestions.Write(full)));
        Assert.That(CensusQuestions.Clean("  a\u0001b\nc\u0007  ", 10, false), Is.EqualTo("ab c"));

        // Answers are stored as the contract writes them: sorted indexes, text trimmed and cut to maxLength.
        var answers = new CensusSubmitEvent();
        answers.Choices["q1"] = new List<int> { 1 };
        answers.Choices["q2"] = new List<int> { 2, 0 };
        answers.Texts["q3"] = "  far too long an answer  ";
        var json = CensusQuestions.Answer(Parse(Questions, out _)!, answers, out var error, out _, out _);
        Assert.That(error, Is.Null);
        Assert.That(json, Is.EqualTo("""{"q1":[1],"q2":[0,2],"q3":"far too lo"}"""));
    }

    [Test]
    public async Task PlayersAnswerOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var census = server.System<ServerCensusSystem>();
        var db = server.ResolveDependency<IServerDbManager>();
        var client = pair.Client.System<ClientCensusSystem>();
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        var controller = ui.GetUIController<CensusUIController>();
        var results = new List<CensusResultEvent>();
        client.Answered += results.Add;
        int? id = null;
        try
        {
            var save = new CensusSave
            {
                Title = "Map census",
                Questions = Parse(Questions, out _)!,
                State = CensusRules.StateOpen,
                Actor = "test",
            };

            var (outcome, row) = await Run(() => census.SaveAsync(save));
            Assert.That(outcome, Is.EqualTo(CensusSaveOutcome.Saved));
            id = row!.Census.Id;

            // In the lobby the window opens by itself, and the count shows on the buttons.
            await WaitFor(() => controller.Unanswered == 1 && ui.WindowRoot.Children.OfType<CensusWindow>().Any());

            var answers = new CensusSubmitEvent { CensusId = id.Value };
            answers.Choices["q2"] = new List<int> { 2, 0 };
            answers.Texts["q3"] = "  far too long an answer  ";
            await Submit(answers);
            Assert.That(results[^1].Error, Is.EqualTo("wf-census-error-required"), "q1 is required.");

            answers.Choices["q1"] = new List<int> { 1 };
            answers.Choices["q2"] = new List<int> { 0, 1, 2 };
            await Submit(answers);
            Assert.That(results[^1].Error, Is.EqualTo("wf-census-error-too-many"));

            answers.Choices["q2"] = new List<int> { 2, 0 };
            await Submit(answers);
            Assert.That(results[^1].Error, Is.Null);
            await WaitFor(() => controller.Unanswered == 0);

            await Submit(answers);
            Assert.That(results[^1].Error, Is.EqualTo("wf-census-error-answered"));

            var stored = await Run(() => db.GetCensusAsync(id.Value));
            Assert.That(stored!.Responses, Is.EqualTo(1));

            // With an answer in, the questions may not change, but the same questions sent back in full may.
            save.Id = id;
            save.Questions = Parse(Questions.Replace("Favourite map?", "Best map?"), out _)!;
            var (refused, _) = await Run(() => census.SaveAsync(save));
            Assert.That(refused, Is.EqualTo(CensusSaveOutcome.HasResponses));

            save.Questions = Parse(CensusQuestions.Write(Parse(Questions, out _)!), out _)!;
            save.Title = "Map census, closed";
            save.State = CensusRules.StateClosed;
            var (closed, closedRow) = await Run(() => census.SaveAsync(save));
            Assert.That(closed, Is.EqualTo(CensusSaveOutcome.Saved));
            Assert.That(closedRow!.Census.ClosedAt, Is.Not.Null);
        }
        finally
        {
            client.Answered -= results.Add;
            if (id is { } remove)
                await Run(() => census.DeleteAsync(remove));
        }

        await pair.CleanReturnAsync();

        async Task Submit(CensusSubmitEvent ev)
        {
            var before = results.Count;
            await pair.Client.WaitPost(() => client.Submit(ev));
            await WaitFor(() => results.Count > before);
        }

        async Task<T> Run<T>(Func<Task<T>> start)
        {
            Task<T> task = null!;
            await server.WaitPost(() => task = start());
            for (var i = 0; i < 600 && !task.IsCompleted; i++)
            {
                await pair.RunTicksSync(1);
                await Task.Delay(5);
            }

            Assert.That(task.IsCompleted, Is.True, "Server task timed out.");
            return await task;
        }

        async Task WaitFor(Func<bool> condition)
        {
            var done = false;
            for (var i = 0; i < 600 && !done; i++)
            {
                await pair.Client.WaitPost(() => done = condition());
                if (!done)
                {
                    await pair.RunTicksSync(1);
                    await Task.Delay(10);
                }
            }

            Assert.That(done, Is.True);
        }
    }

    // No nullable annotations: CI builds the integration tests with nullable warnings as errors.
    private static List<CensusQuestion> Parse(string json, out string problem)
    {
        using var doc = JsonDocument.Parse(json);
        var parsed = CensusQuestions.Parse(doc.RootElement, out var found);
        problem = found!;
        return parsed!;
    }
}
