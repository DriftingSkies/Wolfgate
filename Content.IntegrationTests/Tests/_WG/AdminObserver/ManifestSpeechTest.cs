#nullable enable
using System.Linq;
using Content.Client.Chat.Managers;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server._WG.AdminObserver;
using Content.Server.Chat.Systems;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Chat;
using Content.Shared.Inventory;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>A manifested observer speaks in character with its staff tag; hidden, it is back in dead chat.</summary>
[TestFixture]
[TestOf(typeof(WGManifestSystem))]
public sealed class ManifestSpeechTest
{
    private const int MaxWaitTicks = 120;

    /// <summary>The living player beside a manifested observer hears it say, whisper and radio, tagged as staff.</summary>
    [Test]
    public async Task LivingPlayerHearsAManifestedObserver()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var manifest = server.System<WGManifestSystem>();
        var chatSys = server.System<ChatSystem>();
        var inventory = server.System<InventorySystem>();
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var map = await pair.CreateTestMap();

        var human = await AdminKitFixture.Living(pair, map.GridCoords);
        var observer = await AdminKitFixture.Unmanned(pair, map.GridCoords);

        await server.WaitAssertion(() =>
        {
            var headset = entMan.SpawnEntity("ClothingHeadsetGrey", map.GridCoords);
            Assert.That(inventory.TryEquip(human, headset, "ears", silent: true, force: true));
        });

        // Hidden: nothing the observer says reaches the living.
        await server.WaitPost(() =>
            chatSys.TrySendInGameICMessage(observer, "words from beyond", InGameICChatType.Speak, ChatTransmitRange.Normal));
        await pair.RunTicksSync(20);
        Assert.That(chat.History.Any(h => h.Msg.Channel == ChatChannel.Local
                                          && h.Msg.Message.Contains("words from beyond", StringComparison.OrdinalIgnoreCase)),
            Is.False, "A hidden observer must not speak in character.");

        await server.WaitPost(() =>
        {
            manifest.Manifest(observer, new WGAvatarPreset { DisplayName = "Inspector", EntryStyle = WGEntryStyle.Silent }, null);
            chatSys.TrySendInGameICMessage(observer, "hello there", InGameICChatType.Speak, ChatTransmitRange.Normal);
        });
        var said = await WaitFor(pair, chat, ChatChannel.Local, "hello there", "a living player to hear a manifested observer");
        Assert.Multiple(() =>
        {
            Assert.That(said.WrappedMessage, Does.Contain("Inspector"), "The avatar's name should be on the line.");
            Assert.That(said.WrappedMessage, Does.Contain("Staff"), "The staff tag should be on the line.");
        });

        await server.WaitPost(() =>
            chatSys.TrySendInGameICMessage(observer, "a quiet word", InGameICChatType.Whisper, ChatTransmitRange.Normal));
        await WaitFor(pair, chat, ChatChannel.Whisper, "a quiet word", "a living player to hear a manifested observer whisper");

        await server.WaitPost(() =>
            chatSys.TrySendInGameICMessage(observer, ";radio check", InGameICChatType.Speak, ChatTransmitRange.Normal));
        var radio = await WaitFor(pair, chat, ChatChannel.Radio, "radio check", "a headset to pick up a manifested observer");
        Assert.That(radio.WrappedMessage, Does.Contain("Staff"), "The staff tag should be on the radio line too.");

        await pair.CleanReturnAsync();
    }

    /// <summary>The admin's own chat box: Local is Local while manifested, Dead still works, and both fall back when hidden.</summary>
    [Test]
    public async Task ObserverChannelsFollowManifesting()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var manifest = server.System<WGManifestSystem>();
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();
        var map = await pair.CreateTestMap();

        var session = await AdminKitFixture.Admin(pair);
        await AdminKitFixture.Enter(pair, session, map.GridCoords);
        Assert.That(chat.MapLocalIfGhost(ChatSelectChannel.Local), Is.EqualTo(ChatSelectChannel.Dead),
            "A hidden observer's Local is dead chat.");

        await server.WaitAssertion(() => Assert.That(manifest.TryManifest(session, null, out _)));
        await pair.RunTicksSync(10);
        Assert.That(chat.MapLocalIfGhost(ChatSelectChannel.Local), Is.EqualTo(ChatSelectChannel.Local),
            "A manifested observer's Local stays Local.");

        await pair.Client.WaitPost(() => clientChat.SendMessage("spoken aloud", ChatSelectChannel.Local));
        var local = await WaitFor(pair, chat, ChatChannel.Local, "spoken aloud", "the admin's Local message to come back as Local");
        Assert.That(local.WrappedMessage, Does.Contain("["), "The admin's own line carries the staff tag.");

        await pair.Client.WaitPost(() => clientChat.SendMessage("among the dead", ChatSelectChannel.Dead));
        await WaitFor(pair, chat, ChatChannel.Dead, "among the dead", "dead chat to still work while manifested");

        await pair.Client.WaitPost(() => clientChat.SendMessage("waves a hand", ChatSelectChannel.Emotes));
        await WaitFor(pair, chat, ChatChannel.Emotes, "waves a hand", "a manifested observer to emote");

        await server.WaitAssertion(() => Assert.That(manifest.TryUnmanifest(session, out _)));
        await pair.RunTicksSync(10);
        Assert.That(chat.MapLocalIfGhost(ChatSelectChannel.Local), Is.EqualTo(ChatSelectChannel.Dead),
            "Hiding sends Local back to dead chat.");

        await pair.Client.WaitPost(() => clientChat.SendMessage("back in the grave", ChatSelectChannel.Local));
        await WaitFor(pair, chat, ChatChannel.Dead, "back in the grave", "a hidden observer's Local message to go to dead chat");

        await pair.CleanReturnAsync();
    }

    private static async Task<ChatMessage> WaitFor(TestPair pair, ChatUIController chat, ChatChannel channel, string text, string what)
    {
        ChatMessage? Find() => chat.History
            .Select(h => h.Msg)
            .FirstOrDefault(m => m.Channel == channel && m.Message.Contains(text, StringComparison.OrdinalIgnoreCase));

        for (var i = 0; i < MaxWaitTicks && Find() == null; i++)
        {
            await pair.RunTicksSync(1);
        }

        var found = Find();
        Assert.That(found, Is.Not.Null, $"Timed out waiting for {what}. Got: "
            + string.Join(" | ", chat.History.TakeLast(6).Select(h => $"{h.Msg.Channel}: {h.Msg.Message}")));
        return found!;
    }
}
