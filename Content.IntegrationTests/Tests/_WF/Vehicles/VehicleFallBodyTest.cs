#nullable enable
using Content.IntegrationTests.Pair;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Damage;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// Vehicles with no frame integrity falling through z-levels: the landing hurts their Damageable by the weight class's
/// share of their body health, and whatever that much damage does to them happens through their normal destruction path.
/// </summary>
[TestFixture]
public sealed class VehicleFallBodyTest
{
    private const string Body = "WFFallBodyTestVehicle";
    private const string Brittle = "WFFallBodyTestVehicleBrittle";

    /// <summary>What the vehicle's body health is, in the prototypes below.</summary>
    private const float BodyHealth = 200f;

    [TestPrototypes]
    private const string Prototypes = @"
# A drivable box with a Damageable and nothing else to hurt: no frame, no wheels.
- type: entity
  parent: WFVehicleTestChassis
  id: WFFallBodyTestVehicle
  components:
  - type: VehicleFall
    bodyHealth: 200
  - type: Damageable
    damageContainer: StructuralInorganic

# The same box, wrecked by 40 damage: a heavy landing (50) breaks it and a light one (30) does not.
- type: entity
  parent: WFFallBodyTestVehicle
  id: WFFallBodyTestVehicleBrittle
  components:
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 40
      behaviors:
      - !type:DoActsBehavior
        acts: [ ""Destruction"" ]
";

    private static float Damage(FallLevels levels)
    {
        return (float) levels.Pair.Server.EntMan.GetComponent<DamageableComponent>(levels.Vehicle).TotalDamage;
    }

    /// <summary>
    /// A vehicle with no frame integrity that drives off an edge with a level below lands there, keeps its driver and is
    /// hurt by the share of its body health that its weight class names (falling.md).
    /// </summary>
    [TestCase(VehicleWeightClass.Weak, 20f)]
    [TestCase(VehicleWeightClass.Light, 30f)]
    [TestCase(VehicleWeightClass.Medium, 40f)]
    [TestCase(VehicleWeightClass.Heavy, 50f)]
    public async Task AVehicleWithNoFrameLandsAndItsBodyTakesTheLandingDamage(VehicleWeightClass weight, float expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var levels = await FallLevelRigs.Build(pair, Body);
        var server = pair.Server;
        var entMan = server.EntMan;

        try
        {
            await server.WaitPost(() => VehicleRigs.Mover(levels.Rig).WeightClass = weight);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<HardpointIntegrityComponent>(levels.Vehicle), Is.False, "Test setup: the vehicle should have no frame integrity.");
                Assert.That(Damage(levels), Is.Zero);
            });

            await VehicleRigs.Seat(levels.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(levels.Rig, MoveButtons.Right));

            Assert.That(await FallLevelRigs.RunUntilLanded(levels, 500), Is.True, "The vehicle never dropped to the lower map.");

            await server.WaitAssertion(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(levels.Vehicle);
                var mover = VehicleRigs.Mover(levels.Rig);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(xform.GridUid, Is.EqualTo(levels.LowerGrid), "The vehicle did not land on the lower grid.");
                    Assert.That(Damage(levels), Is.EqualTo(expected).Within(0.5f), "The body took the wrong landing damage.");
                    Assert.That(entMan.GetComponent<RMCVehicleComponent>(levels.Vehicle).Operator, Is.EqualTo(levels.Rig.Driver), "The driver lost the wheel.");
                    Assert.That(mover.CurrentSpeed, Is.Zero, "A landing should end the vehicle's speed.");
                }
            });

            // A second fall is not owed: the landing is paid once, and the vehicle drives on from it.
            await server.WaitRunTicks(60);
            await server.WaitAssertion(() =>
            {
                Assert.That(Damage(levels), Is.EqualTo(expected).Within(0.5f), "The vehicle was hurt again after landing.");
                Assert.That(VehicleRigs.WorldPosition(levels.Rig).X, Is.GreaterThan(FallLevelRigs.EdgeX - 1.5f), "The vehicle could not be driven after landing.");
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(levels.Rig, MoveButtons.None));
            await FallLevelRigs.Teardown(levels);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A landing that does as much damage as the body can take goes through the thresholds: the vehicle is destroyed once,
    /// as any other damage would destroy it, and the driver is left unharmed on the level it fell to.
    /// </summary>
    [TestCase(VehicleWeightClass.Heavy, true)]
    [TestCase(VehicleWeightClass.Light, false)]
    public async Task ALandingThatBreaksTheBodyDestroysItThroughItsThresholds(VehicleWeightClass weight, bool destroyed)
    {
        await using var pair = await PoolManager.GetServerClient();
        var levels = await FallLevelRigs.Build(pair, Brittle);
        var server = pair.Server;
        var entMan = server.EntMan;
        var probe = server.System<VehicleEventProbeSystem>();

        try
        {
            await server.WaitPost(() => VehicleRigs.Mover(levels.Rig).WeightClass = weight);
            await VehicleRigs.Seat(levels.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(levels.Rig, MoveButtons.Right));

            Assert.That(await FallLevelRigs.RunUntilLanded(levels, 500), Is.True, "The vehicle never dropped to the lower map.");
            await server.WaitRunTicks(10);

            if (!destroyed)
            {
                await server.WaitAssertion(() =>
                {
                    Assert.That(FallLevelRigs.MapOf(levels, levels.Vehicle), Is.EqualTo(levels.LowerMap), "The vehicle that survived did not land on the lower map.");
                });
            }

            await server.WaitAssertion(() =>
            {
                var destructions = probe.Destructions.TryGetValue(levels.Vehicle, out var count) ? count : 0;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(destructions, Is.EqualTo(destroyed ? 1 : 0), "The landing did not take the destruction path as damage does.");
                    Assert.That(!entMan.EntityExists(levels.Vehicle) || entMan.IsQueuedForDeletion(levels.Vehicle), Is.EqualTo(destroyed));
                    Assert.That(entMan.Deleted(levels.Rig.Driver), Is.False, "The driver was lost with the vehicle.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (entMan.EntityExists(levels.Vehicle))
                    VehicleRigs.Hold(levels.Rig, MoveButtons.None);
            });
            await FallLevelRigs.Teardown(levels);
        }

        await pair.CleanReturnAsync();
    }
}
