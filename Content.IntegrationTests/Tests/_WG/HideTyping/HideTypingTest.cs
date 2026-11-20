#nullable enable
using Content.Client.Chat.TypingIndicator;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.HideTyping;
using Content.Shared.Chat.TypingIndicator;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.HideTyping;

/// <summary>With <see cref="HideTypingCVars.ShowMyTyping"/> off, typing never shows; turning it off clears it.</summary>
[TestOf(typeof(TypingIndicatorSystem))]
public sealed class HideTypingTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task HiddenTypingSendsNone()
    {
        var cfg = Client.ResolveDependency<IConfigurationManager>();
        var typing = CEntMan.System<TypingIndicatorSystem>();

        try
        {
            await Client.WaitPost(() =>
            {
                typing.ClientChangedChatFocus(true);
                typing.ClientChangedChatText();
            });
            await RunTicks(5);
            Assert.That(await ServerState(), Is.EqualTo(TypingIndicatorState.Typing), "Typing should show by default.");

            await Client.WaitPost(() => cfg.SetCVar(HideTypingCVars.ShowMyTyping, false));
            await RunTicks(5);
            Assert.That(await ServerState(), Is.EqualTo(TypingIndicatorState.None), "Turning it off should clear the indicator.");

            await Client.WaitPost(() => typing.ClientChangedChatText());
            await RunTicks(5);
            Assert.That(await ServerState(), Is.EqualTo(TypingIndicatorState.None), "Typing while hidden shouldn't show.");
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                cfg.SetCVar(HideTypingCVars.ShowMyTyping, true);
                typing.ClientChangedChatFocus(false);
            });
            await RunTicks(5);
        }
    }

    private async Task<TypingIndicatorState> ServerState()
    {
        var state = TypingIndicatorState.None;
        await Server.WaitPost(() => SEntMan.System<SharedAppearanceSystem>().TryGetData(
            SPlayer, TypingIndicatorVisuals.State, out state));
        return state;
    }
}
