#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>Remote pilots acquire their helm and timed queues advance, append, pause and skip independently.</summary>
    [Test]
    public async Task RemoteCrewRunsEditableQueue()
    {
        var deck = await CreateDeck(new Vector2(2000, 2000), 7, gravity: true);
        EntityUid pilot = default;
        await Server.WaitAssertion(() =>
        {
            SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(3.5f)));
            pilot = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Pilot,
                new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "remote")!.Value;
            SEntMan.GetComponent<HTNComponent>(pilot).SleepPlayerCheckRangeOverride = 1;
            var objectives = Server.System<WFCrewObjectiveSystem>();
            Assert.That(objectives.SetQueue(deck, "remote", new List<WFCrewObjective>
            {
                new() { Kind = WFCrewObjectiveKind.Hold, Duration = 1 },
                new() { Kind = WFCrewObjectiveKind.Hold },
            }), Is.True);
        });
        await WaitUntil(() => Server.System<WFCrewObjectiveSystem>().Snapshot().Single(row => row.Group == "remote").Objectives.Count == 1,
            900, () => Describe(pilot));
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFPilotDutySystem>().IsAtHelm(pilot), Is.True);
            var objectives = Server.System<WFCrewObjectiveSystem>();
            Assert.That(objectives.SetQueue(deck, "remote", new List<WFCrewObjective>
                { new() { Kind = WFCrewObjectiveKind.Hold, Duration = 1 } }, append: true), Is.True);
            objectives.Control(deck, "remote", WFCrewSetupAction.Pause);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var objectives = Server.System<WFCrewObjectiveSystem>();
            Assert.That(objectives.Snapshot().Single(row => row.Group == "remote").Objectives.Count, Is.EqualTo(2));
            Assert.That(objectives.SetQueue(deck, "remote", new List<WFCrewObjective>
                { new() { Kind = WFCrewObjectiveKind.Dock, Target = SEntMan.GetNetEntity(deck) } }), Is.False);
            objectives.Control(deck, "remote", WFCrewSetupAction.Skip);
        });
        await WaitUntil(() => Server.System<WFCrewObjectiveSystem>().Snapshot().Single(row => row.Group == "remote").Objectives.Count == 0,
            300, () => Describe(pilot));
    }
}
