#nullable enable
using System.Reflection;
using Content.Client.Chat.UI;
using Content.Client.UserInterface.Systems.Chat;
using Content.Shared._WG.SpeechBubbles;
using Content.Shared.Chat;
using Content.Shared.Mobs.Components;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.SpeechBubbles;

/// <summary>Each speech bubble setting hides exactly its own kind of bubble.</summary>
[TestFixture]
[TestOf(typeof(ChatUIController))]
public sealed class SpeechBubbleFilterTest
{
    private static readonly MethodInfo Show = typeof(ChatUIController)
        .GetMethod("WgShowSpeechBubble", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Test]
    public async Task SettingsFilterBubbles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var cfg = client.ResolveDependency<IConfigurationManager>();
        var ents = client.ResolveDependency<IEntityManager>();
        var chat = client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var cvars = new[] { SpeechBubbleCVars.Enabled, SpeechBubbleCVars.NonMobs, SpeechBubbleCVars.Ghosts, SpeechBubbleCVars.Emotes };

        var map = await pair.CreateTestMap();
        EntityUid serverMob = default;
        await pair.Server.WaitPost(() => serverMob = pair.Server.EntMan.SpawnEntity("MobHuman", map.GridCoords));
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            // The filter only checks for MobStateComponent: a human is a mob, the grid is not.
            var mob = pair.ToClientUid(serverMob);
            var radio = map.CGridUid;
            Assert.That(ents.HasComponent<MobStateComponent>(mob), Is.True);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(Bubble(mob, ChatChannel.Local, SpeechBubble.SpeechType.Say), Is.True, "Defaults show everything.");
                    Assert.That(Bubble(radio, ChatChannel.Local, SpeechBubble.SpeechType.Say), Is.True);

                    cfg.SetCVar(SpeechBubbleCVars.NonMobs, false);
                    Assert.That(Bubble(radio, ChatChannel.Local, SpeechBubble.SpeechType.Say), Is.False, "Objects off hides the radio.");
                    Assert.That(Bubble(mob, ChatChannel.Local, SpeechBubble.SpeechType.Say), Is.True, "Objects off keeps mobs.");
                    cfg.SetCVar(SpeechBubbleCVars.NonMobs, true);

                    cfg.SetCVar(SpeechBubbleCVars.Emotes, false);
                    Assert.That(Bubble(mob, ChatChannel.Emotes, SpeechBubble.SpeechType.Emote), Is.False);
                    Assert.That(Bubble(mob, ChatChannel.Local, SpeechBubble.SpeechType.Say), Is.True);
                    cfg.SetCVar(SpeechBubbleCVars.Emotes, true);

                    cfg.SetCVar(SpeechBubbleCVars.Ghosts, false);
                    Assert.That(Bubble(mob, ChatChannel.Dead, SpeechBubble.SpeechType.Say), Is.False);
                    cfg.SetCVar(SpeechBubbleCVars.Ghosts, true);

                    cfg.SetCVar(SpeechBubbleCVars.Enabled, false);
                    Assert.That(Bubble(mob, ChatChannel.Local, SpeechBubble.SpeechType.Say), Is.False, "Off hides all bubbles.");
                    Assert.That(Bubble(mob, ChatChannel.LOOC, SpeechBubble.SpeechType.Looc), Is.True, "LOOC keeps its own toggle.");
                });
            }
            finally
            {
                foreach (var cvar in cvars)
                {
                    cfg.SetCVar(cvar, true);
                }
            }
        });

        await pair.CleanReturnAsync();

        bool Bubble(EntityUid sender, ChatChannel channel, SpeechBubble.SpeechType type)
        {
            var msg = new ChatMessage(channel, "hi", "hi", ents.GetNetEntity(sender), null);
            return (bool) Show.Invoke(chat, new object[] { sender, msg, type })!;
        }
    }
}
