using System.Linq;
using Content.Server._WG.Farming;
using Content.Shared.Interaction;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Farming;

/// <summary>A busy processing machine queues more of the same and hands the queue back when emptied.</summary>
[TestFixture]
public sealed class ProcessorQueueTest
{
    [Test]
    public async Task BusyMachineQueues()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var farmer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var mayo = entMan.SpawnEntity("WGFarmMayoMachine", map.GridCoords);
            for (var i = 0; i < 3; i++)
                entMan.EventBus.RaiseLocalEvent(mayo, new InteractUsingEvent(farmer, entMan.SpawnEntity("FoodEgg", map.GridCoords), mayo, map.GridCoords));

            var comp = entMan.GetComponent<WGProcessorComponent>(mayo);
            Assert.That(comp.Recipe, Is.EqualTo("WGMayo"));
            Assert.That(comp.Queue, Has.Count.EqualTo(2));

            var containers = entMan.System<SharedContainerSystem>();
            int Loose() => entMan.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == "FoodEgg"
                && !entMan.IsQueuedForDeletion(m.Owner) && !containers.IsEntityInContainer(m.Owner));
            var before = Loose();
            entMan.System<WGProcessorSystem>().Unload((mayo, comp), farmer);
            Assert.That(comp.Queue, Is.Empty);
            Assert.That(Loose() - before, Is.EqualTo(2), "The queued eggs weren't handed back.");
        });

        await pair.CleanReturnAsync();
    }
}
