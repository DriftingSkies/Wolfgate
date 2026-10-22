#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.MappingMode;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// A reference file loads aside, then lands on the session map at the chosen offset and turn with its tiles and its
/// grids' layout; one over a cap or too big is refused and leaves no scratch map behind.
/// </summary>
[TestFixture]
public sealed class WFMappingReferenceTest
{
    [Test]
    public async Task LoadPlaceAndRefuse()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var mapSys = entMan.System<MapSystem>();
        var xformSys = entMan.System<TransformSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var res = server.ResolveDependency<IResourceManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var clientReference = client.EntMan.System<WFMappingReferenceSystem>();
        await Enable(pair);
        var player = pair.Player!;
        var dummies = await AddMappers(pair, 3);

        WFMappingSession session = default!;
        string shipPath = default!;
        string twoGridsPath = default!;
        var plating = new Tile(tileDefs["Plating"].TileId);

        int TileCount(EntityUid grid)
        {
            var count = 0;
            var tiles = mapSys.GetAllTiles(grid, entMan.GetComponent<MapGridComponent>(grid));
            while (tiles.MoveNext(out _))
            {
                count++;
            }

            return count;
        }

        List<EntityUid> Grids()
        {
            var grids = new List<EntityUid>();
            var children = entMan.GetComponent<TransformComponent>(session.RootMap).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (entMan.HasComponent<MapGridComponent>(child))
                    grids.Add(child);
            }

            return grids;
        }

        bool AnyScratch() => entMan.EntityQuery<WFMappingSessionMapComponent>(true).Any(m => m.Kind == WFMappingMapKind.Scratch);
        int Maps() => entMan.EntityQuery<MapComponent>(true).Count();
        static int Cells(WFMappingFootprintGrid grid) => grid.Rects.Sum(r => r.Width * r.Height);

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(player, "Reference Test", out var created), Is.True);
            session = created!;
            foreach (var dummy in dummies)
            {
                Assert.That(mapping.TryJoinSession(dummy, session), Is.True);
            }

            // The smallest game ship, to keep the load quick.
            shipPath = mapping.ReferenceFiles(player)
                .Where(f => f.Source == MappingMapSource.Ships)
                .Select(f => f.Path)
                .Take(40)
                .OrderBy(p =>
                {
                    using var stream = res.ContentFileRead(new ResPath(p));
                    return stream.Length;
                })
                .First();

            // A map of two grids, one turned, saved where the player's own library files are offered.
            var temp = mapSys.CreateMap(out var tempId, runMapInit: false);
            var first = mapSys.CreateGridEntity(tempId);
            mapSys.SetTiles(first, first.Comp, Enumerable.Range(0, 3).Select(x => (new Vector2i(x, 0), plating)).ToList());
            var second = mapSys.CreateGridEntity(tempId);
            xformSys.SetWorldPositionRotation(second.Owner, new Vector2(10, 4), Angle.FromDegrees(90));
            mapSys.SetTiles(second, second.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(0, 0), plating), (new Vector2i(1, 0), plating),
                (new Vector2i(0, 1), plating), (new Vector2i(1, 1), plating),
            });

            var version = new WFMappingVersionName(1, new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
                WFMappingVersionKind.Manual).Folder;
            var path = WFMappingLibraryNames.LevelPath(player.UserId, "Two Grids", version, 0);
            res.UserData.CreateDir(path.Directory);
            Assert.That(loader.TrySaveMap(temp, path), Is.True);
            entMan.DeleteEntity(temp);
            twoGridsPath = path.ToString();
            Assert.That(mapping.ReferenceFiles(player).Select(f => f.Path), Does.Contain(twoGridsPath));
        });

        // A ship grid: picked, then placed at an offset and a quarter turn.
        await server.WaitPost(() => RaiseAs(entMan, player, new WFMappingReferencePickRequest(shipPath)));
        await pair.RunTicksSync(10);

        WFMappingReferenceFootprintEvent footprint = default!;
        await client.WaitAssertion(() =>
        {
            Assert.That(clientReference.Footprint, Is.Not.Null, "The client should get the ship's footprint.");
            footprint = clientReference.Footprint!;
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(AnyScratch(), Is.True, "The ship waits on a scratch map.");
            Assert.That(Grids(), Is.Empty);

            var offset = new Vector2(10, 5);
            RaiseAs(entMan, player, new WFMappingReferenceConfirmRequest(footprint.Token, offset, 1));
            var grids = Grids();
            Assert.Multiple(() =>
            {
                Assert.That(grids, Has.Count.EqualTo(footprint.Grids.Count));
                Assert.That(grids.Sum(TileCount), Is.EqualTo(footprint.Grids.Sum(Cells)), "Every tile arrives.");
                Assert.That(AnyScratch(), Is.False, "The scratch map goes once placed.");
            });

            if (footprint.Grids.Count == 1)
            {
                var (position, rotation) = WFMappingFootprint.Place(footprint.Grids[0], offset, 1);
                var (actual, actualRotation) = xformSys.GetWorldPositionRotation(grids[0]);
                Assert.Multiple(() =>
                {
                    Assert.That(Vector2.Distance(actual, position), Is.LessThan(0.01f));
                    Assert.That(actualRotation.EqualsApprox(rotation, 0.001), Is.True);
                });
            }
        });

        // A map file of two grids keeps how they sit relative to each other.
        await pair.RunSeconds(5.5f);
        await server.WaitPost(() => RaiseAs(entMan, player, new WFMappingReferencePickRequest(twoGridsPath)));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            Assert.That(clientReference.Footprint?.Token, Is.Not.EqualTo(footprint.Token), "A new footprint arrives.");
            footprint = clientReference.Footprint!;
            Assert.That(footprint.Grids, Has.Count.EqualTo(2));
        });

        await server.WaitAssertion(() =>
        {
            var before = Grids();
            RaiseAs(entMan, player, new WFMappingReferenceConfirmRequest(footprint.Token, new Vector2(-30, -20), 2));
            var added = Grids().Except(before).ToList();
            Assert.That(added, Has.Count.EqualTo(2));

            var strip = added.Single(g => TileCount(g) == 3);
            var square = added.Single(g => TileCount(g) == 4);
            var (stripPos, stripRot) = xformSys.GetWorldPositionRotation(strip);
            var (squarePos, squareRot) = xformSys.GetWorldPositionRotation(square);
            var expected = MappingToolsMath.RotationDelta(2).RotateVec(new Vector2(10, 4));
            Assert.Multiple(() =>
            {
                Assert.That(Vector2.Distance(squarePos - stripPos, expected), Is.LessThan(0.01f));
                Assert.That((squareRot - stripRot).EqualsApprox(Angle.FromDegrees(90), 0.001), Is.True);
                Assert.That(AnyScratch(), Is.False);
            });
        });

        // Over the grid cap: loaded aside, refused, and the scratch map is gone.
        await server.WaitAssertion(() =>
        {
            var maps = Maps();

            // Right after a load, the server-wide cooldown refuses anyone's pick before it loads.
            RaiseAs(entMan, dummies[2], new WFMappingReferencePickRequest(shipPath));
            Assert.Multiple(() =>
            {
                Assert.That(AnyScratch(), Is.False, "A pick during the cooldown loads nothing.");
                Assert.That(Maps(), Is.EqualTo(maps));
            });

            server.CfgMan.SetCVar(WFMappingModeCVars.ReferenceCooldown, 0f);
            server.CfgMan.SetCVar(WFMappingModeCVars.MaxGridsPerSession, mapping.CountGrids(session));
            RaiseAs(entMan, dummies[0], new WFMappingReferencePickRequest(shipPath));
            Assert.Multiple(() =>
            {
                Assert.That(AnyScratch(), Is.False, "A refused reference leaves no scratch map.");
                Assert.That(Maps(), Is.EqualTo(maps));
            });
            server.CfgMan.SetCVar(WFMappingModeCVars.MaxGridsPerSession, WFMappingModeCVars.MaxGridsPerSession.DefaultValue);

            // Too big: refused before anything loads.
            var grids = Grids().Count;
            server.CfgMan.SetCVar(WFMappingModeCVars.MaxReferenceBytes, 100L);
            RaiseAs(entMan, dummies[1], new WFMappingReferencePickRequest(shipPath));
            Assert.Multiple(() =>
            {
                Assert.That(AnyScratch(), Is.False);
                Assert.That(Maps(), Is.EqualTo(maps));
                Assert.That(Grids(), Has.Count.EqualTo(grids));
            });
            server.CfgMan.SetCVar(WFMappingModeCVars.MaxReferenceBytes, WFMappingModeCVars.MaxReferenceBytes.DefaultValue);
            server.CfgMan.SetCVar(WFMappingModeCVars.ReferenceCooldown, WFMappingModeCVars.ReferenceCooldown.DefaultValue);
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
