#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Content.Server._WG.EmoteVoices;
using Content.Server.Chat.Systems;
using Content.Server.Database;
using Content.Server.Polymorph.Systems;
using Content.Shared._WG.EmoteVoices;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Cloning;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Speech.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.EmoteVoices;

/// <summary>
/// The voice catalogue is sound and offers every species' own scream and laugh, and a character's chosen voices
/// replace their species' sounds through sex changes, cloning, polymorph, validation and the database.
/// </summary>
[TestFixture]
[TestOf(typeof(EmoteVoiceSystem))]
public sealed class EmoteVoiceTest
{
    private const string ClassicScream = "WGScreamClassic";
    private const string ClownLaugh = "WGLaughClown";
    private const string SilentScream = "WGScreamNone";
    private const string MaleHuman = "MaleHuman";
    private const string FemaleHuman = "FemaleHuman";

    /// <summary>MobHuman with transferHumanoidAppearance (Polymorphs/polymorph.yml).</summary>
    private const string TestHumanMorph = "TestHumanMorph";

    [Test]
    public async Task CatalogueIsValidTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ProtoMan;
        var res = server.ResolveDependency<IResourceManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var voice in proto.EnumeratePrototypes<EmoteVoicePrototype>())
                {
                    Assert.That(voice.Emote == EmoteVoiceRules.Scream || voice.Emote == EmoteVoiceRules.Laugh,
                        $"{voice.ID} is for {voice.Emote}, which the creator has no picker for.");
                    Assert.That(loc.HasString(voice.Name), $"{voice.ID} has no string for its name {voice.Name}.");

                    var sounds = Sounds(voice).ToList();
                    if (sounds.Count == 0)
                    {
                        Assert.That(voice.Name.Id, Is.EqualTo("emote-voice-name-silent"),
                            $"{voice.ID} has no sound but is not named as the silent voice.");
                    }

                    foreach (var file in sounds.SelectMany(s => Files(s, proto)))
                    {
                        Assert.That(res.ContentFileExists(file), $"{voice.ID} plays {file}, which does not exist.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Each playable species' own scream and laugh, as it sounds for each of its sexes, is one of the voices.</summary>
    [Test]
    public async Task EverySpeciesSoundIsOfferedTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var res = server.ResolveDependency<IResourceManager>();
        var hashes = new Dictionary<ResPath, string>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart))
                {
                    foreach (var sex in species.Sexes)
                    {
                        foreach (var emote in new[] { EmoteVoiceRules.Scream, EmoteVoiceRules.Laugh })
                        {
                            if (!EmoteVoiceRules.TryGetEmoteSound(species.ID, sex, emote, null, proto, factory,
                                    out var own, out var ownParams))
                                continue;

                            var key = Key(own, ownParams, proto, res, hashes);
                            var offered = EmoteVoiceRules.VoicesFor(emote, proto)
                                .Any(v => v.GetSound(sex) is { } s && Key(s, v.Params, proto, res, hashes) == key);

                            Assert.That(offered, $"{species.ID} ({sex}) {emote} is not offered as a voice. Add an emoteVoice "
                                + $"playing {Describe(own)} at pitch {ownParams.Pitch}, volume {ownParams.Volume}.");
                        }
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VoicesReplaceSpeciesSoundsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ProtoMan;
        var map = await pair.CreateTestMap();
        var humanoid = entMan.System<SharedHumanoidAppearanceSystem>();

        var voiced = HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithSex(Sex.Male)
            .WithScreamVoice(ClassicScream)
            .WithLaughVoice(ClownLaugh);

        var male = proto.Index<EmoteSoundsPrototype>(MaleHuman);
        var female = proto.Index<EmoteSoundsPrototype>(FemaleHuman);
        var classic = proto.Index<EmoteVoicePrototype>(ClassicScream);
        var coughParams = male.Sounds["Cough"].Params;

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            humanoid.LoadProfile(mob, voiced);
        });

        await server.WaitAssertion(() =>
        {
            var sounds = entMan.GetComponent<VocalComponent>(mob).EmoteSounds!;
            Assert.Multiple(() =>
            {
                Assert.That(Describe(sounds.Sounds["Scream"]), Is.EqualTo(Describe(classic.Sound!)));
                Assert.That(sounds.Sounds["Scream"].Params, Is.EqualTo(classic.Params), "The voice plays with its own params.");
                Assert.That(Describe(sounds.Sounds["Laugh"]), Is.EqualTo("collection WGLaughsClown"));
                Assert.That(Describe(sounds.Sounds["Cough"]), Is.EqualTo(Describe(male.Sounds["Cough"])));
                Assert.That(sounds.Sounds["Cough"].Params, Is.EqualTo(male.GeneralParams),
                    "Emotes without a voice keep the species' params.");
                Assert.That(male.Sounds["Cough"].Params, Is.EqualTo(coughParams), "The species prototype was changed.");
            });
        });

        // VocalSystem reloads the species' sounds for the new sex; the voices stay on top.
        await server.WaitPost(() => humanoid.SetSex(mob, Sex.Female));
        await server.WaitAssertion(() =>
        {
            var sounds = entMan.GetComponent<VocalComponent>(mob).EmoteSounds!;
            Assert.That(Describe(sounds.Sounds["Scream"]), Is.EqualTo(Describe(classic.Sound!)));
            Assert.That(Describe(sounds.Sounds["Cough"]), Is.EqualTo(Describe(female.Sounds["Cough"])));
        });

        // A silent voice drops the emote's sound.
        await server.WaitPost(() => humanoid.LoadProfile(mob, voiced.WithSex(Sex.Female).WithScreamVoice(SilentScream)));
        await server.WaitAssertion(() =>
        {
            var sounds = entMan.GetComponent<VocalComponent>(mob).EmoteSounds;
            Assert.That(entMan.System<ChatSystem>().TryPlayEmoteSound(mob, sounds, EmoteVoiceRules.Scream), Is.False);
        });

        // A profile without voices gives the species' own set back.
        await server.WaitPost(() => humanoid.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithSex(Sex.Female)));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<EmoteVoiceComponent>(mob), Is.False);
            Assert.That(entMan.GetComponent<VocalComponent>(mob).EmoteSounds, Is.SameAs(female));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClonesAndPolymorphsKeepVoicesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ProtoMan;
        var map = await pair.CreateTestMap();
        var humanoid = entMan.System<SharedHumanoidAppearanceSystem>();
        var classic = Describe(proto.Index<EmoteVoicePrototype>(ClassicScream).Sound!);

        EntityUid source = default;
        EntityUid clone = default;
        EntityUid? child = null;
        await server.WaitPost(() =>
        {
            source = entMan.SpawnEntity("MobHuman", map.GridCoords);
            humanoid.LoadProfile(source, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithScreamVoice(ClassicScream));

            // CloningSystem's order: spawn the species prototype, copy the appearance, raise CloningEvent on the source.
            clone = entMan.SpawnEntity("MobHuman", map.GridCoords);
            humanoid.CloneAppearance(source, clone);
            var ev = new CloningEvent(source, clone);
            entMan.EventBus.RaiseLocalEvent(source, ref ev);

            child = entMan.System<PolymorphSystem>().PolymorphEntity(source, TestHumanMorph);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(child, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(Describe(entMan.GetComponent<VocalComponent>(clone).EmoteSounds!.Sounds["Scream"]), Is.EqualTo(classic));
                Assert.That(Describe(entMan.GetComponent<VocalComponent>(child!.Value).EmoteSounds!.Sounds["Scream"]), Is.EqualTo(classic));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>EnsureValid drops unknown voices and voices for the other emote, and the preview ignores the latter.</summary>
    [Test]
    public async Task ValidationTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var collection = server.ResolveDependency<IDependencyCollection>();
        var proto = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var session = server.PlayerMan.Sessions.First();

            // A laugh passed for the scream plays the species' own scream.
            Assert.That(EmoteVoiceRules.TryGetEmoteSound("Human", Sex.Male, EmoteVoiceRules.Scream, ClownLaugh, proto, factory,
                out var scream, out _), Is.True);
            Assert.That(Describe(scream!), Is.EqualTo(Describe(proto.Index<EmoteSoundsPrototype>(MaleHuman).Sounds["Scream"])));

            var kept = HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithScreamVoice(ClassicScream)
                .WithLaughVoice(ClownLaugh);
            kept.EnsureValid(session, collection);

            var dropped = HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithScreamVoice(ClownLaugh)
                .WithLaughVoice("WGNoSuchVoice");
            dropped.EnsureValid(session, collection);

            Assert.Multiple(() =>
            {
                Assert.That(kept.ScreamVoice?.Id, Is.EqualTo(ClassicScream));
                Assert.That(kept.LaughVoice?.Id, Is.EqualTo(ClownLaugh));
                Assert.That(dropped.ScreamVoice, Is.Null, "A laugh was kept as the scream.");
                Assert.That(dropped.LaughVoice, Is.Null, "An unknown voice was kept.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DatabaseRoundTripTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");

        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        var db = new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);

        var user = new NetUserId(Guid.NewGuid());
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithScreamVoice(ClassicScream)
            .WithLaughVoice(ClownLaugh);
        await db.InitPrefsAsync(user, profile);
        var plain = HumanoidCharacterProfile.DefaultWithSpecies("Human");
        await db.SaveCharacterSlotAsync(user, plain, 1);

        var prefs = await db.GetPlayerPreferencesAsync(user);
        var loaded = (HumanoidCharacterProfile) prefs!.Characters[0];
        var loadedPlain = (HumanoidCharacterProfile) prefs.Characters[1];
        Assert.Multiple(() =>
        {
            Assert.That(loaded.ScreamVoice?.Id, Is.EqualTo(ClassicScream));
            Assert.That(loaded.LaughVoice?.Id, Is.EqualTo(ClownLaugh));
            Assert.That(loadedPlain.ScreamVoice, Is.Null);
            Assert.That(loadedPlain.LaughVoice, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<SoundSpecifier> Sounds(EmoteVoicePrototype voice)
    {
        if (voice.Sound != null)
            yield return voice.Sound;

        foreach (var sound in voice.SexSounds.Values)
        {
            yield return sound;
        }
    }

    private static IEnumerable<ResPath> Files(SoundSpecifier sound, IPrototypeManager proto)
    {
        return sound switch
        {
            SoundPathSpecifier path => new[] { path.Path },
            SoundCollectionSpecifier { Collection: { } id } => proto.Index<SoundCollectionPrototype>(id).PickFiles,
            _ => Array.Empty<ResPath>(),
        };
    }

    private static string Describe(SoundSpecifier sound)
    {
        return sound switch
        {
            SoundPathSpecifier path => $"path {path.Path}",
            SoundCollectionSpecifier collection => $"collection {collection.Collection}",
            _ => sound.ToString() ?? string.Empty,
        };
    }

    /// <summary>What a sound plays: the contents of its files, however they are named, with its pitch and volume.</summary>
    private static string Key(SoundSpecifier sound, AudioParams audioParams, IPrototypeManager proto, IResourceManager res,
        Dictionary<ResPath, string> hashes)
    {
        var files = Files(sound, proto)
            .Select(file =>
            {
                if (!hashes.TryGetValue(file, out var hash))
                {
                    using var stream = res.ContentFileRead(file);
                    hashes[file] = hash = Convert.ToHexString(SHA256.HashData(stream));
                }

                return hash;
            })
            .Order();

        return $"{string.Join(",", files)}|{audioParams.Pitch:0.###}|{audioParams.Volume:0.###}";
    }
}
