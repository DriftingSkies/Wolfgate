#nullable enable
using System.Linq;
using Content.Server._WF.MappingMode;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.MappingMode;
using Content.Shared.Atmos;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>A library save gives every grid tile its roundstart air, as <c>fixgridatmos</c> would.</summary>
[TestFixture]
public sealed class WFMappingAtmosFixTest
{
    [Test]
    public async Task SaveFixesGridAtmos()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var atmos = entMan.System<AtmosphereSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        Entity<MapGridComponent> grid = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Atmos Fix Test", out var session), Is.True);
            grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session!.RootMap).MapId);
            entMan.EnsureComponent<GridAtmosphereComponent>(grid);
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp,
                Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3).Select(y => (new Vector2i(x, y), plating)))
                    .ToList());
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            atmos.GetTileMixture(grid.Owner, null, new Vector2i(1, 1), true)?.Clear();
            RaiseAs(entMan, a, new WFMappingSaveRequest(string.Empty));

            var air = atmos.GetTileMixture(grid.Owner, null, new Vector2i(1, 1));
            Assert.That(air, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(air!.GetMoles(Gas.Oxygen), Is.EqualTo(Atmospherics.OxygenMolesStandard).Within(0.01f));
                Assert.That(air.GetMoles(Gas.Nitrogen), Is.EqualTo(Atmospherics.NitrogenMolesStandard).Within(0.01f));
            });
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
