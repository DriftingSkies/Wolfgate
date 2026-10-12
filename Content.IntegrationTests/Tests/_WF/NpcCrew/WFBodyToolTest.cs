using Content.Shared._WF.NpcCrew;
using Content.Shared.Tools.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

/// <summary>
/// A crewman can pry, but its body is no crowbar for whoever drags it: put into a biomass reclaimer, it does not
/// take the reclaimer apart.
/// </summary>
[TestFixture]
[TestOf(typeof(WFBodyToolSystem))]
public sealed class WFBodyToolTest
{
    [Test]
    public async Task DraggedBodyIsNotAToolTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var tools = entMan.System<SharedToolSystem>();
            var reclaimer = entMan.SpawnEntity("BiomassReclaimer", map.GridCoords);
            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = entMan.SpawnEntity("WFMobCrewBodyHuman", map.GridCoords);

            Assert.That(tools.HasQuality(body, "Prying"), Is.True, "the crewman can no longer pry at all");
            Assert.Multiple(() =>
            {
                Assert.That(tools.UseTool(body, user, reclaimer, 0f, "Prying", new CableCuttingFinishedEvent()), Is.False,
                    "a dragged body pried the reclaimer");
                Assert.That(tools.UseTool(body, body, reclaimer, 0f, "Prying", new CableCuttingFinishedEvent()), Is.True,
                    "a crewman cannot pry for itself");
            });
        });

        await pair.CleanReturnAsync();
    }
}
