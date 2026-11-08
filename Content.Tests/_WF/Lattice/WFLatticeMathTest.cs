using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.Lattice;
using NUnit.Framework;

namespace Content.Tests._WF.Lattice;

/// <summary>The Lattice field's bands, depth and garbling.</summary>
[TestFixture]
public sealed class WFLatticeMathTest
{
    private static readonly List<float> Edges = new() { 0f, 18000f, 22000f, 25000f, 30000f };
    private const float Reach = 4000f;

    private static List<(Vector2, float)> Hexagon(int skip = -1)
    {
        var anchors = new List<(Vector2, float)>();
        var corners = WFLatticeMath.Corners(Vector2.Zero, 6, 3000f, 30f);
        for (var i = 0; i < corners.Count; i++)
        {
            if (i != skip)
                anchors.Add((corners[i], 1f));
        }

        return anchors;
    }

    [Test]
    public void IntactLatticeBandsFollowTheEdges()
    {
        var anchors = Hexagon();
        var thresholds = WFLatticeMath.Thresholds(Edges, anchors, Reach, Vector2.Zero);
        Assert.That(thresholds, Is.Ordered.Descending);

        var samples = new[] { (10000f, 0), (20000f, 1), (23500f, 2), (27000f, 3), (35000f, 4), (60000f, 4) };
        for (var angle = 0f; angle < MathF.Tau; angle += 0.4f)
        {
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            foreach (var (distance, band) in samples)
            {
                var field = WFLatticeMath.Field(direction * distance, anchors, Reach);
                Assert.That(WFLatticeMath.Band(field, thresholds), Is.EqualTo(band), $"{distance} m at {angle} rad");
            }
        }
    }

    [Test]
    public void LostAnchorPullsItsSideInward()
    {
        var thresholds = WFLatticeMath.Thresholds(Edges, Hexagon(), Reach, Vector2.Zero);
        var broken = Hexagon(skip: 0);
        var towards = WFLatticeMath.Corners(Vector2.Zero, 6, 1f, 30f)[0];
        var away = -towards;

        Assert.That(WFLatticeMath.Band(WFLatticeMath.Field(towards * 16000f, broken, Reach), thresholds), Is.EqualTo(1));
        Assert.That(WFLatticeMath.Band(WFLatticeMath.Field(away * 16000f, broken, Reach), thresholds), Is.EqualTo(0));
    }

    [Test]
    public void DepthGrowsOutward()
    {
        var anchors = Hexagon();
        var thresholds = WFLatticeMath.Thresholds(Edges, anchors, Reach, Vector2.Zero);
        var last = -1f;
        foreach (var distance in new[] { 30500f, 35000f, 45000f, 90000f })
        {
            var field = WFLatticeMath.Field(new Vector2(distance, 0f), anchors, Reach);
            var depth = WFLatticeMath.Depth(field, 4, thresholds);
            Assert.That(depth, Is.GreaterThan(last).And.InRange(0f, 1f));
            last = depth;
        }

        Assert.That(WFLatticeMath.Depth(1f, 0, thresholds), Is.Zero);
    }

    [Test]
    public void GarbleIsStableAndKeepsShape()
    {
        const string text = "the Deep is here";
        Assert.That(WFLatticeMath.Garble(text, 0f, 3), Is.EqualTo(text));

        var garbled = WFLatticeMath.Garble(text, 0.5f, 3);
        Assert.That(garbled, Is.EqualTo(WFLatticeMath.Garble(text, 0.5f, 3)));
        Assert.That(garbled, Is.Not.EqualTo(text));
        Assert.That(garbled.Length, Is.EqualTo(text.Length));
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
                Assert.That(garbled[i], Is.EqualTo(' '));
        }

        Assert.That(WFLatticeMath.Garble(text, 1f, 3).Trim('#', '$', '%', '@', '!', '&', '*', '?', '~', '^', ' '), Is.Empty);
    }
}
