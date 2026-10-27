#nullable enable
using System.Linq;
using Content.Server._WF.ShipAccess;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>"AI can't open doors on ship it belongs to": a locked ship's doors admit the AI whose core it carries, and no other AI.</summary>
[TestFixture]
[TestOf(typeof(WFShipAiAccessSystem))]
public sealed class ShipAccessAiTest
{
    [Test]
    public async Task ShipAiOpensItsOwnLockedDoorsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var elsewhere = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var readers = entMan.System<AccessReaderSystem>();
            var access = entMan.System<WFShipAccessServerSystem>();
            var hands = entMan.System<SharedHandsSystem>();
            var door = entMan.SpawnEntity("AirlockShuttle", map.GridCoords);
            var (_, deed) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(entMan, deed, map.Grid.Owner);
            var ship = new Entity<WFShipAccessComponent>(map.Grid.Owner, entMan.EnsureComponent<WFShipAccessComponent>(map.Grid.Owner));
            access.SetLocked(ship, true);

            var ownAi = SpawnAi(entMan, map.GridCoords);
            var otherAi = SpawnAi(entMan, elsewhere.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(readers.IsAllowed(ownAi, door), Is.True, "the ship's own AI opens its locked door.");
                Assert.That(readers.IsAllowed(otherAi, door), Is.False, "another ship's AI does not.");
            });
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns an anchored vessel AI core and returns the AI held in it.</summary>
    private static EntityUid SpawnAi(IEntityManager entMan, Robust.Shared.Map.EntityCoordinates coords)
    {
        var core = entMan.SpawnEntity("PlayerStationAiVessel", coords);
        var xform = entMan.GetComponent<TransformComponent>(core);
        if (!xform.Anchored)
            entMan.System<SharedTransformSystem>().AnchorEntity((core, xform));

        Assert.That(xform.Anchored, Is.True, "Precondition: the core is anchored.");
        var held = entMan.System<SharedContainerSystem>()
            .GetContainer(core, StationAiCoreComponent.Container).ContainedEntities.Single();
        Assert.That(entMan.System<SharedStationAiSystem>().TryGetCore(held, out _), Is.True,
            "Precondition: the AI has a working core.");
        return held;
    }
}
