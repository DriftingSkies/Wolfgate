using System.Collections.Generic;
using Content.Shared._WF.ExamineCard;
using NUnit.Framework;
using Robust.Shared.Utility;

namespace Content.Tests._WF.ExamineCard;

/// <summary>Line splitting, pattern matching and folding of examine markup into chips.</summary>
[TestFixture]
[TestOf(typeof(ExamineCardText))]
public sealed class ExamineCardTextTest
{
    private const char W = ExamineCardText.Wildcard;

    [TestCase("It is holding a wrench.", true)]
    [TestCase("It is holding a wrench and a crowbar.", true)]
    [TestCase("It is holding.", false)]
    [TestCase("He is holding a wrench.", false)]
    public void MatchesFragmentsInOrder(string line, bool expected)
    {
        var fragments = $"It is holding {W}.".Split(W);
        Assert.That(ExamineCardText.Matches(line, fragments), Is.EqualTo(expected));
    }

    [Test]
    public void SplitKeepsEachLineBalanced()
    {
        var message = FormattedMessage.FromMarkupOrThrow("[color=gray]one\ntwo[/color]\nthree");
        var lines = ExamineCardText.SplitLines(message);

        Assert.That(lines, Has.Count.EqualTo(3));
        foreach (var line in lines)
        {
            var depth = 0;
            foreach (var node in line)
            {
                if (node.Name != null)
                    depth += node.Closing ? -1 : 1;
            }
            Assert.That(depth, Is.Zero);
        }
    }

    [Test]
    public void FoldTurnsLinesIntoChipsAndKeepsTheRest()
    {
        var message = FormattedMessage.FromMarkupOrThrow(
            "[color=darkgray]It is a young human.\n- It is holding a [color=paleturquoise]wrench[/color].\n" +
            "[color=mediumpurple]He is totally catatonic.[/color]\nHe smells of smoke.[/color]");
        var patterns = new List<ExamineCardPattern>
        {
            new(null, ExamineChipTone.Neutral, $"It is a {W} {W}.".Split(W)),
            new(null, ExamineChipTone.Neutral, $"It is holding {W}.".Split(W)),
            new("CATATONIC", ExamineChipTone.Mind, new[] { "He is totally catatonic." }),
        };

        var chips = new List<ExamineCardChip>();
        var folded = ExamineCardText.Fold(message, patterns, chips);

        Assert.Multiple(() =>
        {
            Assert.That(folded.ToString(), Is.EqualTo("He smells of smoke."));
            Assert.That(chips, Has.Count.EqualTo(1));
            Assert.That(chips[0].Label, Is.EqualTo("CATATONIC"));
            Assert.That(chips[0].Text, Is.EqualTo("He is totally catatonic."));
        });
    }

    [Test]
    public void FoldKeepsDataTagsOnAFoldedLine()
    {
        var message = FormattedMessage.FromMarkupOrThrow("You look it over.[wolfmedlook name=\"torso\"][/wolfmedlook]\nIts torso: bent.");
        var patterns = new List<ExamineCardPattern> { new(null, ExamineChipTone.Neutral, new[] { "You look it over." }) };

        var folded = ExamineCardText.Fold(message, patterns, new List<ExamineCardChip>());

        Assert.That(folded.ToString(), Is.EqualTo("\nIts torso: bent."));
        Assert.That(folded.Nodes, Has.Some.Matches<MarkupNode>(n => n.Name == "wolfmedlook"));
    }
}
