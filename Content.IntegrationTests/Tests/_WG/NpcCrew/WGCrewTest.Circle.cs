#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Grid orbit tasks require a distinct target and steer around its bounds center.</summary>
    [TestCase(WGCrewObjectiveKind.Circle, 150f, 4f)]
    [TestCase(WGCrewObjectiveKind.Attack, 350f, 6f)]
    public async Task CrewOrbitQueueUsesTargetCenter(WGCrewObjectiveKind kind, float expectedRadius, float expectedSpeed)
    {
        var deck = await CreateDeck(new Vector2(20, 20), 7, gravity: true);
        var target = await CreateDeck(new Vector2(500, 20), 9, gravity: true);
        EntityUid pilot = default;
        await Server.WaitAssertion(() =>
        {
            pilot = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Pilot,
                new EntityCoordinates(deck, new Vector2(2.5f)), "orbit")!.Value;
            SEntMan.GetComponent<HTNComponent>(pilot).Enabled = false;
            var objectives = Server.System<WGCrewObjectiveSystem>();
            var item = new WGCrewObjective { Kind = kind, Range = 150, Duration = 30 };
            Assert.That(objectives.SetQueue(deck, "orbit", new List<WGCrewObjective> { item }), Is.False);
            item.Target = SEntMan.GetNetEntity(deck);
            Assert.That(objectives.SetQueue(deck, "orbit", new List<WGCrewObjective> { item }), Is.False);
            item.Target = SEntMan.GetNetEntity(target);
            Assert.That(objectives.SetQueue(deck, "orbit", new List<WGCrewObjective> { item }), Is.True);
        });
        await WaitUntil(() => SEntMan.GetComponent<WGPilotDutyComponent>(pilot).Orders == WGPilotOrder.Loiter,
            90, () => Describe(pilot));
        await Server.WaitAssertion(() =>
        {
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(pilot);
            Assert.That(duty.LoiterCenter, Is.EqualTo(new EntityCoordinates(target,
                SEntMan.GetComponent<MapGridComponent>(target).LocalAABB.Center)));
            Assert.That(duty.LoiterRadius, Is.EqualTo(expectedRadius));
            Assert.That(duty.LoiterSpeed, Is.EqualTo(expectedSpeed));
            Assert.That(Server.System<WGCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == "orbit")
                .Objectives.Single().Kind, Is.EqualTo(kind));
        });
    }

    /// <summary>Circle exposes its target, radius and timer, while loaded edits retain their chosen values.</summary>
    [Test]
    public async Task CrewCircleEditorDefaultsPreserveEdits()
    {
        await WithCrewWindow(window =>
        {
            var crew = new WGCrewSetupCrew { Grid = new NetEntity(101), Group = "orbit" };
            var target = new WGCrewSetupCrew { Grid = new NetEntity(102), Group = "target" };
            UiCall(window, "Receive", UiSnapshot(crew, target));
            window.SelectCrew(crew.Grid, crew.Group);
            var selector = UiField<OptionButton>(window, "_objectiveKind");
            selector.SelectId((int) WGCrewObjectiveKind.Circle);
            UiCall(window, "SelectObjectiveKind");
            UiField<OptionButton>(window, "_objectiveTarget").SelectId(1);
            UiField<LineEdit>(window, "_duration").Text = "42";
            var objective = (WGCrewObjective) UiCall(window, "ReadObjective")!;
            Assert.That(objective.Kind, Is.EqualTo(WGCrewObjectiveKind.Circle));
            Assert.That(objective.Target, Is.EqualTo(target.Grid));
            Assert.That(objective.Range, Is.EqualTo(150));
            Assert.That(objective.Duration, Is.EqualTo(42));
            Assert.That(UiField<BoxContainer>(window, "_objectiveTargetLine").Visible, Is.True);
            Assert.That(UiField<BoxContainer>(window, "_objectiveRangeLine").Visible, Is.True);
            Assert.That(UiField<BoxContainer>(window, "_objectiveTimingLine").Visible, Is.True);
            selector.SelectId((int) WGCrewObjectiveKind.Attack);
            UiCall(window, "SelectObjectiveKind");
            Assert.That(UiField<LineEdit>(window, "_objectiveRange").Text, Is.EqualTo("350"));
            objective.Range = 275;
            UiCall(window, "EditObjective", 0, objective);
            UiCall(window, "UpdateObjectiveFields");
            Assert.That(UiField<LineEdit>(window, "_objectiveRange").Text, Is.EqualTo("275"));
            Assert.That(UiField<LineEdit>(window, "_duration").Text, Is.EqualTo("42"));
        });
    }
}
