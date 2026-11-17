#nullable enable
using System;
using System.Threading.Tasks;
using Content.Server._WG.SectorControl.Systems;
using Content.Shared._WG.SectorControl;

namespace Content.IntegrationTests.Tests._WG.SectorControl;

/// <summary>Only the cells the sector map draws can be held: nothing past the sector's edge is claimed or contested.</summary>
[TestOf(typeof(WGSectorTerritorySystem))]
public sealed class WGSectorEdgeTest : WGSectorTestBase
{
    [TearDown]
    public async Task ClearTerritory()
    {
        await Server.WaitPost(() => Server.System<WGSectorTerritorySystem>().Clear(MapData.MapId));
    }

    [Test]
    public async Task CellsPastTheSectorEdgeCannotBeHeld()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WGSectorTerritorySystem>();
            var map = MapData.MapId;
            var size = territory.CellSize;
            // The first cell out along the axis whose centre lies past the edge.
            var outside = new WGSectorCell(0, 0);
            for (var q = 1; WGSectorHex.InSector(outside, size); q++)
            {
                outside = new WGSectorCell(q, 0);
            }

            var edge = new WGSectorCell(outside.Q - 1, 0);
            Assert.That(WGSectorHex.InSector(edge, size), Is.True);
            Assert.That(territory.CanClaim(map, outside, Holder), Is.False, "A cell the map does not draw can't be claimed.");
            Assert.That(territory.TryClaim(map, outside, Holder, null), Is.False);
            Assert.That(territory.Contest(map, outside, Holder, STiming.CurTime + TimeSpan.FromMinutes(5)), Is.False, "Nor contested.");
            Assert.That(territory.TryClaim(map, edge, Holder, null), Is.True, "The outermost drawn cell can.");

            // In the outside cell's own column, a cell nearer the origin is drawn and can be held: it is the distance
            // of a cell's centre that counts, not its column.
            var column = new WGSectorCell(outside.Q, -1);
            for (var r = -1; !WGSectorHex.InSector(column, size) && r > -outside.Q; r--)
            {
                column = new WGSectorCell(outside.Q, r);
            }

            Assert.That(WGSectorHex.InSector(column, size), Is.True);
            Assert.That(territory.TryClaim(map, column, Holder, null), Is.True, "A drawn cell in the same column can be held.");
        });
    }
}
