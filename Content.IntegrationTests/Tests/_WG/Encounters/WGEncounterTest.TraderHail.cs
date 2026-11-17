#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.Hailing;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Power.Components;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.Hailing;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

/// <summary>A trader under way answers a hail and, asked to, holds position so the caller can dock.</summary>
public sealed partial class WGEncounterTest
{
    [TestPrototypes]
    private const string TraderHailPrototypes = @"
- type: wgEncounter
  id: WGTestTraderUnderWay
  name: wg-encounter-name-trader
  start: Manual
  ships:
  - key: trader
    vessel: WGDredger
    side: trader
    captain: false
    deckhands: 0
    guards: 1
    roles: [ WGCrewPilot, WGCrewDeckhand ]
    passengers: [ WGTraderWanderer ]
    wander: 3
    wanderRadius: 2000
    boarding: Ignore
    docking: Ignore
";

    [Test]
    public async Task TraderUnderWayHoldsPositionWhenHailedToStop()
    {
        var ui = SEntMan.System<SharedUserInterfaceSystem>();
        var at = new Vector2(21000f, -21000f);
        EntityUid playerShip = default;
        EntityUid helm = default;
        EntityUid encounter = default;
        WGEncounterShipState ship = default!;

        await Server.WaitAssertion(() =>
        {
            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            playerShip = grid.Owner;
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
            transform.SetCoordinates(playerShip, new EntityCoordinates(MapData.MapUid, at));
            helm = SEntMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(playerShip, new Vector2(1.5f, 0.5f)));
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(helm);
            power.NeedsPower = false;
            power.Powered = true;
            transform.SetCoordinates(SPlayer, new EntityCoordinates(playerShip, new Vector2(1.5f, 1.5f)));
        });

        await RunTicks(5);
        await Server.WaitPost(() => ui.OpenUi(helm, ShuttleConsoleUiKey.Key, SPlayer));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var proto = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestTraderUnderWayProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(proto, new MapCoordinates(at + new Vector2(400f, 0f), MapData.MapId),
                out encounter), Is.True);
            ship = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["trader"];
            Assert.That(ship.Passengers, Is.True);
            Assert.That(ship.HasOrders, Is.True, "It is under way on its wander.");
            PowerHelms(ship.Grid);
        });

        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<HailingSystem>().TryHail(helm, SPlayer, ship.Grid), Is.True);
        });

        await WaitUntilServer(() => Server.System<HailingSystem>().GetState(helm) == HailState.Connected, 600);
        await Server.WaitAssertion(() =>
        {
            var hail = SEntMan.GetComponent<HailConsoleComponent>(helm);
            Assert.That(hail.Options.Select(option => option.Id),
                Is.EqualTo(new[] { WGEncounterTraderHailSystem.Stop, WGEncounterTraderHailSystem.Leave }));
            Assert.That(hail.Log.Count(line => !line.Own), Is.EqualTo(1), "The trader greets the caller.");
            Assert.That(Server.System<HailingSystem>().Choose(helm, SPlayer, WGEncounterTraderHailSystem.Stop), Is.True);

            Assert.That(Server.System<WGEncounterTraderHailSystem>().IsHolding(ship), Is.True);
            Assert.That(ship.Serving, Is.True, "Its orders are paused while it holds.");
            Assert.That(Server.System<WGCrewObjectiveSystem>().QueueStatus(ship.Grid, ship.Group), Is.EqualTo("paused"));
        });

        // The channel closes by itself once the answer has been read.
        await WaitUntilServer(() => Server.System<HailingSystem>().GetState(helm) == null, 600);

        // The hold runs out and it flies on.
        await Server.WaitAssertion(() =>
        {
            ship.HeldUntil = TimeSpan.Zero;
        });
        await WaitUntilServer(() => !ship.Serving, 300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCrewObjectiveSystem>().QueueStatus(ship.Grid, ship.Group), Is.Not.EqualTo("paused"));
            Server.System<WGEncounterSystem>().End(encounter);
            SendPlayerHome(playerShip);
        });
        await RunTicks(10);
    }
}
