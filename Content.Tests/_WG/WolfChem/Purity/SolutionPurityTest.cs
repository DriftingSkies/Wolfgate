using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Tests._WG.WolfChem.Purity;

/// <summary>
/// Grades blend by volume, follow splits and transfers, and leave no table on solutions that never had one.
/// </summary>
[TestFixture, Parallelizable, TestOf(typeof(Solution))]
public sealed class SolutionPurityTest : ContentUnitTest
{
    [OneTimeSetUp]
    public void Setup()
    {
        IoCManager.Resolve<IPrototypeManager>().Initialize();
    }

    [Test]
    public void BlendTest()
    {
        var solution = new Solution();
        solution.AddReagent("water", FixedPoint2.New(10), 0.6f);
        Assert.That(solution.GetPurity("water"), Is.EqualTo(0.6f).Within(0.001f));

        solution.AddReagent("water", FixedPoint2.New(10));
        Assert.That(solution.GetPurity("water"), Is.EqualTo(0.8f).Within(0.001f), "Standard stock should dilute the grade.");
        Assert.That(solution.GetEffectiveQuantity("water").Float(), Is.EqualTo(16f).Within(0.02f));

        var other = new Solution();
        other.AddReagent("water", FixedPoint2.New(20), 0.4f);
        solution.AddSolution(other, null);
        Assert.That(solution.GetPurity("water"), Is.EqualTo(0.6f).Within(0.001f), "AddSolution should blend by volume.");
    }

    [Test]
    public void SplitTest()
    {
        var solution = new Solution();
        solution.AddReagent("water", FixedPoint2.New(10), 0.5f);
        solution.AddReagent("fire", FixedPoint2.New(10));

        var split = solution.SplitSolution(FixedPoint2.New(6));
        Assert.Multiple(() =>
        {
            Assert.That(split.GetPurity("water"), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(solution.GetPurity("water"), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(split.GetPurity("fire"), Is.EqualTo(1f));
        });

        var only = solution.SplitSolutionWithOnly(FixedPoint2.New(2), "water");
        Assert.Multiple(() =>
        {
            Assert.That(only.GetPurity("water"), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(solution.GetPurity("water"), Is.EqualTo(0.5f).Within(0.001f), "The re-added remainder kept its grade.");
        });

        var without = solution.SplitSolutionWithout(FixedPoint2.New(2), "fire");
        Assert.That(without.GetPurity("water"), Is.EqualTo(0.5f).Within(0.001f));

        var perReagent = solution.SplitSolutionPerReagent(FixedPoint2.New(100));
        Assert.Multiple(() =>
        {
            Assert.That(perReagent.GetPurity("water"), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(solution.Purity, Is.Null);
        });

        var clone = perReagent.Clone();
        Assert.That(clone.GetPurity("water"), Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void RemoveTest()
    {
        var solution = new Solution();
        solution.AddReagent("water", FixedPoint2.New(10), 0.5f);
        solution.AddReagent("fire", FixedPoint2.New(10));

        solution.RemoveReagent("water", FixedPoint2.New(4));
        Assert.That(solution.GetPurity("water"), Is.EqualTo(0.5f).Within(0.001f), "Removing part keeps the grade.");

        solution.RemoveReagent("water", FixedPoint2.New(10));
        Assert.That(solution.Purity, Is.Null, "Removing the reagent removes its key.");

        solution.AddReagent("water", FixedPoint2.New(10), 0.5f);
        solution.RemoveSolution(solution.Volume);
        Assert.That(solution.Purity, Is.Null);
    }

    [Test]
    public void UngradedTest()
    {
        var solution = new Solution();
        solution.AddReagent("water", FixedPoint2.New(10));
        solution.AddReagent("fire", FixedPoint2.New(10));
        var split = solution.SplitSolution(FixedPoint2.New(5));
        var target = new Solution();
        target.AddSolution(split, null);
        target.ScaleSolution(0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(solution.Purity, Is.Null);
            Assert.That(split.Purity, Is.Null);
            Assert.That(target.Purity, Is.Null);
            Assert.That(target.GetEffectiveQuantity("water"), Is.EqualTo(target.GetTotalPrototypeQuantity("water")));
        });

        target.AddReagent("water", FixedPoint2.New(1), 1f);
        Assert.That(target.Purity, Is.Null, "A standard-grade addition stores nothing.");
    }
}
