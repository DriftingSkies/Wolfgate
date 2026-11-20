#nullable enable
using System.Linq;
using Content.Server._WG.AdminObserver;
using Content.Server.Preferences.Managers;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Chat;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Speech;
using Content.Shared.Strip.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>What a character avatar wears and how it sounds.</summary>
[TestFixture]
[TestOf(typeof(WGAvatarBodySystem))]
public sealed class AvatarOutfitTest
{
    private const string Gear = "CaptainGear";
    private const string Hat = "ClothingHeadHatWelding";

    [Test]
    public async Task GearAndSavedLayoutBothDressTheBody()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var bodies = server.System<WGAvatarBodySystem>();
        var kit = server.System<WGAdminObserverKitSystem>();
        var store = server.System<WGAdminObserverStore>();
        var inventory = server.System<InventorySystem>();
        var ui = server.System<SharedUserInterfaceSystem>();
        var map = await pair.CreateTestMap();
        var session = await AdminKitFixture.Admin(pair);
        var observer = await AdminKitFixture.Enter(pair, session, map.GridCoords);

        await server.WaitAssertion(() =>
        {
            var ent = (observer, entMan.GetComponent<WGAdminObserverComponent>(observer));
            store.Change(session, data => data.Presets.Add(new WGAvatarPreset { Name = "captain", Kind = WGAvatarKind.Character, Outfit = Gear }));
            WGAvatarPreset Preset() => store.Get(session)!.Presets.Single(p => p.Name == "captain");

            // A starting gear dresses the body.
            var body = bodies.Ensure(ent, session, Preset())!.Value;
            Assert.That(inventory.TryGetSlotEntity(body, "gloves", out var gloves));
            Assert.That(entMan.GetComponent<MetaDataComponent>(gloves!.Value).EntityPrototype?.ID, Is.EqualTo("ClothingHandsGlovesCaptain"));

            // The admin can open its strip window to dress it by hand.
            Assert.That(bodies.OpenDresser(session, "captain"));
            Assert.That(ui.IsUiOpen(body, StrippingUiKey.Key, observer));

            // Dressed by hand and saved, the outfit is the preset's from then on, and the body is kept as it is.
            var hat = entMan.SpawnEntity(Hat, map.GridCoords);
            Assert.That(inventory.TryEquip(body, hat, "head", silent: true, force: true));
            Assert.That(bodies.SaveOutfit(session, "captain"));

            var saved = Preset().OutfitLayout;
            Assert.That(saved, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(saved!.Slots["head"].Prototype, Is.EqualTo(Hat));
                Assert.That(saved.Slots["gloves"].Prototype, Is.EqualTo("ClothingHandsGlovesCaptain"));
                Assert.That(bodies.Ensure(ent, session, Preset()), Is.EqualTo(body), "Saving does not rebuild the body.");
            });

            // Built again from the saved layout, it wears the same.
            entMan.DeleteEntity(body);
            var rebuilt = bodies.Ensure(ent, session, Preset())!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(rebuilt, Is.Not.EqualTo(body));
                Assert.That(AdminKitFixture.Text(kit.Snapshot(rebuilt, true)), Is.EqualTo(AdminKitFixture.Text(saved)));
            });

            // A preset that is no character has no body to dress.
            Assert.That(bodies.OpenDresser(session, store.Get(session)!.Presets[0].Name), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpeechUsesTheCharactersNameAndVoice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var manifest = server.System<WGManifestSystem>();
        var store = server.System<WGAdminObserverStore>();
        var map = await pair.CreateTestMap();
        var session = await AdminKitFixture.Admin(pair);
        var observer = await AdminKitFixture.Enter(pair, session, map.GridCoords);

        await server.WaitAssertion(() =>
        {
            var profile = (HumanoidCharacterProfile) server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(session.UserId).SelectedCharacter;
            var speech = entMan.GetComponent<SpeechComponent>(observer);
            var ownSounds = speech.SpeechSounds;
            var ownVerb = speech.SpeechVerb;

            store.Change(session, data => data.Presets.Add(new WGAvatarPreset
            {
                Name = "me",
                Kind = WGAvatarKind.Character,
                Outfit = "PassengerGear",
                EntryStyle = WGEntryStyle.Silent,
            }));
            Assert.That(manifest.TryManifest(session, "me", out var message), message);

            var body = entMan.GetComponent<WGManifestedComponent>(observer).Body!.Value;
            var voice = entMan.GetComponent<SpeechComponent>(body);
            var name = new TransformSpeakerNameEvent(observer, entMan.GetComponent<MetaDataComponent>(observer).EntityName);
            entMan.EventBus.RaiseLocalEvent(observer, name);
            var label = entMan.GetComponent<WGStaffMarkerComponent>(observer).Label;

            Assert.Multiple(() =>
            {
                Assert.That(name.VoiceName, Is.EqualTo($"[{label}] {profile.Name}"), "It speaks under the character's name, tagged as staff.");
                Assert.That(speech.SpeechSounds, Is.EqualTo(voice.SpeechSounds));
                Assert.That(speech.SpeechVerb, Is.EqualTo(voice.SpeechVerb));
                Assert.That(voice.SpeechSounds, Is.Not.Null, "A humanoid has speech sounds to borrow.");
            });

            Assert.That(manifest.TryUnmanifest(session, out _));
            Assert.Multiple(() =>
            {
                Assert.That(speech.SpeechSounds, Is.EqualTo(ownSounds));
                Assert.That(speech.SpeechVerb, Is.EqualTo(ownVerb));
            });
        });

        await pair.CleanReturnAsync();
    }
}
