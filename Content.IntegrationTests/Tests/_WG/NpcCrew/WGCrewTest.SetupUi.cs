#nullable enable
using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WG.NpcCrew;
using Content.Shared._WG.NpcCrew;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

/// <summary>Records the actual serialized admin requests sent by the crew window.</summary>
public sealed partial class WGCrewSetupRequestObserver : EntitySystem
{
    [Dependency] private INetManager _network = default!;
    public readonly ConcurrentQueue<WGCrewSetupRequest> Requests = new();

    public override void Initialize()
    {
        base.Initialize();
        if (!_network.IsClient)
            SubscribeNetworkEvent<WGCrewSetupRequest>((request, _) => Requests.Enqueue(request));
    }
}

public sealed partial class WGCrewTest
{
    private const BindingFlags UiPrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    private static T UiField<T>(object owner, string name) => (T) owner.GetType().GetField(name, UiPrivate)!.GetValue(owner)!;
    private static object? UiCall(object owner, string method, params object?[] args) => owner.GetType().GetMethod(method, UiPrivate)!.Invoke(owner, args);
    private static object? UiProperty(object owner, string name) => owner.GetType().GetProperty(name, UiPrivate)!.GetValue(owner);
    private static List<WGCrewObjective>? UiDraft(WGCrewSetupWindow window)
    {
        var edits = UiProperty(window, "CurrentEdits");
        return edits?.GetType().GetField("Queue")!.GetValue(edits) as List<WGCrewObjective>;
    }

    private async Task WithCrewWindow(Action<WGCrewSetupWindow> assertion)
    {
        await Client.WaitAssertion(() =>
        {
            var window = new WGCrewSetupWindow();
            try
            {
                window.OpenCentered();
                assertion(window);
            }
            finally
            {
                window.Close();
                window.Teardown();
            }
        });
        await RunTicks(5);
    }

    private static WGCrewSetupResponse UiSnapshot(params WGCrewSetupCrew[] crews) => new()
    {
        Action = WGCrewSetupAction.List,
        Grids = crews.Select(crew => new WGCrewSetupGrid(crew.Grid, crew.Grid.ToString())).ToList(),
        Crews = crews.ToList(),
    };

    /// <summary>A delayed save cannot clear another crew's draft or newer edits in its original window.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CrewSetupIgnoresStaleQueueAcknowledgement(bool switchCrew)
    {
        await WithCrewWindow(window =>
        {
            var first = new WGCrewSetupCrew { Grid = new NetEntity(101), Group = "crew",
                Objectives = new() { new() { Kind = WGCrewObjectiveKind.Hold } } };
            var second = new WGCrewSetupCrew { Grid = new NetEntity(102), Group = "crew",
                Objectives = new() { new() { Kind = WGCrewObjectiveKind.Repair } } };
            UiCall(window, "Receive", UiSnapshot(first, second));
            window.SelectCrew(first.Grid, first.Group);
            UiCall(window, "BeginQueueEdit");
            UiCall(window, "SendQueue", WGCrewSetupAction.Objectives);
            var requestId = UiField<IDictionary>(window, "_requests").Keys.Cast<int>().Single();
            if (switchCrew)
            {
                window.SelectCrew(second.Grid, second.Group);
                UiCall(window, "BeginQueueEdit");
            }
            else
            {
                UiField<OptionButton>(window, "_objectiveKind").SelectId((int) WGCrewObjectiveKind.Undock);
                UiCall(window, "AddObjective");
            }
            var draft = UiDraft(window);
            Assert.That(draft, Is.Not.Null);
            UiCall(window, "Receive", new WGCrewSetupResponse
            {
                RequestId = requestId, Action = WGCrewSetupAction.Objectives, Grid = first.Grid, Crews = new() { first, second },
            });
            Assert.That(UiDraft(window), Is.SameAs(draft), "An older save must not mark another editing revision as saved.");
            Assert.That(((WGCrewSetupCrew) UiProperty(window, "CurrentCrew")!).Grid, Is.EqualTo(switchCrew ? second.Grid : first.Grid));
            Assert.That(draft!.Last().Kind, Is.EqualTo(switchCrew ? WGCrewObjectiveKind.Repair : WGCrewObjectiveKind.Undock));
            if (switchCrew)
            {
                window.SelectCrew(first.Grid, first.Group);
                Assert.That(UiDraft(window), Is.Null, "The original crew's acknowledged draft should be marked saved.");
            }
        });
    }

    /// <summary>Repeated clicks send one mutation until its reply arrives, then allow a deliberate retry.</summary>
    [TestCase(WGCrewSetupAction.Spawn)]
    [TestCase(WGCrewSetupAction.SpawnVessel)]
    [TestCase(WGCrewSetupAction.AppendObjective)]
    [TestCase(WGCrewSetupAction.Skip)]
    public async Task CrewSetupDeduplicatesPendingActions(WGCrewSetupAction action)
    {
        await WithCrewWindow(window =>
        {
            var first = new WGCrewSetupRequest { Action = action, Grid = new NetEntity(101) };
            var duplicate = new WGCrewSetupRequest { Action = action, Grid = first.Grid };
            UiCall(window, "Request", first);
            UiCall(window, "Request", duplicate);
            Assert.That(first.RequestId, Is.GreaterThan(0));
            Assert.That(duplicate.RequestId, Is.Zero);
            Assert.That(UiField<IDictionary>(window, "_requests"), Has.Count.EqualTo(1));
            UiCall(window, "Receive", new WGCrewSetupResponse { RequestId = first.RequestId, Action = action, Message = "Test rejection" });
            UiCall(window, "Request", duplicate);
            Assert.That(duplicate.RequestId, Is.GreaterThan(first.RequestId));
        });
    }

    /// <summary>A new vessel requires a new layout instead of reusing another ship's planned posts.</summary>
    [Test]
    public async Task CrewSetupNewVesselClearsOldRoster()
    {
        await WithCrewWindow(window =>
        {
            var source = new NetEntity(101);
            typeof(WGCrewSetupWindow).GetField("_creationGrid", UiPrivate)!.SetValue(window, source);
            UiCall(window, "AddRow", new WGCrewSetupPost { Position = new Vector2(4.5f) });
            var request = new WGCrewSetupRequest { Action = WGCrewSetupAction.SpawnVessel };
            UiCall(window, "Request", request);
            var spawned = new NetEntity(102);
            UiCall(window, "Receive", new WGCrewSetupResponse { RequestId = request.RequestId, Action = request.Action, Grid = spawned });
            Assert.That(UiField<object>(window, "_creationGrid"), Is.EqualTo(spawned));
            Assert.That(UiField<IList>(window, "_rows"), Is.Empty);
        });
    }

    /// <summary>A newer plan remains usable even if an earlier plan response is still pending.</summary>
    [Test]
    public async Task CrewSetupNewestPlanWins()
    {
        await WithCrewWindow(window =>
        {
            var source = new NetEntity(101);
            typeof(WGCrewSetupWindow).GetField("_creationGrid", UiPrivate)!.SetValue(window, source);
            UiCall(window, "PlanCrew");
            UiCall(window, "PlanCrew");
            var requests = UiField<IDictionary>(window, "_requests").Keys.Cast<int>().Order().ToArray();
            Assert.That(requests, Has.Length.EqualTo(2));
            UiCall(window, "Receive", new WGCrewSetupResponse
            {
                RequestId = requests[0], Action = WGCrewSetupAction.Plan, Grid = source,
                Posts = new() { new() { Position = new Vector2(1.5f) } },
            });
            Assert.That(UiField<IList>(window, "_rows"), Is.Empty);
            UiCall(window, "Receive", new WGCrewSetupResponse
            {
                RequestId = requests[1], Action = WGCrewSetupAction.Plan, Grid = source,
                Posts = new() { new() { Position = new Vector2(2.5f) }, new() { Position = new Vector2(3.5f) } },
            });
            Assert.That(UiField<IList>(window, "_rows"), Has.Count.EqualTo(2));
        });
    }

    /// <summary>Live queue progress must not overwrite an explicitly edited draft.</summary>
    [Test]
    public async Task CrewSetupPollingPreservesQueueDraft()
    {
        await WithCrewWindow(window =>
        {
            var crew = new WGCrewSetupCrew { Grid = new NetEntity(101), Group = "crew",
                Objectives = new() { new() { Kind = WGCrewObjectiveKind.Hold } } };
            UiCall(window, "Receive", UiSnapshot(crew));
            window.SelectCrew(crew.Grid, crew.Group);
            UiCall(window, "BeginQueueEdit");
            UiField<OptionButton>(window, "_objectiveKind").SelectId((int) WGCrewObjectiveKind.Repair);
            UiCall(window, "AddObjective");
            var draft = UiDraft(window);
            UiCall(window, "Receive", UiSnapshot(new WGCrewSetupCrew { Grid = crew.Grid, Group = crew.Group }));
            Assert.That(UiDraft(window), Is.SameAs(draft));
            Assert.That(draft!.Select(item => item.Kind), Is.EqualTo(new[] { WGCrewObjectiveKind.Hold, WGCrewObjectiveKind.Repair }));
        });
    }

    /// <summary>A disappearing source never silently retargets creation or commands to the remaining ship.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CrewSetupMissingShipClearsSelection(bool creating)
    {
        await WithCrewWindow(window =>
        {
            var first = new WGCrewSetupCrew { Grid = new NetEntity(101), Group = "crew" };
            var second = new WGCrewSetupCrew { Grid = new NetEntity(102), Group = "crew" };
            UiCall(window, "Receive", UiSnapshot(first, second));
            if (creating)
            {
                typeof(WGCrewSetupWindow).GetField("_creationGrid", UiPrivate)!.SetValue(window, first.Grid);
                UiCall(window, "RefreshCreationGrid");
                UiCall(window, "AddRow", new WGCrewSetupPost());
            }
            else
                window.SelectCrew(first.Grid, first.Group);
            UiCall(window, "Receive", UiSnapshot(second));
            if (creating)
            {
                Assert.That(UiField<object?>(window, "_creationGrid"), Is.Null);
                Assert.That(UiField<OptionButton>(window, "_grid").SelectedId, Is.EqualTo(-1));
                Assert.That(UiField<IList>(window, "_rows"), Is.Empty);
                Assert.That(UiField<Button>(window, "_next").Disabled, Is.True);
            }
            else
            {
                Assert.That(UiProperty(window, "CurrentCrew"), Is.Null);
                Assert.That(UiField<BoxContainer>(window, "_manageView").Visible, Is.False);
                Assert.That(UiField<BoxContainer>(window, "_emptyView").Visible, Is.True);
                UiCall(window, "SendQueue", WGCrewSetupAction.Pause);
                Assert.That(UiField<IDictionary>(window, "_requests"), Is.Empty, "No request should target the remaining crew.");
            }
        });
    }

    /// <summary>Unused task inputs cannot invalidate repair or undocking after another task was edited.</summary>
    [TestCase(WGCrewObjectiveKind.Repair)]
    [TestCase(WGCrewObjectiveKind.Undock)]
    public async Task CrewSetupNormalizesHiddenTaskParameters(WGCrewObjectiveKind kind)
    {
        await WithCrewWindow(window =>
        {
            foreach (var name in new[] { "_objectiveX", "_objectiveY", "_objectiveRange", "_duration" })
                UiField<LineEdit>(window, name).Text = "unfinished";
            UiField<OptionButton>(window, "_objectiveKind").SelectId((int) kind);
            UiCall(window, "UpdateObjectiveFields");
            var objective = (WGCrewObjective) UiCall(window, "ReadObjective")!;
            Assert.That(objective.Target, Is.Null);
            Assert.That(objective.Position, Is.EqualTo(Vector2.Zero));
            Assert.That(float.IsFinite(objective.Range) && objective.Range >= 1, Is.True);
            Assert.That(objective.Duration, Is.Zero);
            Assert.That(UiCall(window, "ValidateObjective", objective), Is.True);
        });
    }

    /// <summary>Creation sends its own settings and Hold even after unrelated live orders were left unfinished.</summary>
    [Test]
    public async Task CrewSetupCreationIsIndependentOfManualOrders()
    {
        var observer = Server.System<WGCrewSetupRequestObserver>();
        observer.Requests.Clear();
        await WithCrewWindow(window =>
        {
            var ship = new WGCrewSetupCrew { Grid = new NetEntity(101), Group = "existing" };
            UiCall(window, "Receive", UiSnapshot(ship));
            typeof(WGCrewSetupWindow).GetField("_creationGrid", UiPrivate)!.SetValue(window, ship.Grid);
            UiCall(window, "RefreshCreationGrid");
            UiField<LineEdit>(window, "_group").Text = "creation-payload";
            UiCall(window, "AddRow", new WGCrewSetupPost { Position = new Vector2(2.5f) });
            UiField<OptionButton>(window, "_order").SelectId((int) WGPilotOrder.Dock);
            UiCall(window, "UpdateOrderFields");
            UiField<LineEdit>(window, "_range").Text = "unfinished";
            UiField<LineEdit>(window, "_x").Text = "unfinished";
            UiField<LineEdit>(window, "_y").Text = "unfinished";
            UiField<LineEdit>(UiField<object>(window, "_crewSettings"), "_callsign").Text = "Wrong live settings";
            UiField<LineEdit>(UiField<object>(window, "_createSettings"), "_callsign").Text = "New test crew";
            UiCall(window, "SendCreation", WGCrewSetupAction.Preview, null);
        });
        await RunTicks(15);
        var request = observer.Requests.Single(item => item.Action == WGCrewSetupAction.Preview && item.Mission.Group == "creation-payload");
        Assert.That(request.Mission.Callsign, Is.EqualTo("New test crew"));
        Assert.That(request.Mission.Order, Is.EqualTo(WGPilotOrder.Hold));
        Assert.That(request.Mission.Target, Is.Null);
        Assert.That(request.Mission.Destination, Is.EqualTo(Vector2.Zero));
        Assert.That(float.IsFinite(request.Mission.Range), Is.True);
        Assert.That(request.Posts, Has.Count.EqualTo(1));
        Assert.That(request.RequestId, Is.GreaterThan(0));
    }
}
