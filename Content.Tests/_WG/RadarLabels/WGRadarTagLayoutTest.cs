using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.RadarLabels;
using NUnit.Framework;

namespace Content.Tests._WG.RadarLabels;

[TestFixture]
[TestOf(typeof(WGRadarTagLayout))]
public sealed class WGRadarTagLayoutTest
{
    private static readonly Vector2 Bounds = new(800, 800);

    private static WGRadarTagLayout.Tag Tag(float x, float y, int priority = 0, float width = 60, float lines = 1, int key = -1)
    {
        return new WGRadarTagLayout.Tag
        {
            Key = key >= 0 ? key : (int) (x * 1000 + y),
            Anchor = new Vector2(x, y),
            Position = new Vector2(x + 5, y - 6),
            Size = new Vector2(width, 12 * lines),
            HeadSize = new Vector2(width, 12),
            Priority = priority,
        };
    }

    [Test]
    public void TagsWithRoomStandAlone()
    {
        var layout = new WGRadarTagLayout();
        layout.Solve(new[] { Tag(100, 100), Tag(100, 200), Tag(400, 100) }, Bounds);

        Assert.That(layout.Groups, Has.Count.EqualTo(3));
        Assert.That(layout.Groups.All(group => !group.Stacked));
    }

    [Test]
    public void OverlappingTagsStackTopToBottomByContact()
    {
        var layout = new WGRadarTagLayout();
        layout.Solve(new[] { Tag(100, 108), Tag(110, 100) }, Bounds);

        Assert.That(layout.Groups, Has.Count.EqualTo(1));
        var stack = layout.Groups[0];
        Assert.That(stack.Stacked);
        Assert.That(stack.Rows, Is.EqualTo(new[] { 1, 0 }), "Rows follow their contacts down the scope, so leaders don't cross.");
        Assert.That(stack.Box.Height, Is.EqualTo(24).Within(0.01f), "A stack lists only each tag's first line.");
    }

    [Test]
    public void StackSplitsWhenZoomMakesRoom()
    {
        var layout = new WGRadarTagLayout();
        var close = new[] { Tag(300, 300, key: 1), Tag(305, 306, key: 2), Tag(310, 312, key: 3) };
        layout.Solve(close, Bounds);
        Assert.That(layout.Groups, Has.Count.EqualTo(1));

        // Zooming in five times about the scope's middle spreads the contacts apart, past the stack's sticky margin.
        var centre = Bounds / 2;
        var zoomed = close.Select(tag => Tag(centre.X + (tag.Anchor.X - centre.X) * 5, centre.Y + (tag.Anchor.Y - centre.Y) * 5, key: tag.Key)).ToArray();
        layout.Solve(zoomed, Bounds);
        Assert.That(layout.Groups, Has.Count.EqualTo(3));
        Assert.That(layout.Groups.All(group => !group.Stacked));
    }

    [Test]
    public void StackTakesInTagsItGrowsOnto()
    {
        var layout = new WGRadarTagLayout();
        // The first two overlap; their stack grows down onto the third, which stood clear of each of them alone.
        layout.Solve(new[] { Tag(100, 100), Tag(100, 104), Tag(100, 124) }, Bounds);

        Assert.That(layout.Groups, Has.Count.EqualTo(1));
        Assert.That(layout.Groups[0].All, Has.Count.EqualTo(3));
    }

    [Test]
    public void TallTagsCountTheirWholeHeight()
    {
        var layout = new WGRadarTagLayout();
        // A four-line IFF tag reaches down onto a tag below it that its first line alone would miss.
        layout.Solve(new[] { Tag(100, 100, lines: 4), Tag(100, 130) }, Bounds);

        Assert.That(layout.Groups, Has.Count.EqualTo(1));
    }

    [Test]
    public void FullStackKeepsHighestPriorities()
    {
        var layout = new WGRadarTagLayout { MaxRows = 4 };
        var tags = new List<WGRadarTagLayout.Tag>();
        for (var i = 0; i < 7; i++)
        {
            tags.Add(Tag(200, 200 + i, priority: i == 5 ? 9 : 0));
        }

        layout.Solve(tags, Bounds);

        var stack = layout.Groups.Single();
        Assert.That(stack.Rows, Has.Count.EqualTo(4));
        Assert.That(stack.Hidden, Is.EqualTo(3));
        Assert.That(stack.Rows, Does.Contain(5), "The highest priority tag must make the cut.");
    }

    [Test]
    public void StackStaysOnScreen()
    {
        var layout = new WGRadarTagLayout();
        layout.Solve(new[] { Tag(790, 795, width: 120), Tag(792, 796, width: 120) }, Bounds);

        var box = layout.Groups.Single().Box;
        Assert.That(box.Right, Is.LessThanOrEqualTo(Bounds.X));
        Assert.That(box.Top, Is.LessThanOrEqualTo(Bounds.Y));
        Assert.That(box.Left, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void StackHoldsTogetherOnTheEdge()
    {
        var layout = new WGRadarTagLayout();
        // Overlapping, they stack; just clear of each other they would stand alone, but a stack holds until there is clear room.
        layout.Solve(new[] { Tag(100, 100, key: 1), Tag(100, 110, key: 2) }, Bounds);
        Assert.That(layout.Groups, Has.Count.EqualTo(1));

        layout.Solve(new[] { Tag(100, 100, key: 1), Tag(100, 116, key: 2) }, Bounds);
        Assert.That(layout.Groups, Has.Count.EqualTo(1), "A stack split the moment its tags were barely clear.");

        layout.Solve(new[] { Tag(100, 100, key: 1), Tag(100, 140, key: 2) }, Bounds);
        Assert.That(layout.Groups, Has.Count.EqualTo(2));

        var fresh = new WGRadarTagLayout();
        fresh.Solve(new[] { Tag(100, 100, key: 1), Tag(100, 116, key: 2) }, Bounds);
        Assert.That(fresh.Groups, Has.Count.EqualTo(2), "Tags never stacked shouldn't stick.");
    }

    [Test]
    public void RowsKeepTheirOrderUntilContactsReallyPass()
    {
        var layout = new WGRadarTagLayout();
        layout.Solve(new[] { Tag(100, 100, key: 1), Tag(104, 102, key: 2) }, Bounds);
        var first = layout.Groups.Single().Rows.Select(index => index).ToArray();
        Assert.That(first, Is.EqualTo(new[] { 0, 1 }));

        // The second contact edges a few pixels above the first: the rows stay put.
        layout.Solve(new[] { Tag(100, 100, key: 1), Tag(104, 97, key: 2) }, Bounds);
        Assert.That(layout.Groups.Single().Rows, Is.EqualTo(new[] { 0, 1 }), "Rows traded places over a few pixels.");

        // Well above, they swap.
        layout.Solve(new[] { Tag(100, 100, key: 1), Tag(104, 80, key: 2) }, Bounds);
        Assert.That(layout.Groups.Single().Rows, Is.EqualTo(new[] { 1, 0 }));
    }
}
