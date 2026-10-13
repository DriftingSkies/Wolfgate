#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._NF.BountyContracts;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Hailing;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.Hailing;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>
/// Extorters: a ship that closes on a player ship and hails it for a toll. Paying moves the toll from the payer's
/// account onto the extorter's warrant and buys the ship peace from every extorter; refusing, being unable to pay,
/// hanging up, letting three hails go by or having no line to ring gets the ship boarded as a hunter boards its prey,
/// and the extorter roams again after.
/// </summary>
public sealed partial class WFEncounterTest
{
    private const int ExtortToll = 5000;
    private const int ExtortBase = 10000;

    [TestPrototypes]
    private const string ExtortPrototypes = @"
- type: wfEncounter
  id: WFTestExtorter
  name: wf-encounter-name-convoy
  start: Manual
  lifetime: Persistent
  bounty:
    ship: extorter
    target: Crew
    spesos: 10000
    title: wf-bounty-title-extorter-light
    description: wf-bounty-desc-extorter-light
  ships:
  - key: extorter
    vessel: WFDredger
    side: pirates
    deckhands: 1
    guards: 1
    wander: 2
    wanderRadius: 1000
    extort:
      toll: 5000
      reach: 2000
      markMin: 0
      markMax: 0

- type: wfEncounter
  id: WFTestExtorterGreedy
  name: wf-encounter-name-convoy
  start: Manual
  lifetime: Persistent
  bounty:
    ship: extorter
    target: Crew
    spesos: 10000
    title: wf-bounty-title-extorter-light
    description: wf-bounty-desc-extorter-light
  ships:
  - key: extorter
    vessel: WFDredger
    side: pirates
    deckhands: 1
    guards: 1
    wander: 2
    wanderRadius: 1000
    extort:
      toll: 2000000000
      reach: 2000
      markMin: 0
      markMax: 0
";

    /// <summary>An extorter beside the test player's ship, with everything a test needs to drive both ends.</summary>
    private sealed class ExtortScene
    {
        public EntityUid? Board;
        public EntityUid Encounter;
        public EntityUid PlayerShip;
        public EntityUid Helm;
        public WFEncounterShipState Ship = default!;
        public WFExtortState Extort = default!;
    }

    /// <summary>
    /// Puts the test player at the powered helm of a small ship with money in the bank, starts an extorter 400 m off and
    /// waits for its first hail to ring. With <paramref name="answer"/> the player answers it. Without
    /// <paramref name="board"/> the sector has no bounty board.
    /// </summary>
    private async Task<ExtortScene> StartExtortion(string prototype, Vector2 at, bool answer, bool board = true)
    {
        var scene = new ExtortScene();
        var ui = SEntMan.System<SharedUserInterfaceSystem>();
        await Server.WaitAssertion(() =>
        {
            if (board)
                scene.Board = EnsureBoard();
            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            scene.PlayerShip = grid.Owner;
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            Server.System<SharedMapSystem>().SetTiles(grid, tiles);
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(scene.PlayerShip, new EntityCoordinates(MapData.MapUid, at));
            scene.Helm = SEntMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(scene.PlayerShip, new Vector2(1.5f, 0.5f)));
            // The power update is throttled, so don't wait on it to notice.
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(scene.Helm);
            power.NeedsPower = false;
            power.Powered = true;
            transform.SetCoordinates(SPlayer, new EntityCoordinates(scene.PlayerShip, new Vector2(1.5f, 1.5f)));

            // Enough in the bank for the ordinary toll, whatever earlier tests left there.
            SEntMan.EnsureComponent<BankAccountComponent>(SPlayer);
            var bank = Server.System<BankSystem>();
            Assert.That(bank.TryGetBalance(SPlayer, out var balance), Is.True, "The test player has an account.");
            if (balance < ExtortToll * 4)
                Assert.That(bank.TryBankDeposit(SPlayer, ExtortToll * 4 - balance, false), Is.True);
        });

        // A console already open when it first reaches the client is a different engine path from real play.
        await RunTicks(5);
        await Server.WaitPost(() => ui.OpenUi(scene.Helm, ShuttleConsoleUiKey.Key, SPlayer));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ui.IsUiOpen(scene.Helm, ShuttleConsoleUiKey.Key, SPlayer), Is.True, "The player needs the helm open to hold a call.");
            var proto = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(prototype);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(proto, new MapCoordinates(at + new Vector2(400f, 0f), MapData.MapId),
                out scene.Encounter), Is.True);
            scene.Ship = SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter).Ships["extorter"];
            Assert.That(scene.Ship.Extort, Is.Not.Null, "The ship is an extorter.");
            scene.Extort = scene.Ship.Extort!;
            Assert.That(scene.Ship.HasOrders, Is.False, "Its wander is its own roaming, not orders to fly out.");
            Assert.That(SEntMan.HasComponent<WFHoldFireComponent>(scene.Ship.Grid), Is.True, "Its guns answer ship guns only.");

            PowerHelms(scene.Ship.Grid);
        });

        await WaitUntilServer(() => scene.Extort.Phase == WFExtortPhase.Calling, 900);
        await Server.WaitAssertion(() =>
        {
            var hailing = Server.System<HailingSystem>();
            Assert.That(scene.Extort.Mark, Is.EqualTo(scene.PlayerShip), "It went for the player's ship.");
            Assert.That(scene.Extort.Attempts, Is.EqualTo(1));
            Assert.That(hailing.GetState(scene.Helm), Is.EqualTo(HailState.Ringing), "Its hail rings at the player's helm.");
            Assert.That(scene.Extort.Console, Is.Not.Null);
            Assert.That(SEntMan.GetComponent<HailConsoleComponent>(scene.Extort.Console!.Value).Automated, Is.True);
            Assert.That(Server.System<WFEncounterSystem>().ExtortStatus(scene.Ship), Does.Contain("1"), "The admin's line names the attempt.");
            var orders = ExtortOrders(scene);
            Assert.That(orders.Single().Kind, Is.EqualTo(WFCrewObjectiveKind.Follow), "It keeps station on its mark.");
            Assert.That(orders[0].Target, Is.EqualTo(SEntMan.GetNetEntity(scene.PlayerShip)));
            if (!answer)
                return;

            hailing.Answer(scene.Helm, SPlayer, true);
            Assert.That(scene.Extort.Phase, Is.EqualTo(WFExtortPhase.Demand), "Answered, it names its toll.");
            var hail = SEntMan.GetComponent<HailConsoleComponent>(scene.Helm);
            Assert.That(hail.Options.Select(option => option.Id), Is.EqualTo(new[] { WFEncounterSystem.ExtortPay, WFEncounterSystem.ExtortRefuse }));
            Assert.That(hail.Log.Count(line => !line.Own), Is.EqualTo(1), "The demand is said down the channel.");
            Assert.That(hail.Log[0].Text, Does.Contain(scene.Extort.Config.Toll.ToString("N0")).Or.Contain(scene.Extort.Config.Toll.ToString()),
                "The demand names the toll.");
        });

        return scene;
    }

    /// <summary>Powers a ship's helm consoles at once; the power update is throttled.</summary>
    private void PowerHelms(EntityUid grid)
    {
        var helms = SEntMan.EntityQueryEnumerator<ShuttleConsoleComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (helms.MoveNext(out _, out _, out var power, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            power.NeedsPower = false;
            power.Powered = true;
        }
    }

    private List<WFCrewObjective> ExtortOrders(ExtortScene scene)
    {
        return Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == scene.Ship.Group).Objectives;
    }

    /// <summary>A message from the player through the helm's own interface, so the subscription is covered too.</summary>
    private void RaiseHelm(EntityUid helm, BoundUserInterfaceMessage message)
    {
        message.Actor = SPlayer;
        message.UiKey = ShuttleConsoleUiKey.Key;
        message.Entity = SEntMan.GetNetEntity(helm);
        SEntMan.EventBus.RaiseLocalEvent(helm, (object) message);
    }

    /// <summary>Runs a test with hails that ring out after a second and may be made again a second later.</summary>
    private async Task WithShortHails(Func<Task> test)
    {
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var timeout = cfg.GetCVar(HailingCVars.RingTimeout);
        var retry = cfg.GetCVar(HailingCVars.RetryDelay);
        await Server.WaitPost(() =>
        {
            cfg.SetCVar(HailingCVars.RingTimeout, 1f);
            cfg.SetCVar(HailingCVars.RetryDelay, 1f);
        });

        try
        {
            await test();
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                cfg.SetCVar(HailingCVars.RingTimeout, timeout);
                cfg.SetCVar(HailingCVars.RetryDelay, retry);
            });
        }
    }

    private List<EntityUid> ExtortCrew(WFEncounterShipState ship)
    {
        var crew = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<WFCrewComponent>();
        while (query.MoveNext(out var uid, out var member))
        {
            if (member.Group == ship.Group)
                crew.Add(uid);
        }

        return crew;
    }

    private int ExtortBalance()
    {
        Assert.That(Server.System<BankSystem>().TryGetBalance(SPlayer, out var balance), Is.True);
        return balance;
    }

    private void AssertBoarding(ExtortScene scene)
    {
        Assert.That(scene.Extort.Phase, Is.EqualTo(WFExtortPhase.Board));
        Assert.That(scene.Ship.Prey, Is.EqualTo(scene.PlayerShip), "The hunt has the ship as its prey.");
        var orders = ExtortOrders(scene);
        Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[]
        {
            WFCrewObjectiveKind.Loot, WFCrewObjectiveKind.Hold, WFCrewObjectiveKind.Undock, WFCrewObjectiveKind.GoTo,
        }), "The boarders' raid: dock and loot, stay a while, cast off, fly clear.");
        Assert.That(orders[0].Target, Is.EqualTo(SEntMan.GetNetEntity(scene.PlayerShip)));
        Assert.That(scene.Ship.Raided, Is.False);
        Assert.That(SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter).Resolution, Is.Null);
    }

    private async Task EndExtortion(ExtortScene scene)
    {
        await Server.WaitAssertion(() =>
        {
            Server.System<WFEncounterSystem>().End(scene.Encounter);
            SendPlayerHome(scene.PlayerShip);
            if (scene.Board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// Paying takes the toll from the payer's account and puts it on the warrant and its board entry. The ship is left
    /// alone after, and the extorter flies off to roam.
    /// </summary>
    [Test]
    public async Task ExtorterTakesItsTollAndLeavesThePayerAlone()
    {
        var scene = await StartExtortion("WFTestExtorter", new Vector2(40000, -40000), true);
        await Server.WaitAssertion(() =>
        {
            var bounties = Server.System<WFEncounterBountySystem>();
            var contracts = Server.System<BountyContractSystem>();
            var id = SEntMan.GetComponent<WFEncounterBountyComponent>(scene.Encounter).Contract!.Value;
            Assert.That(bounties.Pool(scene.Encounter), Is.EqualTo(ExtortBase));
            var before = ExtortBalance();

            RaiseHelm(scene.Helm, new HailChooseMessage(WFEncounterSystem.ExtortPay));

            Assert.That(before - ExtortBalance(), Is.EqualTo(ExtortToll), "The toll leaves the payer's account.");
            Assert.That(bounties.Pool(scene.Encounter), Is.EqualTo(ExtortBase + ExtortToll), "And goes on the warrant.");
            Assert.That(contracts.TryGetContract(id, out var contract), Is.True);
            Assert.That(contract!.Reward, Is.EqualTo(ExtortBase + ExtortToll), "The board entry shows it.");
            Assert.That(scene.Extort.Phase, Is.EqualTo(WFExtortPhase.Roam));
            Assert.That(scene.Extort.Mark, Is.Null);
            Assert.That(scene.Extort.Tolls, Is.EqualTo(1));
            Assert.That(scene.Extort.Collected, Is.EqualTo(ExtortToll));
            Assert.That(scene.Extort.Spared.ContainsKey(scene.PlayerShip), Is.True, "The ship that paid is left alone.");
            Assert.That(scene.Ship.Prey, Is.Null);
            var orders = ExtortOrders(scene);
            Assert.That(orders, Is.Not.Empty);
            Assert.That(orders.All(order => order.Kind is WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Hold), Is.True, "It has orders to roam.");
            Assert.That((orders[0].Position - Server.System<SharedTransformSystem>().GetWorldPosition(scene.PlayerShip)).Length(),
                Is.GreaterThan(2000f), "First it flies clear of the ship that paid.");
            var said = SEntMan.GetComponent<HailConsoleComponent>(scene.Helm).Log;
            Assert.That(said.Count(line => !line.Own), Is.EqualTo(2), "It acknowledges the payment.");

            var system = Server.System<WFEncounterSystem>();
            var detail = system.StateDetail(scene.Encounter, SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter), "Active", true);
            Assert.That(detail, Does.StartWith("Active").And.Contain(system.ExtortStatus(scene.Ship)!).And.Contain("pool"),
                "The admin's line says what it is doing and what its warrant pays.");
        });

        // It is due another toll at once, and the only ship about is the one that paid.
        await RunTicks(500);
        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Extort.Phase, Is.EqualTo(WFExtortPhase.Roam), "It does not come back for a ship that paid.");
            Assert.That(scene.Extort.Mark, Is.Null);
            Assert.That(scene.Extort.Attempts, Is.Zero);
            Assert.That(Server.System<HailingSystem>().GetState(scene.Helm), Is.Null, "It closed the channel.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter).Resolution, Is.Null, "It stays in the sector.");
            Assert.That(Server.System<WFEncounterBountySystem>().Pool(scene.Encounter), Is.EqualTo(ExtortBase + ExtortToll));
        });
        await EndExtortion(scene);
    }

    /// <summary>A refusal hands the ship to the hunt: it is boarded as a hunter's prey is, and no money moves.</summary>
    [Test]
    public async Task ExtorterBoardsAShipThatRefuses()
    {
        var scene = await StartExtortion("WFTestExtorter", new Vector2(40000, -46000), true);
        await Server.WaitAssertion(() =>
        {
            var before = ExtortBalance();
            RaiseHelm(scene.Helm, new HailChooseMessage(WFEncounterSystem.ExtortRefuse));
            Assert.That(ExtortBalance(), Is.EqualTo(before), "Refusing costs nothing.");
            Assert.That(Server.System<WFEncounterBountySystem>().Pool(scene.Encounter), Is.EqualTo(ExtortBase));
            AssertBoarding(scene);
            Assert.That(scene.Extort.Raids, Is.EqualTo(1));
        });
        await EndExtortion(scene);
    }

    /// <summary>Three hails that ring out or are declined, and it boards: after the third, not the second, and with no fourth.</summary>
    [Test]
    public async Task ExtorterBoardsAfterThreeUnansweredHails()
    {
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var timeout = cfg.GetCVar(HailingCVars.RingTimeout);
        var retry = cfg.GetCVar(HailingCVars.RetryDelay);
        await Server.WaitPost(() =>
        {
            cfg.SetCVar(HailingCVars.RingTimeout, 1f);
            cfg.SetCVar(HailingCVars.RetryDelay, 1f);
        });

        try
        {
            var scene = await StartExtortion("WFTestExtorter", new Vector2(40000, -52000), false);
            var hailing = Server.System<HailingSystem>();
            var most = 0;
            var early = false;
            var declined = false;
            for (var waited = 0; waited < 2400 && scene.Extort.Phase != WFExtortPhase.Board; waited += 10)
            {
                await RunTicks(10);
                await Server.WaitPost(() =>
                {
                    most = Math.Max(most, scene.Extort.Attempts);
                    early |= scene.Extort.Phase == WFExtortPhase.Board && scene.Extort.Attempts < WFEncounterSystem.ExtortHails;
                    // The second hail is turned down by hand; the others ring out.
                    if (!declined && scene.Extort.Attempts == 2 && hailing.GetState(scene.Helm) == HailState.Ringing)
                    {
                        declined = true;
                        hailing.Answer(scene.Helm, SPlayer, false);
                    }
                });
            }

            await Server.WaitAssertion(() =>
            {
                Assert.That(declined, Is.True, "The second hail was there to decline.");
                Assert.That(early, Is.False, "It does not board before its third hail.");
                Assert.That(scene.Extort.Attempts, Is.EqualTo(WFEncounterSystem.ExtortHails), "Three hails, no more and no fewer.");
                Assert.That(most, Is.EqualTo(WFEncounterSystem.ExtortHails));
                AssertBoarding(scene);
                Assert.That(hailing.GetState(scene.Helm), Is.Null, "There is no fourth hail.");
            });

            await RunTicks(300);
            await Server.WaitAssertion(() =>
            {
                Assert.That(scene.Extort.Attempts, Is.EqualTo(WFEncounterSystem.ExtortHails));
                Assert.That(scene.Extort.Phase, Is.EqualTo(WFExtortPhase.Board));
                Assert.That(hailing.GetState(scene.Helm), Is.Null);
            });
            await EndExtortion(scene);
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                cfg.SetCVar(HailingCVars.RingTimeout, timeout);
                cfg.SetCVar(HailingCVars.RetryDelay, retry);
            });
        }
    }

    /// <summary>A ship that agrees to a toll it cannot cover keeps its money and is boarded.</summary>
    [Test]
    public async Task ExtorterBoardsAShipThatCannotPay()
    {
        var scene = await StartExtortion("WFTestExtorterGreedy", new Vector2(40000, -58000), true);
        await Server.WaitAssertion(() =>
        {
            var before = ExtortBalance();
            Assert.That(before, Is.LessThan(scene.Extort.Config.Toll), "The test needs a toll beyond the account.");
            RaiseHelm(scene.Helm, new HailChooseMessage(WFEncounterSystem.ExtortPay));
            Assert.That(ExtortBalance(), Is.EqualTo(before), "No part of a toll is taken.");
            Assert.That(Server.System<WFEncounterBountySystem>().Pool(scene.Encounter), Is.EqualTo(ExtortBase));
            Assert.That(scene.Extort.Tolls, Is.Zero);
            Assert.That(scene.Extort.Spared.ContainsKey(scene.PlayerShip), Is.False);
            Assert.That(SEntMan.GetComponent<HailConsoleComponent>(scene.Helm).Log.Count(line => !line.Own), Is.EqualTo(2),
                "It says the account is short.");
            AssertBoarding(scene);
        });
        await EndExtortion(scene);
    }

    /// <summary>Closing the channel with the toll unanswered is a refusal.</summary>
    [Test]
    public async Task ExtorterBoardsAShipThatHangsUp()
    {
        var scene = await StartExtortion("WFTestExtorter", new Vector2(40000, -64000), true);
        await Server.WaitAssertion(() =>
        {
            var before = ExtortBalance();
            RaiseHelm(scene.Helm, new HailEndMessage());
            Assert.That(Server.System<HailingSystem>().GetState(scene.Helm), Is.Null, "The player closed the channel.");
            Assert.That(ExtortBalance(), Is.EqualTo(before));
            AssertBoarding(scene);
        });
        await EndExtortion(scene);
    }

    /// <summary>A ship that answers and then picks nothing in time has refused: the answers come down and it is boarded.</summary>
    [Test]
    public async Task ExtorterBoardsAShipThatDoesNotChoose()
    {
        var scene = await StartExtortion("WFTestExtorter", new Vector2(40000, -94000), true);
        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Extort.Phase, Is.EqualTo(WFExtortPhase.Demand), "It gives the ship time to answer.");
            Assert.That(Server.System<WFEncounterSystem>().ExtortStatus(scene.Ship), Is.EqualTo(
                Loc.GetString("wf-encounter-extort-status-demand", ("ship", SEntMan.GetComponent<MetaDataComponent>(scene.PlayerShip).EntityName))));
        });
        await WaitUntilServer(() => scene.Extort.Phase == WFExtortPhase.Board, 3600);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent(scene.Helm, out HailConsoleComponent? hail), Is.True, "The channel stays open a moment for its last line.");
            Assert.That(hail!.Options, Is.Empty, "The answers are taken down.");
            Assert.That(hail.Log.Count(line => !line.Own), Is.EqualTo(2), "It says the time is up.");
            AssertBoarding(scene);
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
            Assert.That(Server.System<HailingSystem>().GetState(scene.Helm), Is.Null, "Then it closes the channel."));
        await EndExtortion(scene);
    }

    /// <summary>
    /// A raid flown to its end puts the extorter back to roaming: the encounter is not over, the ship it raided is left
    /// alone a while, and it has orders to wander.
    /// </summary>
    [Test]
    public async Task ExtorterRoamsAgainAfterItsRaid()
    {
        var scene = await StartExtortion("WFTestExtorter", new Vector2(40000, -70000), true);
        await Server.WaitAssertion(() =>
        {
            RaiseHelm(scene.Helm, new HailChooseMessage(WFEncounterSystem.ExtortRefuse));
            AssertBoarding(scene);
            // The raid's orders stand in for a raid flown: a moment's hold, then nothing left.
            Assert.That(Server.System<WFCrewObjectiveSystem>().SetQueue(scene.Ship.Grid, scene.Ship.Group, new List<WFCrewObjective>
            {
                new() { Kind = WFCrewObjectiveKind.Hold, Duration = 1f },
            }), Is.True);
        });
        await WaitUntilServer(() => scene.Extort.Phase == WFExtortPhase.Roam, 900);
        await Server.WaitAssertion(() =>
        {
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter);
            Assert.That(encounter.Resolution, Is.Null, "A finished raid does not finish the encounter.");
            Assert.That(scene.Ship.Raided, Is.False);
            Assert.That(scene.Ship.HasOrders, Is.False);
            Assert.That(scene.Ship.Prey, Is.Null);
            Assert.That(scene.Ship.Boarded, Is.Null);
            Assert.That(scene.Extort.Mark, Is.Null);
            Assert.That(scene.Extort.Spared.ContainsKey(scene.PlayerShip), Is.True, "The ship it raided is left alone a while.");
            var orders = ExtortOrders(scene);
            Assert.That(orders, Is.Not.Empty);
            Assert.That(orders.All(order => order.Kind is WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Hold), Is.True, "It has orders to roam.");
            Assert.That(Server.System<WFEncounterBountySystem>().Pool(scene.Encounter), Is.EqualTo(ExtortBase), "Its warrant stands.");
        });

        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Extort.Phase, Is.EqualTo(WFExtortPhase.Roam), "It does not go straight back for the same ship.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter).Resolution, Is.Null);
        });
        await EndExtortion(scene);
    }

    /// <summary>A ship it cannot get a port on is given up after three runs, and the extorter roams again.</summary>
    [Test]
    public async Task ExtorterGivesUpAShipItCannotDockWith()
    {
        var scene = await StartExtortion("WFTestExtorter", new Vector2(40000, -76000), true);
        await Server.WaitAssertion(() =>
        {
            RaiseHelm(scene.Helm, new HailChooseMessage(WFEncounterSystem.ExtortRefuse));
            AssertBoarding(scene);
        });

        for (var run = 0; run < 6 && scene.Extort.Phase == WFExtortPhase.Board; run++)
        {
            await Server.WaitPost(() =>
            {
                var pilots = SEntMan.EntityQueryEnumerator<WFPilotDutyComponent, WFCrewComponent>();
                while (pilots.MoveNext(out var uid, out _, out var crew))
                {
                    if (crew.Group != scene.Ship.Group)
                        continue;

                    var failed = new WFPilotDockFailedEvent(uid, scene.Ship.Grid, scene.PlayerShip);
                    SEntMan.EventBus.RaiseEvent(EventSource.Local, ref failed);
                    break;
                }
            });
            await RunTicks(160);
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Extort.Phase, Is.EqualTo(WFExtortPhase.Roam), "It gives the ship up.");
            Assert.That(scene.Ship.Prey, Is.Null);
            Assert.That(scene.Extort.Spared.ContainsKey(scene.PlayerShip), Is.True);
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter).Resolution, Is.Null);
        });
        await EndExtortion(scene);
    }

    /// <summary>
    /// A ship alongside with no working helm console cannot be rung, and that is no way out: each stretch its line
    /// stays dead is one of its three hails, and after the third it is boarded.
    /// </summary>
    [Test]
    public async Task ExtorterBoardsAShipItCannotRaise()
    {
        await WithShortHails(async () =>
        {
            EntityUid? board = null;
            EntityUid grid = default, encounter = default;
            WFEncounterShipState ship = default!;
            await Server.WaitAssertion(() =>
            {
                board = EnsureBoard();
                grid = PutPlayerOnGrid(new Vector2(40000, -88000));
                var proto = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestExtorter");
                Assert.That(Server.System<WFEncounterSystem>().TrySpawn(proto, new MapCoordinates(new Vector2(40400, -88000), MapData.MapId),
                    out encounter), Is.True);
                ship = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["extorter"];
                PowerHelms(ship.Grid);
            });

            var early = false;
            for (var waited = 0; waited < 3000 && ship.Extort!.Phase != WFExtortPhase.Board; waited += 10)
            {
                await RunTicks(10);
                await Server.WaitPost(() =>
                    early |= ship.Extort!.Phase == WFExtortPhase.Board && ship.Extort.Attempts < WFEncounterSystem.ExtortHails);
            }

            await Server.WaitAssertion(() =>
            {
                var extort = ship.Extort!;
                Assert.That(early, Is.False, "It does not board before the third.");
                Assert.That(extort.Phase, Is.EqualTo(WFExtortPhase.Board), "No console is no way out of the toll.");
                Assert.That(extort.Attempts, Is.EqualTo(WFEncounterSystem.ExtortHails), "A dead line is a hail gone by.");
                Assert.That(extort.Raids, Is.EqualTo(1));
                Assert.That(ship.Prey, Is.EqualTo(grid));
                Assert.That(extort.Spared.ContainsKey(grid), Is.False, "It is not left alone for having no console.");

                Server.System<WFEncounterSystem>().End(encounter);
                SendPlayerHome(grid);
                if (board is { } host)
                    SEntMan.DeleteEntity(host);
            });
            await RunTicks(10);
        });
    }

    /// <summary>Cutting the helm's power after the first hail does not stop the count: the ship is boarded after three.</summary>
    [Test]
    public async Task ExtorterBoardsAShipThatCutsItsLine()
    {
        await WithShortHails(async () =>
        {
            var scene = await StartExtortion("WFTestExtorter", new Vector2(46000, -40000), false);
            await Server.WaitPost(() =>
            {
                var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(scene.Helm);
                power.NeedsPower = true;
                power.Powered = false;
            });
            await WaitUntilServer(() => scene.Extort.Phase is WFExtortPhase.Board or WFExtortPhase.Roam, 3000);
            await Server.WaitAssertion(() =>
            {
                Assert.That(scene.Extort.Attempts, Is.EqualTo(WFEncounterSystem.ExtortHails), "The hail that rang and two dead lines.");
                Assert.That(scene.Extort.Spared.ContainsKey(scene.PlayerShip), Is.False, "A dead console buys no peace.");
                AssertBoarding(scene);
            });
            await EndExtortion(scene);
        });
    }

    /// <summary>A ship it means to board and cannot get alongside in ten minutes has outrun it: it roams again.</summary>
    [Test]
    public async Task ExtorterGivesUpAShipItCannotCatch()
    {
        var scene = await StartExtortion("WFTestExtorter", new Vector2(46000, -46000), true);
        await Server.WaitAssertion(() =>
        {
            RaiseHelm(scene.Helm, new HailChooseMessage(WFEncounterSystem.ExtortRefuse));
            AssertBoarding(scene);
            scene.Extort.PhaseSince -= WFEncounterSystem.ExtortChaseTime;
        });
        await WaitUntilServer(() => scene.Extort.Phase == WFExtortPhase.Roam, 900);
        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Ship.Prey, Is.Null);
            Assert.That(scene.Extort.Spared.ContainsKey(scene.PlayerShip), Is.True, "The ship that got away is left alone a while.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter).Resolution, Is.Null);
        });
        await EndExtortion(scene);
    }

    /// <summary>
    /// Extorters keep off each other's ships: a second one does not go for a ship the first is asking for its toll,
    /// nor for one that has paid the first.
    /// </summary>
    [Test]
    public async Task ExtortersLeaveEachOthersShipsAlone()
    {
        var at = new Vector2(46000, -52000);
        var scene = await StartExtortion("WFTestExtorter", at, true);
        EntityUid second = default;
        WFEncounterShipState other = default!;
        await Server.WaitAssertion(() =>
        {
            var proto = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestExtorter");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(proto, new MapCoordinates(at - new Vector2(400f, 0f), MapData.MapId),
                out second), Is.True);
            other = SEntMan.GetComponent<WFEncounterComponent>(second).Ships["extorter"];
            PowerHelms(other.Grid);
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.Extort.Phase, Is.EqualTo(WFExtortPhase.Demand), "The first still has its toll in front of the ship.");
            Assert.That(other.Extort!.Mark, Is.Null, "The second does not go for a ship the first is working.");
            Assert.That(other.Extort.Phase, Is.EqualTo(WFExtortPhase.Roam));

            RaiseHelm(scene.Helm, new HailChooseMessage(WFEncounterSystem.ExtortPay));
            Assert.That(scene.Extort.Spared.ContainsKey(scene.PlayerShip), Is.True);
            // The second is due a toll run at once.
            other.Extort.NextMark = TimeSpan.Zero;
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            Assert.That(other.Extort!.Mark, Is.Null, "A toll paid to one buys peace from the other.");
            Assert.That(other.Extort.Phase, Is.EqualTo(WFExtortPhase.Roam));
            Assert.That(other.Extort.Attempts, Is.Zero);
            Assert.That(Server.System<HailingSystem>().GetState(scene.Helm), Is.Null, "Nobody is hailing the ship.");
            Server.System<WFEncounterSystem>().End(second);
        });
        await EndExtortion(scene);
    }

    /// <summary>With no bounty board up the warrant is not posted but still stands, and a toll paid goes on it.</summary>
    [Test]
    public async Task TollGoesOnAWarrantWithNoBoard()
    {
        var scene = await StartExtortion("WFTestExtorter", new Vector2(46000, -58000), true, board: false);
        await Server.WaitAssertion(() =>
        {
            var bounties = Server.System<WFEncounterBountySystem>();
            var bounty = SEntMan.GetComponent<WFEncounterBountyComponent>(scene.Encounter);
            Assert.That(bounty.Contract, Is.Null, "The test needs a sector with no board.");
            Assert.That(bounty.State, Is.EqualTo(WFBountyState.Open));
            var before = ExtortBalance();
            RaiseHelm(scene.Helm, new HailChooseMessage(WFEncounterSystem.ExtortPay));
            Assert.That(before - ExtortBalance(), Is.EqualTo(ExtortToll));
            Assert.That(bounties.Pool(scene.Encounter), Is.EqualTo(ExtortBase + ExtortToll), "The toll is not lost for want of a board.");
        });
        await EndExtortion(scene);
    }

    /// <summary>
    /// Hands left aboard a ship that jumps away with them come off the warrant while the extorter still has crew
    /// aboard. With everyone aboard it dead they are the crew it has left: the warrant stays open until they fall.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task HandsLeftBehindComeOffTheWarrantOnlyWhileTheShipIsCrewed(bool crewed)
    {
        var scene = await StartExtortion("WFTestExtorter", new Vector2(46000, crewed ? -64000 : -70000), true);
        EntityUid left = default, away = default;
        await Server.WaitAssertion(() =>
        {
            RaiseHelm(scene.Helm, new HailChooseMessage(WFEncounterSystem.ExtortRefuse));
            AssertBoarding(scene);

            // The raid as it stands once docked: one guard posted aboard the prey, and the prey jumps away with him.
            var transform = Server.System<SharedTransformSystem>();
            var crew = ExtortCrew(scene.Ship);
            left = crew.First(uid => SEntMan.GetComponent<WFCrewComponent>(uid).Role == WFCrewRoles.Marine);
            var member = SEntMan.GetComponent<WFCrewComponent>(left);
            var post = new EntityCoordinates(scene.PlayerShip, new Vector2(0.5f, 0.5f));
            scene.Ship.BoardingParty[left] = member.Post;
            member.NextPatrol = TimeSpan.MaxValue;
            transform.SetCoordinates(left, post);
            Server.System<WFCrewSystem>().SetPost((left, member), post);
            // The prey here is a bare deck with no gravity: pinned, so he is not carried off it going after its crew.
            Server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetBodyType(left, Robust.Shared.Physics.BodyType.Static);
            foreach (var uid in crew)
            {
                if (!crewed && uid != left)
                    Server.System<MobStateSystem>().ChangeMobState(uid, MobState.Dead);
            }

            scene.Ship.Boarded = scene.PlayerShip;
            scene.Ship.RaidEnds = TimeSpan.MaxValue;
            away = Server.System<SharedMapSystem>().CreateMap(out _);
            transform.SetCoordinates(scene.PlayerShip, new EntityCoordinates(away, Vector2.Zero));
        });
        await WaitUntilServer(() => scene.Extort.Phase == WFExtortPhase.Roam, 900);
        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            var bounty = SEntMan.GetComponent<WFEncounterBountyComponent>(scene.Encounter);
            Assert.That(SEntMan.GetComponent<TransformComponent>(left).GridUid, Is.EqualTo(scene.PlayerShip), "He was left aboard the prey.");
            Assert.That(bounty.Crew.Contains(left), Is.EqualTo(!crewed));
            Assert.That(SEntMan.HasComponent<WFEncounterBountyTargetComponent>(left), Is.EqualTo(!crewed));
            Assert.That(bounty.State, Is.EqualTo(WFBountyState.Open), "Nobody has taken the crew yet.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter).Resolution, Is.Null);
            if (!crewed)
                Server.System<MobStateSystem>().ChangeMobState(left, MobState.Dead);
        });
        await RunTicks(100);
        await Server.WaitAssertion(() =>
        {
            var bounty = SEntMan.GetComponent<WFEncounterBountyComponent>(scene.Encounter);
            Assert.That(bounty.State, Is.EqualTo(crewed ? WFBountyState.Open : WFBountyState.Claimed),
                "The last of them dead, the warrant is taken.");
        });
        await EndExtortion(scene);
        await Server.WaitPost(() => SEntMan.DeleteEntity(away));
        await RunTicks(5);
    }

    /// <summary>Killing the crew pays the warrant's base and every toll taken, and ends the encounter.</summary>
    [Test]
    public async Task KillingAnExtortersCrewPaysTheBaseAndItsTolls()
    {
        var scene = await StartExtortion("WFTestExtorter", new Vector2(40000, -82000), true);
        var before = 0;
        uint id = 0;
        await Server.WaitAssertion(() =>
        {
            before = ExtortBalance();
            id = SEntMan.GetComponent<WFEncounterBountyComponent>(scene.Encounter).Contract!.Value;
            RaiseHelm(scene.Helm, new HailChooseMessage(WFEncounterSystem.ExtortPay));
            Assert.That(before - ExtortBalance(), Is.EqualTo(ExtortToll));

            // The player's ship lands enough hits with its guns to have a hand in it.
            var rewards = Server.System<WFEncounterRewardSystem>();
            for (var i = 0; i < WFEncounterRewardSystem.MinimumHits; i++)
            {
                rewards.RecordHit(scene.Ship.Grid, scene.PlayerShip);
            }

            var crew = new List<EntityUid>();
            var query = SEntMan.EntityQueryEnumerator<WFCrewComponent>();
            while (query.MoveNext(out var uid, out var member))
            {
                if (member.Group == scene.Ship.Group)
                    crew.Add(uid);
            }

            Assert.That(crew, Has.Count.GreaterThan(1));
            Assert.That(crew.All(uid => SEntMan.HasComponent<WFEncounterBountyTargetComponent>(uid)), Is.True, "Every hand is on the warrant.");
            foreach (var uid in crew)
            {
                Server.System<MobStateSystem>().ChangeMobState(uid, MobState.Dead);
            }
        });
        await RunTicks(100);
        await Server.WaitAssertion(() =>
        {
            Assert.That(ExtortBalance() - before, Is.EqualTo(ExtortBase), "Paid the base and the toll: the toll comes back with the base on top.");
            Assert.That(Server.System<BountyContractSystem>().TryGetContract(id, out _), Is.False, "Paid, the warrant is gone.");
            Assert.That(SEntMan.GetComponent<WFEncounterBountyComponent>(scene.Encounter).State, Is.EqualTo(WFBountyState.Claimed));
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(scene.Encounter).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(Server.System<HailingSystem>().GetState(scene.Helm), Is.Null, "The channel goes with the crew.");
        });
        await EndExtortion(scene);
    }
}
