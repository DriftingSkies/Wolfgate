using Content.Server._WF.TestDummies;
using Content.Shared._WF.TestDummies;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.TestDummies;

/// <summary>Tests audience matching, sending and answer bookkeeping of the test dummy call.</summary>
[TestFixture]
public sealed class WFTestDummyTest
{
    [Test]
    public async Task AudienceAndAnswers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var dummies = server.System<WFTestDummySystem>();
        var player = pair.Player!;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var body = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(player, body);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(dummies.Matches(player, WFTestDummyAudience.InBody));
                Assert.That(dummies.Matches(player, WFTestDummyAudience.InSpace));
                Assert.That(dummies.Matches(player, WFTestDummyAudience.OwnsShip), Is.False, "no deed, no ship");
                Assert.That(dummies.Matches(player, WFTestDummyAudience.None));
            });

            // Nobody owns a ship, so the default audience is empty.
            Assert.That(dummies.TrySend(null, new WFTestDummyRequest(), out _), Is.EqualTo("wf-test-dummy-error-nobody"));
            Assert.That(dummies.TrySend(null, new WFTestDummyRequest { Duration = -1 }, out _), Is.EqualTo("wf-test-dummy-error-bad-duration"));
            Assert.That(dummies.TrySend(null, new WFTestDummyRequest
            {
                Description = new string('a', WFTestDummyLimits.MaxDescription + 1),
            }, out _), Is.EqualTo("wf-test-dummy-error-too-long"));

            var error = dummies.TrySend(null, new WFTestDummyRequest
            {
                Description = "shield test",
                Audience = WFTestDummyAudience.InBody | WFTestDummyAudience.InSpace,
            }, out var asked);
            Assert.Multiple(() =>
            {
                Assert.That(error, Is.Null);
                Assert.That(asked, Is.EqualTo(1));
                Assert.That(dummies.Active);
            });

            var state = dummies.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(state.Entries, Has.Count.EqualTo(1));
                Assert.That(state.Entries[0].User, Is.EqualTo(player.UserId));
                Assert.That(state.Entries[0].Answer, Is.EqualTo(WFTestDummyAnswer.Pending));
                Assert.That(state.Description, Is.EqualTo("shield test"));
            });
        });

        // The client answers yes.
        var client = pair.Client;
        var callId = dummies.CallId;
        await client.WaitPost(() => client.EntMan.RaisePredictiveEvent(new WFTestDummyAnswerEvent(callId, true)));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.That(dummies.Snapshot().Entries[0].Answer, Is.EqualTo(WFTestDummyAnswer.OptedIn)));

        // A second answer, and one for a stale call, change nothing.
        await client.WaitPost(() =>
        {
            client.EntMan.RaisePredictiveEvent(new WFTestDummyAnswerEvent(callId, false));
            client.EntMan.RaisePredictiveEvent(new WFTestDummyAnswerEvent(callId - 1, false));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(dummies.Snapshot().Entries[0].Answer, Is.EqualTo(WFTestDummyAnswer.OptedIn), "a repeated or stale answer must not count");

            dummies.End();
            Assert.That(dummies.Active, Is.False);
            Assert.That(dummies.Snapshot().Entries, Has.Count.EqualTo(1), "ending keeps the list");
        });

        // An answer after the call ended changes nothing either.
        await client.WaitPost(() => client.EntMan.RaisePredictiveEvent(new WFTestDummyAnswerEvent(callId, false)));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
            Assert.That(dummies.Snapshot().Entries[0].Answer, Is.EqualTo(WFTestDummyAnswer.OptedIn), "an answer after End must not count"));

        await pair.CleanReturnAsync();
    }
}
