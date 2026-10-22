#nullable enable
using System.Linq;
using Content.Server._WF.EmoteVoices;
using Content.Shared._WF.EmoteVoices;
using Content.Shared.Cloning;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Speech;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.EmoteVoices;

/// <summary>
/// The message sound catalogue is sound and offers every species' own, and a character's pick replaces their
/// species' speech sounds until a profile without one gives them back.
/// </summary>
[TestFixture]
[TestOf(typeof(EmoteVoiceSystem))]
public sealed class MessageSoundTest
{
    private const string Rodentia = "WFMessageRodentia";
    private const string Silent = "WFMessageSilent";

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
                foreach (var sound in proto.EnumeratePrototypes<MessageSoundPrototype>())
                {
                    Assert.That(loc.HasString(sound.Name), $"{sound.ID} has no string for its name {sound.Name}.");
                    if (sound.Sounds is not { } id)
                    {
                        Assert.That(sound.Name.Id, Is.EqualTo("message-sound-name-silent"),
                            $"{sound.ID} has no sounds but is not named as the silent one.");
                        continue;
                    }

                    var speech = proto.Index(id);
                    foreach (var specifier in new[] { speech.SaySound, speech.AskSound, speech.ExclaimSound })
                    {
                        if (specifier is SoundPathSpecifier path)
                            Assert.That(res.ContentFileExists(path.Path), $"{sound.ID} plays {path.Path}, which does not exist.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Each playable species' own speech sounds are one of the picks.</summary>
    [Test]
    public async Task EverySpeciesSoundIsOfferedTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var offered = proto.EnumeratePrototypes<MessageSoundPrototype>().Select(s => s.Sounds).ToHashSet();
            Assert.Multiple(() =>
            {
                foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart))
                {
                    var own = MessageSoundRules.SpeciesSounds(species.ID, proto, factory);
                    Assert.That(offered, Does.Contain(own),
                        $"{species.ID}'s speech sounds {own} are not offered. Add a wfMessageSound for them.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PickReplacesSpeciesSoundsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var humanoid = entMan.System<SharedHumanoidAppearanceSystem>();
        var human = HumanoidCharacterProfile.DefaultWithSpecies("Human");

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            humanoid.LoadProfile(mob, human.WithMessageSound(Rodentia));
        });
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<SpeechComponent>(mob).SpeechSounds?.Id, Is.EqualTo("Squeak")));

        await server.WaitPost(() => humanoid.LoadProfile(mob, human.WithMessageSound(Silent)));
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<SpeechComponent>(mob).SpeechSounds, Is.Null));

        // A moth without a pick chatters with its species' own sounds.
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobMoth", map.GridCoords);
            humanoid.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Moth").WithMessageSound(Rodentia));
            humanoid.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Moth"));
        });
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<SpeechComponent>(mob).SpeechSounds?.Id, Is.EqualTo("WFMoth")));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CloneKeepsPickTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var humanoid = entMan.System<SharedHumanoidAppearanceSystem>();

        EntityUid clone = default;
        await server.WaitPost(() =>
        {
            var source = entMan.SpawnEntity("MobHuman", map.GridCoords);
            humanoid.LoadProfile(source, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithMessageSound(Rodentia));

            // CloningSystem's order: spawn the species prototype, copy the appearance, raise CloningEvent on the source.
            clone = entMan.SpawnEntity("MobHuman", map.GridCoords);
            humanoid.CloneAppearance(source, clone);
            var ev = new CloningEvent(source, clone);
            entMan.EventBus.RaiseLocalEvent(source, ref ev);
        });
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<SpeechComponent>(clone).SpeechSounds?.Id, Is.EqualTo("Squeak")));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnknownPickIsDroppedTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ProtoMan;

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(MessageSoundRules.Sanitize("WFMessageNoSuchSound", proto), Is.Null);
            Assert.That(MessageSoundRules.Sanitize(Rodentia, proto)?.Id, Is.EqualTo(Rodentia));
        });

        await pair.CleanReturnAsync();
    }
}
