#nullable enable
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Shared._WF.Encounters;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    /// <summary>The encounter stays on the map when its lead flies over the origin and away, which grid traversal would otherwise take it along.</summary>
    [Test]
    public async Task EncounterRootStaysWhenItsLeadFliesOverIt()
    {
        var origin = new Vector2(1800, 1800);
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(Convoy);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(origin, MapData.MapId), out encounter), Is.True);
        });
        await RunTicks(5);

        EntityUid lead = default;
        await Server.WaitAssertion(() =>
        {
            lead = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["lead"].Grid;
            var centre = SEntMan.GetComponent<MapGridComponent>(lead).LocalAABB.Center;
            Server.System<SharedTransformSystem>().SetWorldPosition(lead, origin - centre);
        });
        await RunTicks(5);

        await Server.WaitPost(() => Server.System<SharedTransformSystem>().SetWorldPosition(lead, origin + new Vector2(500, 0)));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var xform = SEntMan.GetComponent<TransformComponent>(encounter);
            Assert.That(xform.ParentUid, Is.EqualTo(MapData.MapUid),
                $"The encounter was parented to {SEntMan.ToPrettyString(xform.ParentUid)} by its lead.");
            Assert.That(Vector2.Distance(Server.System<SharedTransformSystem>().GetWorldPosition(xform), origin), Is.LessThan(0.01f));
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }
}
