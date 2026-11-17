#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.CombatMusic;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.Tests._WG.CombatMusic;

[TestFixture]
[TestOf(typeof(WGCombatMusicRules))]
public sealed class WGCombatMusicRulesTest
{
    [Test]
    public void StoredPlaylistRoundTrips()
    {
        var tracks = new List<ProtoId<WGCombatTrackPrototype>> { "WGCombatTrackMobster", "WGCombatTrackUnholy" };
        var stored = WGCombatMusicRules.ToStored(tracks);

        Assert.That(stored, Is.EqualTo("WGCombatTrackMobster,WGCombatTrackUnholy"));
        Assert.That(WGCombatMusicRules.FromStored(stored), Is.EqualTo(tracks));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase(",,")]
    public void EmptyStoredPlaylistIsFactionDefault(string? stored)
    {
        Assert.That(WGCombatMusicRules.FromStored(stored), Is.Empty);
    }

    [Test]
    public void StoredPlaylistIgnoresSpaces()
    {
        var tracks = WGCombatMusicRules.FromStored(" WGCombatTrackMobster , WGCombatTrackUnholy ");
        Assert.That(tracks.Select(t => t.Id), Is.EqualTo(new[] { "WGCombatTrackMobster", "WGCombatTrackUnholy" }));
    }
}
