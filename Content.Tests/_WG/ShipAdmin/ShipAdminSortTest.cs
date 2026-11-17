using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.ShipAdmin;
using Content.Shared._WG.ShipAdmin;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Tests._WG.ShipAdmin;

/// <summary>
/// Shipmaster's list order: the ship the viewer is on first, then their map by distance, then other maps; and the
/// other sorts by name, owner and how many are aboard.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipAdminWindow))]
public sealed class ShipAdminSortTest
{
    private static readonly MapId Here = new(1);
    private static readonly MapId Elsewhere = new(2);

    private static List<WGShipAdminRow> Ships()
    {
        return new List<WGShipAdminRow>
        {
            new() { Grid = new NetEntity(1), Name = "Delta", Owner = "Ada", Occupants = 0, MapId = Here, Position = new Vector2(300, 0) },
            new() { Grid = new NetEntity(2), Name = "Alpha", Owner = string.Empty, Occupants = 3, MapId = Elsewhere, Position = new Vector2(1, 0) },
            new() { Grid = new NetEntity(3), Name = "Charlie", Owner = "Ben", Occupants = 1, MapId = Here, Position = new Vector2(20, 0) },
            new() { Grid = new NetEntity(4), Name = "Bravo", Owner = "Ada", Occupants = 1, MapId = Here, Position = new Vector2(900, 0) },
        };
    }

    private static string Order(List<WGShipAdminRow> ships)
    {
        return string.Join(" ", ships.Select(ship => ship.Name));
    }

    [Test]
    public void NearestPutsTheViewersShipFirstThenTheirMapByDistance()
    {
        var ships = Ships();
        WGShipAdminWindow.Sort(ships, WGShipAdminSort.Nearest, new NetEntity(4), Here, Vector2.Zero);
        Assert.That(Order(ships), Is.EqualTo("Bravo Charlie Delta Alpha"), "The ship stood on leads though it is listed far away; another map comes last.");

        WGShipAdminWindow.Sort(ships, WGShipAdminSort.Nearest, null, Here, new Vector2(1000, 0));
        Assert.That(Order(ships), Is.EqualTo("Bravo Delta Charlie Alpha"));
    }

    [Test]
    public void NearestWithNobodyToMeasureFromIsByName()
    {
        var ships = Ships();
        WGShipAdminWindow.Sort(ships, WGShipAdminSort.Nearest, null, null, Vector2.Zero);
        Assert.That(Order(ships), Is.EqualTo("Alpha Bravo Charlie Delta"));
    }

    [Test]
    public void TheOtherSortsBreakTiesByName()
    {
        var ships = Ships();
        WGShipAdminWindow.Sort(ships, WGShipAdminSort.Name, null, Here, Vector2.Zero);
        Assert.That(Order(ships), Is.EqualTo("Alpha Bravo Charlie Delta"));

        WGShipAdminWindow.Sort(ships, WGShipAdminSort.Owner, null, Here, Vector2.Zero);
        Assert.That(Order(ships), Is.EqualTo("Bravo Delta Charlie Alpha"), "Unowned ships come last.");

        WGShipAdminWindow.Sort(ships, WGShipAdminSort.Aboard, null, Here, Vector2.Zero);
        Assert.That(Order(ships), Is.EqualTo("Alpha Bravo Charlie Delta"));
    }
}
