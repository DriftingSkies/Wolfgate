#nullable enable
using System.Linq;
using Content.Server._WF.Colonies;
using Content.Server.Bed.Components;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Light.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Colonies;

/// <summary>
/// Each colony seat's map loads as one roofed grid that fills its footprint, with a road socket pointing out of it, a
/// public landing pad, and what its tier promises: a depot's exchange, hub and bunks, a hall's charter office.
/// </summary>
[TestFixture]
[TestOf(typeof(WFColonySystem))]
public sealed class ColonySeatMapsTest
{
    [Test]
    public async Task SeatsLoadWholeWithWhatTheirTierPromises()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var seats = server.ProtoMan.EnumeratePrototypes<WFColonySeatPrototype>().ToList();
            Assert.That(seats.Select(seat => seat.Tier), Is.Unique, "Two seats share a tier.");
            Assert.That(seats.Select(seat => seat.Tier), Does.Contain(WFWorldTier.Settled).And.Contain(WFWorldTier.Colony).And.Contain(WFWorldTier.Charter));
            foreach (var seat in seats)
            {
                var site = seat.Site;
                Assert.That(site.MapPath, Is.Not.Null);
                Assert.That(site.Footprint, Is.Not.Null);
                Assert.That(site.Sockets, Has.Count.EqualTo(1));
                var footprint = site.Footprint!.Value;
                var socket = site.Sockets[0];
                var step = socket.Direction switch
                {
                    WFPlanetPoiDirection.North => new Vector2i(0, 1),
                    WFPlanetPoiDirection.East => new Vector2i(1, 0),
                    WFPlanetPoiDirection.South => new Vector2i(0, -1),
                    _ => new Vector2i(-1, 0),
                };
                Assert.That(footprint.ContainsTile(socket.Offset) && !footprint.ContainsTile(socket.Offset + step), Is.True,
                    $"{seat.ID}'s socket doesn't point out of its footprint.");

                maps.CreateMap(out var mapId);
                Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath(site.MapPath!), out var grid), Is.True, site.MapPath);
                var uid = grid!.Value.Owner;

                var grids = 0;
                var query = entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
                while (query.MoveNext(out _, out _, out var xform))
                {
                    if (xform.MapID == mapId)
                        grids++;
                }

                Assert.That(grids, Is.EqualTo(1), $"{site.MapPath} loaded in pieces.");
                Assert.That(entMan.HasComponent<RoofComponent>(uid), Is.True, $"{site.MapPath} has no roof.");
                var tiles = maps.GetAllTiles(uid, grid.Value.Comp).Select(tile => tile.GridIndices).ToList();
                Assert.That(tiles.All(tile => footprint.ContainsTile(tile)), Is.True, $"{site.MapPath} spills out of its footprint.");
                Assert.That(tiles, Has.Count.EqualTo(footprint.Width * footprint.Height), $"{site.MapPath} doesn't fill its footprint.");
                Assert.That(maps.GetTileRef(uid, grid.Value.Comp, socket.Offset).Tile.IsEmpty, Is.False, "The socket has no floor.");

                int Count(string proto) => entMan.EntityQuery<MetaDataComponent, TransformComponent>()
                    .Count(found => found.Item2.GridUid == uid && found.Item1.EntityPrototype?.ID == proto);
                Assert.That(Count("WFPoiPublicLandingPad"), Is.EqualTo(1), $"{seat.ID} needs its public landing pad.");
                var depot = seat.Tier >= WFWorldTier.Colony;
                Assert.That(Count("WFColonyDepotVendor"), Is.EqualTo(depot ? 1 : 0), $"{seat.ID}: the exchange vendor comes with the depot.");
                Assert.That(Count("WFColonyDepotChute"), Is.EqualTo(depot ? 1 : 0), $"{seat.ID}: the exchange chute comes with the depot.");
                Assert.That(Count("WFPoiPublicTradeHub"), Is.EqualTo(depot ? 1 : 0), $"{seat.ID}: the drop hub comes with the depot.");
                Assert.That(Count("WFColonyOfficeTerminal"), Is.EqualTo(seat.Tier >= WFWorldTier.Charter ? 1 : 0),
                    $"{seat.ID}: the charter office comes with the hall.");
                Assert.That(Count("WFColonyHallBeacon"), Is.EqualTo(seat.Tier >= WFWorldTier.Charter ? 1 : 0));

                // Its bunks house the people its prototype says.
                var beds = entMan.EntityQuery<HealOnBuckleComponent, TransformComponent>().Count(found => found.Item2.GridUid == uid);
                Assert.That(seat.Housing, Is.EqualTo(beds * 4), $"{seat.ID} houses four to a bunk.");
                Assert.That(seat.Colonists, Is.LessThanOrEqualTo(seat.Housing), $"{seat.ID} can't lodge its colonists.");
                maps.DeleteMap(mapId);
            }
        });

        await pair.CleanReturnAsync();
    }
}
