#nullable enable
using System.Numerics;
using Content.Server._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>The orbit layer's centre marker stays on the orbit map when a hull flies over it.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetNetworkSystem))]
public sealed class OrbitBeaconPinTest
{
    private const string BeaconProto = "WFOrbitBeacon";

    /// <summary>A grid moving over a map entity reparents it since engine v283, unless it opts out of traversal.</summary>
    [Test]
    public async Task HullMovingOverTheBeaconLeavesItBehind()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var beacon = EntityUid.Invalid;
        var orbitId = MapId.Nullspace;

        await server.WaitPost(() =>
        {
            orbitId = entMan.GetComponent<MapComponent>(orbit).MapId;
            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var meta, out var xform))
            {
                if (meta.EntityPrototype?.ID == BeaconProto && xform.MapUid == orbit)
                    beacon = uid;
            }
        });

        Assert.That(beacon, Is.Not.EqualTo(EntityUid.Invalid), "The orbit layer has no centre marker.");

        var hull = await BuildHull(pair, orbitId, new Vector2(-100f, 0f));
        var position = Vector2.Zero;

        await server.WaitPost(() =>
        {
            position = transform.GetWorldPosition(beacon);
            var centre = entMan.GetComponent<MapGridComponent>(hull).LocalAABB.Center;
            transform.SetWorldPosition(hull, position - centre);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        var parent = EntityUid.Invalid;
        var moved = Vector2.Zero;

        await server.WaitPost(() =>
        {
            parent = entMan.GetComponent<TransformComponent>(beacon).ParentUid;
            moved = transform.GetWorldPosition(beacon) - position;
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parent, Is.EqualTo(orbit),
                $"{entMan.ToPrettyString(beacon)} was parented to {entMan.ToPrettyString(parent)} by a passing hull.");
            Assert.That(moved.Length(), Is.LessThan(0.01f), $"{entMan.ToPrettyString(beacon)} moved {moved}.");
        }

        await server.WaitPost(() => entMan.DeleteEntity(hull));
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
