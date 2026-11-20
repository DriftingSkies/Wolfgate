#nullable enable
using System.Linq;
using System.Numerics;
using Content.Shared._WG.Vehicles.Base;
using Content.Shared._WG.Vehicles.Bison;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared.DragDrop;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Vehicles;

/// <summary>The Bison hauls a warhead on its bed, and shares its controls between its van cab and its remote.</summary>
[TestFixture]
public sealed class CMUCargoWarheadTest
{
    private const string CarrierId = "CMUCargoCarrier";
    private const string WarheadId = "WGWarheadTactical";

    private static async Task<(TestMapData Map, EntityUid Carrier)> Build(Pair.TestPair pair)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid carrier = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -10; x < 20; x++)
            for (var y = -10; y < 20; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            carrier = server.EntMan.SpawnEntity(CarrierId, new EntityCoordinates(map.Grid.Owner, 4.5f, 4.5f));
        });
        await server.WaitRunTicks(5);

        return (map, carrier);
    }

    /// <summary>Dropping a loose warhead on the carrier opens the bay and loads it onto the bed, in view; unloading sets it down again.</summary>
    [Test]
    public async Task ADraggedWarheadRidesOnTheBed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (map, carrier) = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        EntityUid user = default, warhead = default;

        await server.WaitPost(() =>
        {
            user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 5.7f, 4.5f));
            warhead = entMan.SpawnEntity(WarheadId, new EntityCoordinates(map.Grid.Owner, 5.7f, 5.7f));
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            server.System<SharedTransformSystem>().Unanchor(warhead);

            var target = new CanDropTargetEvent(user, warhead);
            entMan.EventBus.RaiseLocalEvent(carrier, ref target);
            Assert.That(target.CanDrop, Is.True, "A loose warhead could not be dropped on the carrier.");

            var drop = new DragDropTargetEvent(user, warhead);
            entMan.EventBus.RaiseLocalEvent(carrier, ref drop);
            Assert.That(drop.Handled, Is.True);
            Assert.That(entMan.GetComponent<CMUCargoVehicleComponent>(carrier).BayOpen, Is.True, "Dropping cargo did not open the bay.");
        });
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            var cargo = entMan.GetComponent<CMUCargoVehicleComponent>(carrier);
            var xform = entMan.GetComponent<TransformComponent>(warhead);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(cargo.CargoContainer!.ContainedEntity, Is.EqualTo(warhead), "The warhead was not loaded.");
                Assert.That(cargo.CargoContainer.ShowContents, Is.True, "The bed hides its cargo.");
                Assert.That(xform.ParentUid, Is.EqualTo(carrier));
                Assert.That(xform.LocalPosition, Is.EqualTo(cargo.CargoOffset), "The warhead is not on the bed.");
                Assert.That(entMan.HasComponent<CMUCargoOnBedComponent>(warhead), Is.True);
            }
        });

        await server.WaitAssertion(() =>
        {
            var verbs = server.System<Content.Shared.Verbs.SharedVerbSystem>();
            var unload = verbs.GetLocalVerbs(carrier, user, typeof(Content.Shared.Verbs.AlternativeVerb), force: true)
                .FirstOrDefault(v => v.Text == server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>().GetString("cmu-cargo-vehicle-verb-unload"));
            Assert.That(unload, Is.Not.Null, "No unload verb.");
            verbs.ExecuteVerb(unload!, user, carrier, forced: true);
        });
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<CMUCargoVehicleComponent>(carrier).CargoContainer!.ContainedEntity, Is.Null);
                Assert.That(entMan.GetComponent<TransformComponent>(warhead).ParentUid, Is.EqualTo(map.Grid.Owner));
                Assert.That(entMan.HasComponent<CMUCargoOnBedComponent>(warhead), Is.False);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A cab driver takes the wheel from the carrier, and getting up hands it back to the remote.</summary>
    [Test]
    public async Task TheCabDriverHandsTheWheelBackToTheRemote()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (map, carrier) = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var vehicles = server.System<RMCVehicleSystem>();
            var vehicle = entMan.GetComponent<RMCVehicleComponent>(carrier);

            Assert.That(entMan.HasComponent<VehicleEnterComponent>(carrier), Is.True, "The carrier has no cab.");
            Assert.That(vehicle.Operator, Is.EqualTo(carrier), "The carrier is not its own operator at rest.");

            var driver = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            Assert.That(vehicles.TrySetOperator((carrier, vehicle), driver), Is.True);
            Assert.That(vehicle.Operator, Is.EqualTo(driver));

            Assert.That(vehicles.TryRemoveOperator((carrier, vehicle)), Is.True);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<RMCVehicleComponent>(carrier).Operator, Is.EqualTo(carrier), "The carrier did not take its own wheel back.");
        });

        await pair.CleanReturnAsync();
    }
}
