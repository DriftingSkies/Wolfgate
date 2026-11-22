using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.MarkingGlow;
using Content.Shared.Humanoid.Markings;
using NUnit.Framework;

namespace Content.Tests._WG.MarkingGlow;

/// <summary>How the list of what glows is cleaned, changed and stored.</summary>
[TestFixture]
[TestOf(typeof(MarkingGlowRules))]
public sealed class MarkingGlowRulesTest
{
    [Test]
    public void CleanTest()
    {
        var worn = new List<Marking> { new("TattooA", 1), new("TattooB", 1) };
        var glow = new[] { "TattooB", "Gone", MarkingGlowRules.Eyes, "TattooB", "", "Bad;Id", "TattooA", MarkingGlowRules.Hair };

        Assert.Multiple(() =>
        {
            Assert.That(MarkingGlowRules.Clean(glow, worn),
                Is.EqualTo(new[] { MarkingGlowRules.Eyes, MarkingGlowRules.Hair, "TattooA", "TattooB" }),
                "keys stay, markings not worn go, and the rest is sorted without repeats");
            Assert.That(MarkingGlowRules.Clean(glow), Does.Contain("Gone"), "without a worn list every ID stays");
            Assert.That(MarkingGlowRules.Clean(null), Is.Empty);
            Assert.That(MarkingGlowRules.Clean(Enumerable.Range(0, 200).Select(i => $"M{i}")), Has.Count.EqualTo(MarkingGlowRules.MaxEntries));
        });
    }

    [Test]
    public void SetAndStoreTest()
    {
        var glow = MarkingGlowRules.Set(new List<string>(), "TattooA", true);
        glow = MarkingGlowRules.Set(glow, MarkingGlowRules.FacialHair, true);
        glow = MarkingGlowRules.Set(glow, "TattooA", true);

        Assert.Multiple(() =>
        {
            Assert.That(glow, Is.EqualTo(new[] { MarkingGlowRules.FacialHair, "TattooA" }));
            Assert.That(MarkingGlowRules.Set(glow, "TattooA", false), Is.EqualTo(new[] { MarkingGlowRules.FacialHair }));
            Assert.That(MarkingGlowRules.FromStored(MarkingGlowRules.ToStored(glow)), Is.EqualTo(glow));
            Assert.That(MarkingGlowRules.FromStored(""), Is.Empty);
            Assert.That(MarkingGlowRules.FromStored(null), Is.Empty);
            Assert.That(MarkingGlowRules.IsKey("TattooA"), Is.False);
        });
    }
}
