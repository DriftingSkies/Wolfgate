#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
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
            pilot = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Pilot,
                new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "remote")!.Value;
            SEntMan.GetComponent<HTNComponent>(pilot).SleepPlayerCheckRangeOverride = 1;
            var objectives = Server.System<WGCrewObjectiveSystem>();
            Assert.That(objectives.SetQueue(deck, "remote", new List<WGCrewObjective>
            {
                new() { Kind = WGCrewObjectiveKind.Hold, Duration = 1 },
                new() { Kind = WGCrewObjectiveKind.Hold },
            }), Is.True);
        });
        await WaitUntil(() => Server.System<WGCrewObjectiveSystem>().Snapshot().Single(row => row.Group == "remote").Objectives.Count == 1,
            900, () => Describe(pilot));
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGPilotDutySystem>().IsAtHelm(pilot), Is.True);
            var objectives = Server.System<WGCrewObjectiveSystem>();
            Assert.That(objectives.SetQueue(deck, "remote", new List<WGCrewObjective>
                { new() { Kind = WGCrewObjectiveKind.Hold, Duration = 1 } }, append: true), Is.True);
            objectives.Control(deck, "remote", WGCrewSetupAction.Pause);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var objectives = Server.System<WGCrewObjectiveSystem>();
            Assert.That(objectives.Snapshot().Single(row => row.Group == "remote").Objectives.Count, Is.EqualTo(2));
            Assert.That(objectives.SetQueue(deck, "remote", new List<WGCrewObjective>
                { new() { Kind = WGCrewObjectiveKind.Dock, Target = SEntMan.GetNetEntity(deck) } }), Is.False);
            objectives.Control(deck, "remote", WGCrewSetupAction.Skip);
        });
        await WaitUntil(() => Server.System<WGCrewObjectiveSystem>().Snapshot().Single(row => row.Group == "remote").Objectives.Count == 0,
            300, () => Describe(pilot));
    }
}
