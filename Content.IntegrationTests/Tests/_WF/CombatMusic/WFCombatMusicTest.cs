#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Shared._WF.CombatMusic;
using Content.Shared.Preferences;
using Robust.Shared.ContentPack;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.CombatMusic;

[TestFixture]
[TestOf(typeof(WFCombatMusicRules))]
public sealed class WFCombatMusicTest
{
    [Test]
    public async Task EveryTrackHasItsFileAndText()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var res = client.ResolveDependency<IResourceManager>();
        var loc = client.ResolveDependency<ILocalizationManager>();

        await client.WaitAssertion(() =>
        {
            var tracks = WFCombatMusicRules.Ordered(proto);
            Assert.That(tracks, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                foreach (var track in tracks)
                {
                    Assert.That(res.ContentFileExists(track.Path), $"{track.ID}: no file at {track.Path}.");
                    Assert.That(loc.HasString(track.Name), $"{track.ID}: no name string.");
                    Assert.That(loc.HasString(track.Credits), $"{track.ID}: no credits string.");
                    if (track.Description is { } desc)
                        Assert.That(loc.HasString(desc), $"{track.ID}: no description string.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ValidationDropsUnknownAndRepeatedTracks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var known = WFCombatMusicRules.Ordered(proto).First().ID;
            var tracks = new List<ProtoId<WFCombatTrackPrototype>> { known, "WFCombatTrackDoesNotExist", known };

            Assert.That(WFCombatMusicRules.Validate(tracks, proto), Is.EqualTo(new List<ProtoId<WFCombatTrackPrototype>> { known }));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProfileCopiesAndComparesThePlaylist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var known = WFCombatMusicRules.Ordered(proto).First().ID;
            var plain = HumanoidCharacterProfile.Random();
            var withMusic = plain.WithCombatMusic(new[] { new ProtoId<WFCombatTrackPrototype>(known) });

            Assert.That(withMusic.CombatMusic, Is.EqualTo(new List<ProtoId<WFCombatTrackPrototype>> { known }));
            Assert.That(new HumanoidCharacterProfile(withMusic).CombatMusic, Is.EqualTo(withMusic.CombatMusic));
            Assert.That(withMusic.MemberwiseEquals(plain.WithCombatMusic(new List<ProtoId<WFCombatTrackPrototype>>())), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
