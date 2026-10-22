#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._FarHorizons.StarSystem;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Sector stars and planets stay on the sector map when a hull arrives on top of them.</summary>
[TestFixture]
[TestOf(typeof(StarSystemMapSystem))]
public sealed class SectorBodyPinTest
{
    private const string SystemProto = "SystemKyphrus";

    /// <summary>Seconds to wait for a jump before giving up.</summary>
    private const float HopTimeout = 20f;

    /// <summary>A hull jumping onto a body neither carries it off nor takes it along when deleted.</summary>
    [Test]
    public async Task HullArrivingOnABodyLeavesItBehind()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var shuttles = server.System<ShuttleSystem>();

        var departure = await pair.CreateTestMap();
        var sector = EntityUid.Invalid;
        var bodies = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            sector = server.System<SharedMapSystem>().CreateMap(out _);
            var starMap = entMan.AddComponent<StarSystemMapComponent>(sector);
            server.System<StarSystemMapSystem>().SetSystem((sector, starMap), SystemProto);

            var query = entMan.AllEntityQueryEnumerator<FTLBeaconComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == sector)
                    bodies.Add(uid);
            }
        });

        Assert.That(bodies, Is.Not.Empty, "The star system spawned no bodies.");

        foreach (var body in bodies)
        {
            var hull = await BuildHull(pair, departure.MapId, new Vector2(-100f, 0f));
            var position = Vector2.Zero;

            await server.WaitPost(() =>
            {
                position = transform.GetWorldPosition(body);
                var centre = entMan.GetComponent<MapGridComponent>(hull).LocalAABB.Center;

                // Land the hull's centre on the body.
                shuttles.FTLToCoordinates(hull, entMan.GetComponent<ShuttleComponent>(hull),
                    new EntityCoordinates(sector, position - centre), Angle.Zero, 0.5f, 0.5f);
            });

            for (var waited = 0f; waited < HopTimeout; waited += 0.5f)
            {
                await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
                if (entMan.GetComponent<TransformComponent>(hull).MapUid == sector && !entMan.HasComponent<FTLComponent>(hull))
                    break;
            }

            // Let the hull settle where it landed.
            await server.WaitRunTicks(pair.SecondsToTicks(1f));

            var parent = EntityUid.Invalid;
            var hullMap = EntityUid.Invalid;
            var moved = Vector2.Zero;

            await server.WaitPost(() =>
            {
                hullMap = entMan.GetComponent<TransformComponent>(hull).MapUid ?? EntityUid.Invalid;
                parent = entMan.GetComponent<TransformComponent>(body).ParentUid;
                moved = transform.GetWorldPosition(body) - position;
                entMan.DeleteEntity(hull);
            });

            await server.WaitRunTicks(1);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hullMap, Is.EqualTo(sector), "Precondition: the hull never reached the sector map.");
                Assert.That(parent, Is.EqualTo(sector),
                    $"{entMan.ToPrettyString(body)} was parented to {entMan.ToPrettyString(parent)} by an arriving hull.");
                Assert.That(moved.Length(), Is.LessThan(0.01f), $"{entMan.ToPrettyString(body)} moved {moved}.");
                Assert.That(entMan.EntityExists(body), Is.True,
                    $"Deleting the hull deleted {entMan.ToPrettyString(body)}.");
            }
        }

        await server.WaitPost(() => entMan.DeleteEntity(sector));
        await pair.CleanReturnAsync();
    }
}
