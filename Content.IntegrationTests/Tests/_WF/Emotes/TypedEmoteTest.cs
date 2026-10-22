#nullable enable
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Components.Localization;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.Emotes;

/// <summary>
/// Emotes name a player without "The", a typed trigger word becomes that emote's message, and "..." can be said.
/// </summary>
[TestFixture]
[TestOf(typeof(ChatSystem))]
public sealed class TypedEmoteTest
{
    [Test]
    public async Task MaskedPlayerKeepsProperName()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var inventory = entMan.System<InventorySystem>();
        var map = await pair.CreateTestMap();

        var human = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<MetaDataSystem>().SetEntityName(human, "David");
            var mask = entMan.SpawnEntity("ClothingMaskGas", map.GridCoords);
            Assert.That(inventory.TryEquip(human, mask, "mask", silent: true, force: true));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GrammarComponent>(human).ProperNoun, Is.True);
            Assert.That(entMan.GetComponent<GrammarComponent>(Identity.Entity(human, entMan)).ProperNoun, Is.False);
            Assert.That(inventory.TryUnequip(human, "mask", silent: true, force: true));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var ent = Identity.Entity(human, entMan);
            Assert.That(entMan.GetComponent<MetaDataComponent>(ent).EntityName, Is.EqualTo("David"));
            Assert.That(entMan.GetComponent<GrammarComponent>(ent).ProperNoun, Is.True);
            var wrapped = loc.GetString("chat-manager-entity-me-wrap-message",
                ("entityName", "David"), ("entity", ent), ("message", "looks at you."));
            Assert.That(wrapped, Does.StartWith("[italic]David looks"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TriggerWordBecomesEmoteMessage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var chat = entMan.System<ChatSystem>();
        var mindSys = entMan.System<SharedMindSystem>();
        var history = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var map = await pair.CreateTestMap();
        var salutes = loc.GetString("chat-emote-msg-salute");

        var human = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<MetaDataSystem>().SetEntityName(human, "David");
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, human);

            Assert.That(chat.ResolveTypedEmote(human, "salute"), Is.EqualTo(salutes));
            Assert.That(chat.ResolveTypedEmote(human, " Salute. "), Is.EqualTo(salutes));
            Assert.That(chat.ResolveTypedEmote(human, "salute!"), Is.EqualTo(salutes.TrimEnd('.') + "!"));
            Assert.That(chat.ResolveTypedEmote(human, "salute?!"), Is.EqualTo(salutes.TrimEnd('.') + "?!"));
            Assert.That(chat.ResolveTypedEmote(human, "salutes the captain"), Is.EqualTo("salutes the captain"));
            Assert.That(chat.ResolveTypedEmote(human, "laugh"), Is.EqualTo(loc.GetString("chat-emote-msg-laugh")));
            // A synonym trigger keeps the word typed rather than turning into another verb.
            Assert.That(chat.ResolveTypedEmote(human, "giggles"), Is.EqualTo("giggles"));
            Assert.That(chat.ResolveTypedEmote(human, "chuckles!"), Is.EqualTo("chuckles!"));
        });
        await pair.RunTicksSync(5);

        // The typed word goes through the real emote path and arrives as the emote's message.
        await server.WaitPost(() =>
            chat.TrySendInGameICMessage(human, "salute", InGameICChatType.Emote, hideChat: false, player: session));

        bool Arrived() => history.History.Any(h =>
            h.Msg.Channel == ChatChannel.Emotes && h.Msg.WrappedMessage.Contains($"David {salutes}"));
        for (var i = 0; i < 120 && !Arrived(); i++)
            await pair.RunTicksSync(1);
        Assert.That(Arrived(), Is.True, "The typed *salute did not arrive as the salute emote's message.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EllipsisIsNotARadioKey()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var chat = entMan.System<ChatSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(chat.TryProccessRadioMessage(human, "...", out _, out _, quiet: true), Is.False);
            Assert.That(chat.TryProccessRadioMessage(human, "...what?", out _, out _, quiet: true), Is.False);
            Assert.That(chat.TryProccessRadioMessage(human, ".h hello", out _, out _, quiet: true), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
