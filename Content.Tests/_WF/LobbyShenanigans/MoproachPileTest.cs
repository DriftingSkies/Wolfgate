using System.Collections.Generic;
using System.Numerics;
using Content.Client._WF.LobbyShenanigans;
using NUnit.Framework;

namespace Content.Tests._WF.LobbyShenanigans;

/// <summary>Dropped moproaches come to rest on the floor, inside the screen, without sinking into each other.</summary>
[TestFixture]
[TestOf(typeof(MoproachPile))]
public sealed class MoproachPileTest
{
    private static readonly Vector2 Bounds = new(800, 600);

    private static MoproachPile Drop(int count)
    {
        var pile = new MoproachPile();
        var random = new System.Random(7);
        for (var i = 0; i < count; i++)
        {
            pile.Bodies.Add(new MoproachPile.Body
            {
                Pos = new Vector2(20 + random.NextSingle() * 760, -40 - i * 30),
                Vel = new Vector2((random.NextSingle() - 0.5f) * 300, 0),
                Radius = 20,
            });
        }
        for (var t = 0f; t < 10f; t += 1f / 60f)
            pile.Step(1f / 60f, Bounds);
        return pile;
    }

    [Test]
    public void OneLandsOnTheFloorAndStops()
    {
        var body = Drop(1).Bodies[0];
        Assert.That(body.Pos.Y, Is.EqualTo(Bounds.Y - body.Radius).Within(0.5f));
        Assert.That(body.Vel.Length(), Is.LessThan(5f));
    }

    [Test]
    public void ManySettleIntoAPileInsideTheScreen()
    {
        var pile = Drop(60);
        foreach (var body in pile.Bodies)
        {
            Assert.That(body.Pos.X, Is.InRange(body.Radius - 0.5f, Bounds.X - body.Radius + 0.5f));
            Assert.That(body.Pos.Y, Is.InRange(0f, Bounds.Y - body.Radius + 0.5f));
            Assert.That(body.Vel.Length(), Is.LessThan(40f), "settled, not jittering");
        }
        for (var i = 0; i < pile.Bodies.Count; i++)
        for (var j = i + 1; j < pile.Bodies.Count; j++)
        {
            var a = pile.Bodies[i];
            var b = pile.Bodies[j];
            Assert.That((a.Pos - b.Pos).Length(), Is.GreaterThan((a.Radius + b.Radius) * 0.8f), "no deep overlaps");
        }
    }

    [Test]
    public void AHeldOneFollowsTheMouseShovesTheRestAndFliesWhenLetGo()
    {
        var pile = Drop(20);
        var held = pile.Bodies[0];
        pile.Grab(held, held.Pos);
        var start = held.Pos;
        for (var i = 1; i <= 30; i++)
        {
            pile.Drag(start + new Vector2(0, -10f * i), 1f / 60f);
            pile.Step(1f / 60f, Bounds);
        }
        Assert.That(held.Pos.Y, Is.EqualTo(start.Y - 300).Within(1f), "it stays where the mouse put it");
        foreach (var other in pile.Bodies)
        {
            if (other != held)
                Assert.That((other.Pos - held.Pos).Length(), Is.GreaterThan((other.Radius + held.Radius) * 0.95f), "the others are shoved aside");
        }

        pile.Release();
        Assert.That(held.Vel.Y, Is.LessThan(-300f), "thrown upwards the way it was dragged");
        Assert.That(pile.Held, Is.Null);
        for (var t = 0f; t < 6f; t += 1f / 60f)
            pile.Step(1f / 60f, Bounds);
        Assert.That(held.Pos.Y, Is.GreaterThan(Bounds.Y - held.Radius * 8), "and falls back down");
    }

    [Test]
    public void SomethingSwimmingThroughKnocksThemAside()
    {
        var pile = Drop(20);
        var target = pile.Bodies[5];
        var at = target.Pos + new Vector2(-5, 0);
        pile.Shove(at, 30, new Vector2(200, 0));
        Assert.That((target.Pos - at).Length(), Is.EqualTo(30 + target.Radius).Within(0.01f), "pushed out to the edge");
        Assert.That(target.Vel.X, Is.GreaterThan(200f), "and sent on the way the swimmer was going");
    }

    [Test]
    public void ASingularitySpiralsThemInAndSwallowsTheClosestButNeverTheHeldOne()
    {
        var pile = Drop(20);
        var held = pile.Bodies[0];
        pile.Grab(held, held.Pos);
        var at = new Vector2(400, 500);
        var swallowed = new List<MoproachPile.Body>();
        for (var t = 0f; t < 4f; t += 1f / 60f)
        {
            pile.Drag(held.Pos, 1f / 60f);
            pile.Pull(at, 600, 30, 6000, 1f / 60f, swallowed);
            pile.Step(1f / 60f, Bounds);
        }
        Assert.That(swallowed, Is.Not.Empty, "it eats");
        Assert.That(pile.Bodies, Does.Contain(held), "but not what the mouse is holding");
        Assert.That(pile.Bodies.Count + swallowed.Count, Is.EqualTo(20), "what it eats leaves the pile");
    }
}
