#nullable enable
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._WG.ShipCombat;
using Content.Server.Power.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.ShipCombat;

/// <summary>
/// A tiered ship gun turns at its turn rate and only fires once laid, and stops at the edge of its arc when following a
/// point it can't fire at. A fixed gun still fires at once.
/// </summary>
[TestFixture]
public sealed class WGTurretTraverseTest
{
    private const string Gun = "WeaponTurretL85Autocannon";

    [Test]
    public async Task GunHoldsFireUntilLaid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var xform = em.System<SharedTransformSystem>();
        var fireControl = em.System<FireControlSystem>();

        EntityUid gun = default;
        EntityUid fixedGun = default;
        EntityCoordinates target = default;
        Angle goal = default;

        await server.WaitAssertion(() =>
        {
            gun = Spawn(em, map.GridCoords);
            fixedGun = Spawn(em, map.GridCoords);
            em.RemoveComponent<FireControlRotateComponent>(fixedGun);

            Assert.That(em.GetComponent<WGTurretTraverseComponent>(gun).TurnRate, Is.EqualTo(120f),
                "The T1 parent must give the gun its turn rate.");

            var (position, rotation) = xform.GetWorldPositionRotation(gun);
            goal = rotation + Angle.FromDegrees(90);
            target = new EntityCoordinates(map.MapUid, position + goal.ToWorldVec() * 10f);

            Assert.That(fireControl.AttemptFire(gun, gun, target, noServer: true), Is.False,
                "A gun 90 degrees off its target must hold fire.");
            Assert.That(fireControl.AttemptFire(fixedGun, fixedGun, target, noServer: true), Is.True,
                "A gun that does not rotate must fire at once.");
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var offset = Angle.ShortestDistance(xform.GetWorldRotation(gun), goal);
            Assert.That(Math.Abs(offset.Degrees), Is.LessThan(3), "The gun must have turned onto its target.");
            Assert.That(fireControl.AttemptFire(gun, gun, target, noServer: true), Is.True,
                "A laid gun must fire.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GunStopsAtTheEdgeOfItsFiringArc()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var xform = em.System<SharedTransformSystem>();
        var traverse = em.System<WGTurretTraverseSystem>();

        EntityUid gun = default;
        Vector2 position = default;
        var east = Angle.FromWorldVec(new Vector2(1, 0));
        var clear = east - Angle.FromDegrees(60);

        await server.WaitAssertion(() =>
        {
            // A wall one tile east of the gun blocks bearings within 45 degrees of east.
            em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            gun = Spawn(em, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            em.GetComponent<FireControllableComponent>(gun).IgnoreLos = false;
            position = xform.GetWorldPosition(gun);

            xform.SetWorldRotation(gun, east + Angle.FromDegrees(90));
            var blocked = new EntityCoordinates(map.MapUid, position + east.ToWorldVec() * 10f);
            traverse.TryLay(gun, blocked, east.ToWorldVec());
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var offset = Math.Abs(Angle.ShortestDistance(xform.GetWorldRotation(gun), east).Degrees);
            Assert.That(offset, Is.InRange(40, 60), "A gun following a blocked point must stop at the edge of its arc.");

            xform.SetWorldRotation(gun, east + Angle.FromDegrees(60));
            var target = new EntityCoordinates(map.MapUid, position + clear.ToWorldVec() * 10f);
            traverse.TryLay(gun, target, clear.ToWorldVec());
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var offset = Math.Abs(Angle.ShortestDistance(xform.GetWorldRotation(gun), clear).Degrees);
            Assert.That(offset, Is.LessThan(3), "A gun turning onto a clear target may cross its blocked arc.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns the gun without a power receiver, so it counts as powered on the bare test grid.</summary>
    private static EntityUid Spawn(IEntityManager em, EntityCoordinates coords)
    {
        var uid = em.SpawnEntity(Gun, coords);
        em.RemoveComponent<ApcPowerReceiverComponent>(uid);
        em.GetComponent<FireControllableComponent>(uid).IgnoreLos = true;
        return uid;
    }
}
