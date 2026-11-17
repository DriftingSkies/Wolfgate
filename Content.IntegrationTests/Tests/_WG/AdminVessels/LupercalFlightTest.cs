#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Movement.Systems;
using Content.Shared.Players;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.AdminVessels;

/// <summary>
/// A person flies the Lupercal from its helm: the ship's own pilot gives way, it picks up speed and turns, the crewman
/// takes up station where they leave it, and it jumps to another map.
/// </summary>
[TestFixture]
public sealed class LupercalFlightTest
{
    [Test]
    public async Task APersonFliesItAndItJumps()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var ticks = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var xforms = server.System<SharedTransformSystem>();
        var consoles = server.System<ShuttleConsoleSystem>();
        var map = await pair.CreateTestMap();
        var elsewhere = await pair.CreateTestMap();
        EntityUid grid = default, pilot = default, crewPilot = default;

        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WGLupercal");
            Assert.That(server.System<AdminVesselSpawnSystem>().TrySpawnVessel(vessel, map.MapId, new Vector2(300, 0), null, out var spawned, createStation: true), Is.True);
            grid = spawned!.Value;

            var helm = OfProto(em, grid, "ComputerShuttle").First();
            var maps = server.System<SharedMapSystem>();
            var gridComp = em.GetComponent<MapGridComponent>(grid);
            var here = maps.TileIndicesFor(grid, gridComp, em.GetComponent<TransformComponent>(helm).Coordinates);
            var floor = new[] { new Vector2i(0, -1), new Vector2i(0, 1), new Vector2i(1, 0), new Vector2i(-1, 0) }
                .Select(offset => here + offset)
                .First(tile => maps.GetAnchoredEntities(grid, gridComp, tile)
                    .All(ent => !em.TryGetComponent<PhysicsComponent>(ent, out var solid) || !solid.CanCollide || !solid.Hard));

            // A real player's body at the helm, the way a person takes it.
            var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
            var minds = server.System<SharedMindSystem>();
            minds.WipeMind(session.ContentData()?.Mind);
            pilot = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, floor + new Vector2(0.5f, 0.5f)));
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, pilot);
            em.EnsureComponent<PilotComponent>(pilot);
            consoles.AddPilot(helm, pilot, em.GetComponent<ShuttleConsoleComponent>(helm));
        });

        // The ship's own pilot walks to the helm once the power is up.
        for (var i = 0; i < 60 && crewPilot == default; i++)
        {
            await pair.RunTicksSync(ticks / 2);
            await server.WaitPost(() =>
            {
                var duties = em.EntityQueryEnumerator<WGPilotDutyComponent>();
                while (duties.MoveNext(out var uid, out var duty))
                {
                    if (duty.AtHelm && em.GetComponent<TransformComponent>(uid).GridUid == grid)
                        crewPilot = uid;
                }
            });
        }

        Assert.That(crewPilot, Is.Not.EqualTo(default(EntityUid)), "The ship's own pilot reaches a helm.");

        var start = Vector2.Zero;
        await server.WaitPost(() =>
        {
            start = xforms.GetWorldPosition(grid);
            em.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.StrafeUp;
        });
        await pair.RunTicksSync(ticks * 5);

        var report = string.Empty;
        await server.WaitAssertion(() =>
        {
            var body = em.GetComponent<PhysicsComponent>(grid);
            var shuttle = em.GetComponent<ShuttleComponent>(grid);
            var speed = body.LinearVelocity.Length();
            var moved = (xforms.GetWorldPosition(grid) - start).Length();
            report = $"mass {body.Mass:F0}, thrust {shuttle.LinearThrust[0]:F0}, after 5 s {speed:F1} m/s and {moved:F1} m";
            Assert.That(speed, Is.GreaterThan(5f), $"Full thrust gets the ship moving, with its pilot standing by: {report}");
            Assert.That(moved, Is.GreaterThan(10f), $"And it covers ground: {report}");
            em.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.RotateLeft;
        });
        await pair.RunTicksSync(ticks * 3);

        await server.WaitAssertion(() =>
        {
            var spin = em.GetComponent<PhysicsComponent>(grid).AngularVelocity;
            report += $"; after 3 s turning {spin:F2} rad/s";
            Assert.That(MathF.Abs(spin), Is.GreaterThan(0.1f), $"The gyroscopes turn it: {report}");
            em.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.None;
            consoles.RemovePilot(pilot);
        });

        // The crewman waits a few seconds after the controls go quiet.
        await pair.RunTicksSync(ticks * 5);

        await server.WaitAssertion(() =>
        {
            var duty = em.GetComponent<WGPilotDutyComponent>(crewPilot);
            var anchor = duty.HoldPosition;
            Assert.That(anchor, Is.Not.Null, $"The crewman takes up station once the person leaves: orders {duty.Orders}, at helm {duty.AtHelm}, yielded until {duty.YieldedUntil}.");
            Assert.That((anchor!.Value.Position - start).Length(), Is.GreaterThan(5f), "Where the ship is now, not where it started.");

            var shuttles = server.System<ShuttleSystem>();
            Assert.That(shuttles.CanFTL(grid, out var reason), Is.True, $"It can jump: {reason}");
            shuttles.FTLToCoordinates(grid, em.GetComponent<ShuttleComponent>(grid), new EntityCoordinates(elsewhere.MapUid, Vector2.Zero), Angle.Zero);
            Assert.That(em.HasComponent<FTLComponent>(grid), Is.True, "The jump starts.");
        });

        // Spool, transit and arrival at the default timings.
        var arrived = false;
        for (var i = 0; i < 120 && !arrived; i++)
        {
            await pair.RunTicksSync(ticks);
            await server.WaitPost(() => arrived = em.GetComponent<TransformComponent>(grid).MapID == elsewhere.MapId && !em.HasComponent<FTLComponent>(grid));
        }

        Assert.That(arrived, Is.True, "It arrives on the other map.");
        TestContext.Out.WriteLine($"Lupercal flight: {report}");
        await pair.CleanReturnAsync();
    }

    private static List<EntityUid> OfProto(IEntityManager em, EntityUid grid, string proto)
    {
        var found = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.GridUid == grid && meta.EntityPrototype?.ID == proto)
                found.Add(uid);
        }

        return found;
    }
}
