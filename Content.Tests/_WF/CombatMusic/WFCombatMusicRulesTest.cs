#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.CombatMusic;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.Tests._WF.CombatMusic;

[TestFixture]
[TestOf(typeof(WFCombatMusicRules))]
public sealed class WFCombatMusicRulesTest
{
    [Test]
    public void StoredPlaylistRoundTrips()
    {
        var tracks = new List<ProtoId<WFCombatTrackPrototype>> { "WFCombatTrackMobster", "WFCombatTrackUnholy" };
        var stored = WFCombatMusicRules.ToStored(tracks);

        Assert.That(stored, Is.EqualTo("WFCombatTrackMobster,WFCombatTrackUnholy"));
        Assert.That(WFCombatMusicRules.FromStored(stored), Is.EqualTo(tracks));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase(",,")]
    public void EmptyStoredPlaylistIsFactionDefault(string? stored)
    {
        Assert.That(WFCombatMusicRules.FromStored(stored), Is.Empty);
    }

    [Test]
    public void StoredPlaylistIgnoresSpaces()
    {
        var tracks = WFCombatMusicRules.FromStored(" WFCombatTrackMobster , WFCombatTrackUnholy ");
        Assert.That(tracks.Select(t => t.Id), Is.EqualTo(new[] { "WFCombatTrackMobster", "WFCombatTrackUnholy" }));
    }
}
