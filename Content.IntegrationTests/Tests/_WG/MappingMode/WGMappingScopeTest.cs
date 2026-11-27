using System.Numerics;
using Content.Server._WG.MappingMode;
using Content.Server._WG.NetTraffic;
using Content.Shared._WG.NetTraffic;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.MappingMode;

/// <summary>
/// Grids on mapping maps are never scoped: a mapper who looks away (a live test, another level) keeps the same grid
/// on their client instead of having it deleted and sent again with its walls detached.
/// </summary>
[TestFixture]
[TestOf(typeof(WGMapScopedPvsSystem))]
public sealed class WGMappingScopeTest
{
    [Test]
    public async Task MappingGridsStayOnTheClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var cEm = pair.Client.EntMan;

        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        await pair.RunTicksSync(5);
        var mapping = await pair.CreateTestMap();
        var other = await pair.CreateTestMap();

        NetEntity mappingGrid = default, otherGrid = default;
        await server.WaitPost(() =>
        {
            server.System<WGMappingModeSystem>().RegisterTestMap(mapping.MapUid, 1);
            mappingGrid = em.GetNetEntity(mapping.Grid.Owner);
            otherGrid = em.GetNetEntity(other.Grid.Owner);
            server.CfgMan.SetCVar(NetTrafficCVars.MapScopedGrids, true);
            server.PlayerMan.SetAttachedEntity(pair.Player!,
                em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, mapping.MapId)));
        });
        await pair.RunTicksSync(30);

        try
        {
            await server.WaitAssertion(() =>
            {
                var scoped = server.System<WGMapScopedPvsSystem>();
                Assert.Multiple(() =>
                {
                    Assert.That(scoped.IsScoped(mapping.Grid.Owner), Is.False, "a mapping map's grid is not scoped");
                    Assert.That(scoped.IsScoped(other.Grid.Owner), "another map's grid is scoped");
                });
            });
            Assert.That(cEm.TryGetEntity(mappingGrid, out var before), "the mapping grid is on the client");

            // Looking at another map, as a tester does, leaves the mapping grid where it was on the client.
            await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!,
                em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, other.MapId))));
            await pair.RunTicksSync(30);
            Assert.Multiple(() =>
            {
                Assert.That(cEm.TryGetEntity(otherGrid, out _), "the viewed map's grid arrives");
                Assert.That(cEm.TryGetEntity(mappingGrid, out var away) && away == before,
                    "the mapping grid stays on the client while the mapper is away");
            });

            await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!,
                em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, mapping.MapId))));
            await pair.RunTicksSync(30);
            Assert.That(cEm.TryGetEntity(mappingGrid, out var after) && after == before,
                "coming back finds the same mapping grid, not a new copy");
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(NetTrafficCVars.MapScopedGrids, false);
                server.CfgMan.SetCVar(CVars.NetPVS, false);
            });
            await pair.RunTicksSync(10);
        }

        await pair.CleanReturnAsync();
    }
}
