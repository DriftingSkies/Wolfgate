using System.Numerics;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._WF.ShipMissiles;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.ShipMissiles;

/// <summary>A ship missile launches, acquires a painted hull and steers at it on its diverters.</summary>
[TestFixture]
public sealed class WFMissileFlightTest
{
    /// <summary>The missiles this module flies.</summary>
    private static readonly string[] Flown =
    {
        "MissileProjectile50mmHE",
        "ShipMissileASM150",
        "ShipMissileASM250",
        "ShipMissileASM250HEAT",
        "ShipMissileASM250HV",
    };

    /// <summary>
    /// The carriers must stay neutral: a carrier that still flies itself would fight the flight system, and one
    /// without a seeker cone would stop flares working.
    /// </summary>
    [Test]
    public async Task CarriersOnlySeek()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var offset = 0f;
            foreach (var id in Flown)
            {
                offset += 200f;
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.MapUid, new Vector2(offset, 2000f)));

                var seeker = em.GetComponent<TargetSeekingComponent>(uid);
                var flight = em.GetComponent<WFMissileFlightComponent>(uid);

                Assert.Multiple(() =>
                {
                    Assert.That(seeker.Acceleration, Is.Zero, $"{id} must not thrust itself.");
                    Assert.That(seeker.LaunchSpeed, Is.Zero, $"{id} must not launch itself.");
                    Assert.That(seeker.TurnRate?.Theta ?? 0f, Is.Zero, $"{id} must not aim itself.");
                    Assert.That(seeker.TrackingAlgorithm, Is.EqualTo(TrackingMethod.Direct),
                        $"{id} must not run an intercept solver that divides by its zero thrust.");
                    Assert.That(seeker.ScanArc, Is.GreaterThan(0f), $"{id} must keep a cone for flare decoys.");
                    Assert.That(seeker.DetectionRange, Is.GreaterThan(0f), $"{id} must keep a ring for flare decoys.");
                    Assert.That(flight.MaxSpeed, Is.EqualTo(seeker.MaxSpeed),
                        $"{id} carrier and flight must agree on the speed cap.");
                });
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Fired down +x with the hull 600 m out and 100 m off the line of fire, the missile must lock, turn onto it and
    /// never exceed its speed cap.
    /// </summary>
    [Test]
    public async Task LaunchesAndLeads()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var xform = em.System<SharedTransformSystem>();

        EntityUid missile = default;

        await pair.Server.WaitAssertion(() =>
        {
            // A hull carrying a seeker target, off the missile's line of fire but inside its cone.
            var painted = em.SpawnEntity(null, map.GridCoords);
            xform.AnchorEntity(painted);
            em.AddComponent<TargetSeekingTargetComponent>(painted);

            missile = em.SpawnEntity("ShipMissileASM150",
                new EntityCoordinates(map.MapUid, new Vector2(-600f, -100f)));
            xform.SetWorldRotation(missile, Angle.FromDegrees(90)); // +x
        });

        await pair.RunTicksSync(20);

        await pair.Server.WaitAssertion(() =>
        {
            var flight = em.GetComponent<WFMissileFlightComponent>(missile);
            var seeker = em.GetComponent<TargetSeekingComponent>(missile);
            var body = em.GetComponent<PhysicsComponent>(missile);

            Assert.Multiple(() =>
            {
                Assert.That(flight.Launched, Is.True, "The launch impulse goes in on the first tick.");
                Assert.That(seeker.CurrentTarget, Is.EqualTo(map.Grid.Owner),
                    "The carrier must have acquired the painted hull.");
                Assert.That(flight.Seeker, Is.EqualTo(WFMissileSeekerState.Tracking), "A parked hull is not a notch.");
                Assert.That(flight.Divert, Is.GreaterThan(0f).And.LessThanOrEqualTo(flight.DivertBudget),
                    "A gentle lead must not drain the diverters.");
                Assert.That(body.LinearVelocity.X, Is.GreaterThan(0f), "The motor must push down the nose.");
                Assert.That(body.LinearVelocity.Y, Is.GreaterThan(0f), "Steering turns onto the off-axis hull.");
                Assert.That(body.LinearVelocity.Length(), Is.LessThanOrEqualTo(flight.MaxSpeed + 0.5f),
                    "The speed cap must hold.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Handed 45 degrees off its nose, the missile still locks during the launch transient and takes the bearing: the
    /// ASM-50's own 7.5 degree scan arc would never see that hull.
    /// </summary>
    [Test]
    public async Task SoftLaunchTakesTheBearing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var xform = em.System<SharedTransformSystem>();

        EntityUid missile = default;

        await pair.Server.WaitAssertion(() =>
        {
            // The painted hull sits on the grid at its origin; the missile is 600 m out, 45 degrees off the bearing.
            var painted = em.SpawnEntity(null, map.GridCoords);
            xform.AnchorEntity(painted);
            em.AddComponent<TargetSeekingTargetComponent>(painted);

            missile = em.SpawnEntity("MissileProjectile50mmHE",
                new EntityCoordinates(map.MapUid, new Vector2(-424f, -424f)));
            xform.SetWorldRotation(missile, Angle.FromDegrees(90)); // +x: a quarter turn in world angles
        });

        await pair.RunTicksSync(12);

        await pair.Server.WaitAssertion(() =>
        {
            var flight = em.GetComponent<WFMissileFlightComponent>(missile);
            var seeker = em.GetComponent<TargetSeekingComponent>(missile);
            var body = em.GetComponent<PhysicsComponent>(missile);

            Assert.Multiple(() =>
            {
                Assert.That(seeker.CurrentTarget, Is.EqualTo(map.Grid.Owner),
                    "The launch cone must lock what the scan arc alone would miss.");
                Assert.That(flight.LaunchTimer, Is.GreaterThan(0f), "The launch transient is still running.");
                Assert.That(flight.Divert, Is.EqualTo(flight.DivertBudget), "Off-rail stabilisation is free.");
                Assert.That(body.LinearVelocity.X, Is.GreaterThan(0f), "The launch impulse runs down the nose.");
                Assert.That(body.LinearVelocity.Y, Is.GreaterThan(0f), "Steering turns onto the off-axis hull.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
