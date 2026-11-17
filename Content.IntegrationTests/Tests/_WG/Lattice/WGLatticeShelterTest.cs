using System.Linq;
using System.Numerics;
using Content.Server._WG.Lattice;
using Content.Server.Power.Components;
using Content.Shared._WG.Lattice;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Lattice;

/// <summary>A powered stabiliser shelters its ship, a worn charm its wearer, and the far bands pay out in ore and exotic matter.</summary>
public sealed class WGLatticeShelterTest
{
    [Test]
    public async Task StabiliserAndCharmShelter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var transform = entities.System<SharedTransformSystem>();
        var inventory = entities.System<InventorySystem>();

        EntityUid stabiliser = default, human = default;
        await server.WaitAssertion(() =>
        {
            stabiliser = entities.SpawnEntity("WGLatticeStabiliser", map.GridCoords);
            if (!entities.GetComponent<TransformComponent>(stabiliser).Anchored)
                transform.AnchorEntity(stabiliser);
            entities.GetComponent<ApcPowerReceiverComponent>(stabiliser).NeedsPower = false;

            human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var charm = entities.SpawnEntity("WGLatticeCharm", map.GridCoords);
            Assert.That(inventory.TryEquip(human, charm, "neck", force: true), Is.True);
            Assert.That(entities.GetComponent<WGLatticeShelterComponent>(human).StopsTaking, Is.False);
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.TryGetComponent<WGLatticeShelterComponent>(map.Grid, out var shelter), Is.True, "the powered stabiliser didn't shelter its grid.");
            Assert.That(shelter!.StopsTaking, Is.True);
            entities.DeleteEntity(stabiliser);
            Assert.That(inventory.TryUnequip(human, "neck", force: true), Is.True);
            Assert.That(entities.HasComponent<WGLatticeShelterComponent>(human), Is.False);
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<WGLatticeShelterComponent>(map.Grid), Is.False, "the shelter outlived its stabiliser.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FarBandsYieldMoreAndDropExoticMatter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WGLatticeSystem>();

        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            var near = new MapCoordinates(new Vector2(5000f, 0f), map.MapId);
            var feedback = new MapCoordinates(new Vector2(58000f, 0f), map.MapId);
            var deep = new MapCoordinates(new Vector2(72000f, 0f), map.MapId);
            Assert.That(lattice.YieldAt(near), Is.EqualTo(1f));
            Assert.That(lattice.YieldAt(deep), Is.GreaterThan(1f));

            int Shards()
            {
                return entities.EntityQuery<StackComponent>().Count(stack => stack.StackTypeId == "WGExoticMatter");
            }

            lattice.SpawnExotic(near, 1000f, 2f);
            Assert.That(Shards(), Is.Zero, "anchored space dropped exotic matter.");
            lattice.SpawnExotic(feedback, 1000f, 2f);
            Assert.That(Shards(), Is.Zero, "exotic matter dropped outside the Coda.");
            lattice.SpawnExotic(deep, 1000f, 2f);
            Assert.That(Shards(), Is.GreaterThan(10));
        });

        await pair.CleanReturnAsync();
    }
}
