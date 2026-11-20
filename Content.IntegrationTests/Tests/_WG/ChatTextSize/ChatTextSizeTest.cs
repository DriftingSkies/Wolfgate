#nullable enable
using System.Reflection;
using Content.Client._WG.ChatTextSize;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared._WG.ChatTextSize;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WG.ChatTextSize;

/// <summary>
/// A chat text size or line spacing gives the log a font of that size with that much extra line height, and the
/// defaults leave the theme alone.
/// </summary>
[TestFixture]
[TestOf(typeof(ChatBox))]
public sealed class ChatTextSizeTest
{
    private static readonly MethodInfo Update = typeof(ChatBox)
        .GetMethod("WgUpdateTextSize", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Test]
    public async Task SizeAndSpacingApply()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var cfg = client.ResolveDependency<IConfigurationManager>();
        var ui = client.ResolveDependency<IUserInterfaceManager>();

        await client.WaitAssertion(() =>
        {
            var chat = new ChatBox();
            var log = chat.FindControl<OutputPanel>("Contents");
            ui.PopupRoot.AddChild(chat);
            try
            {
                Update.Invoke(chat, null);
                Assert.That(log.Stylesheet, Is.Null, "The defaults leave the theme's font alone.");

                cfg.SetCVar(ChatTextSizeCVars.Size, 16);
                cfg.SetCVar(ChatTextSizeCVars.LineSpacing, 4);
                Update.Invoke(chat, null);
                Assert.That(log.Stylesheet, Is.Not.Null);
                log.ForceRunStyleUpdate();
                Assert.That(log.TryGetStyleProperty<Font>("font", out var font), Is.True);
                var spaced = font as WGSpacedFont;
                Assert.That(spaced, Is.Not.Null, "Line spacing wraps the font.");
                Assert.That(spaced!.GetLineHeight(1f), Is.EqualTo(spaced.Inner.GetLineHeight(1f) + 4));

                cfg.SetCVar(ChatTextSizeCVars.Size, 0);
                cfg.SetCVar(ChatTextSizeCVars.LineSpacing, 0);
                Update.Invoke(chat, null);
                Assert.That(log.Stylesheet, Is.Null, "Going back to the defaults removes the override.");
            }
            finally
            {
                cfg.SetCVar(ChatTextSizeCVars.Size, 0);
                cfg.SetCVar(ChatTextSizeCVars.LineSpacing, 0);
                chat.Orphan();
                chat.Dispose();
            }
        });

        await pair.CleanReturnAsync();
    }
}
