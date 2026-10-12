#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WF.Encounters;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string StrandedPrototypes = @"
- type: wfEncounter
  id: WFTestStoryStrandedFrigate
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: frigate
    vessel: Fujian
    company: TSF
    deckhands: 1
    stranded: Thrusters
";

    /// <summary>
    /// A frigate stranded for thrusters loses every linear thruster outright, small and large, and its repair snapshot
    /// knows them all as gone, so the tool can rebuild each one. It is only rescued once it can drive ahead: a side
    /// thruster alone is not thrust worth thanking anyone for.
    /// </summary>
    [Test]
    public async Task StrandedFrigateLosesAllItsThrustersAndIsRescuedByForwardThrust()
    {
        EntityUid encounter = default, ship = default;
        // Where the frigate's own thrusters stood, by the way they faced: a side one, and one that drove it ahead.
        (Vector2 Position, Angle Rotation)? sideSpot = null, mainSpot = null;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestStoryStrandedFrigate");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-45000, -45000), MapData.MapId), out encounter), Is.True);
            ship = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["frigate"].Grid;
        });
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            var linear = 0;
            var thrusters = SEntMan.EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
            while (thrusters.MoveNext(out _, out var thruster, out var xform))
            {
                if (xform.GridUid == ship && thruster.Type == ThrusterType.Linear)
                    linear++;
            }
            Assert.That(linear, Is.EqualTo(0), "Every linear thruster is wrecked.");

            // The snapshot was taken as the crew came aboard, before the wreckage: every thruster it knows must read as gone.
            var data = SEntMan.GetComponent<ShipRepairDataComponent>(ship);
            var recorded = 0;
            var standing = new List<string>();
            foreach (var chunk in data.Chunks.Values)
            {
                foreach (var spec in chunk.Entities.Values)
                {
                    if (!data.EntityPalette[spec.ProtoIndex].Id.Contains("Thruster"))
                        continue;
                    recorded++;
                    var facing = (int) spec.Rotation.GetCardinalDir() / 2;
                    if (facing == 2)
                        mainSpot ??= (spec.LocalPosition, spec.Rotation);
                    else if (facing is 1 or 3)
                        sideSpot ??= (spec.LocalPosition, spec.Rotation);
                    if (spec.OriginalEntity is not { } net)
                        continue;
                    var original = SEntMan.GetEntity(net);
                    if (!SEntMan.EntityExists(original) || SEntMan.Deleted(original))
                        continue;
                    var on = SEntMan.GetComponent<TransformComponent>(original).GridUid;
                    standing.Add($"{data.EntityPalette[spec.ProtoIndex].Id} at {spec.LocalPosition} still exists on {(on == ship ? "the hull" : on?.ToString() ?? "no grid")}");
                }
            }
            Assert.That(recorded, Is.GreaterThan(0), "The snapshot recorded the thrusters.");
            Assert.That(standing, Is.Empty, "No wrecked thruster's original survives anywhere.");

            Assert.That(sideSpot, Is.Not.Null, "The frigate had a side thruster.");
            Assert.That(mainSpot, Is.Not.Null, "The frigate had a main thruster.");

            // A side thruster back on, where one stood: the ship can nudge, not drive.
            var side = SEntMan.SpawnEntity("Thruster", new EntityCoordinates(ship, sideSpot!.Value.Position));
            Server.System<SharedTransformSystem>().SetLocalRotation(side, sideSpot.Value.Rotation);
            // The wreck's power is its own problem; the thrusters here run regardless, so only the thrust is tested.
            SEntMan.GetComponent<ApcPowerReceiverComponent>(side).NeedsPower = false;
            Assert.That(SEntMan.GetComponent<TransformComponent>(side).Anchored, Is.True);
        });
        // Past the rescue delay, by a margin.
        await RunTicks(900);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["frigate"];
            var shuttle = SEntMan.GetComponent<ShuttleComponent>(ship);
            Assert.That(shuttle.LinearThrust[1] + shuttle.LinearThrust[3], Is.GreaterThan(0f), "Precondition: the side thruster gives lateral thrust.");
            Assert.That(shuttle.LinearThrust[2], Is.EqualTo(0f), "Precondition: nothing drives it forward.");
            Assert.That(state.Rescued, Is.False, "A side thruster alone does not count as a rescue.");

            var main = SEntMan.SpawnEntity("Thruster", new EntityCoordinates(ship, mainSpot!.Value.Position));
            Server.System<SharedTransformSystem>().SetLocalRotation(main, mainSpot.Value.Rotation);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(main).NeedsPower = false;
        });
        await RunTicks(900);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["frigate"];
            Assert.That(SEntMan.GetComponent<ShuttleComponent>(ship).LinearThrust[2], Is.GreaterThan(0f), "Precondition: it can drive ahead now.");
            Assert.That(state.Rescued, Is.True, "Forward thrust is the rescue.");
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }
}
