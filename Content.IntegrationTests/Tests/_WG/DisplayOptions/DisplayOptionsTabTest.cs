#nullable enable
using Content.Client._Scp.Shaders.Bloom;
using Content.Client._WG.DisplayOptions;
using Content.Client.Options.UI;
using Content.Client.Options.UI.Tabs;
using Content.IntegrationTests.Tests._WG.EngineCompat;
using Content.IntegrationTests.Tests._WG.Options;
using Content.Shared._Scp.ScpCCVars;
using Content.Shared._WG.ChatTextSize;
using Content.Shared._WG.DarkFlash;
using Content.Shared._WG.HideTyping;
using Content.Shared._WG.Notifications;
using Content.Shared._WG.SpeechBubbles;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WG.DisplayOptions;

/// <summary>
/// The new Graphics, Misc and Accessibility controls are bound to their cvars, and Apply writes them.
/// </summary>
[TestFixture]
[TestOf(typeof(GraphicsTab))]
public sealed class DisplayOptionsTabTest
{
    [Test]
    public async Task NewControlsApply()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var config = client.ResolveDependency<IConfigurationManager>();

        await client.WaitAssertion(() =>
        {
            OptionsTabTestHelper.AssertCheckBoxesApply<GraphicsTab>(config, "GraphicsTab",
                ("ParallaxEnabledCheckBox", CCVars.ParallaxEnabled),
                ("BloomCheckBox", ScpCCVars.LightBloomEnable),
                ("GrainCheckBox", ScpCCVars.GrainToggleOverlay));

            OptionsTabTestHelper.AssertCheckBoxesApply<MiscTab>(config, "MiscTab",
                ("OutlineCheckBox", CCVars.OutlineEnabled),
                ("ShowMyTypingCheckBox", HideTypingCVars.ShowMyTyping),
                ("SpeechBubblesCheckBox", SpeechBubbleCVars.Enabled),
                ("SpeechBubblesNonMobsCheckBox", SpeechBubbleCVars.NonMobs),
                ("SpeechBubblesGhostsCheckBox", SpeechBubbleCVars.Ghosts),
                ("SpeechBubblesEmotesCheckBox", SpeechBubbleCVars.Emotes),
                ("FlashRoundStartCheckBox", NotificationCVars.FlashRoundStart),
                ("FlashAHelpCheckBox", NotificationCVars.FlashAHelp),
                ("FlashHighlightCheckBox", NotificationCVars.FlashHighlight),
                ("SoundHighlightCheckBox", NotificationCVars.SoundHighlight),
                ("FlashGhostRoleCheckBox", NotificationCVars.FlashGhostRole),
                ("SoundGhostRoleCheckBox", NotificationCVars.SoundGhostRole));

            OptionsTabTestHelper.AssertCheckBoxesApply<AccessibilityTab>(config, "AccessibilityTab",
                ("DarkFlashCheckBox", DarkFlashCVars.Enabled));

            // The sliders and the FPS drop-down exist and show the current values.
            var menu = new OptionsMenu();
            try
            {
                var graphics = menu.FindControl<GraphicsTab>("GraphicsTab");
                var fps = graphics.FindControl<OptionDropDown>("DropDownMaxFps");
                Assert.That(fps.Button.ItemCount, Is.EqualTo(9), "Unlimited plus eight caps.");

                var misc = menu.FindControl<MiscTab>("MiscTab");
                foreach (var (name, cvar) in new[]
                         {
                             ("SpeechBubbleMaxLengthSlider", SpeechBubbleCVars.MaxLength),
                             ("ChatTextSizeSlider", ChatTextSizeCVars.Size),
                             ("ChatLineSpacingSlider", ChatTextSizeCVars.LineSpacing),
                         })
                {
                    var slider = misc.FindControl<OptionSlider>(name);
                    Assert.That((int) slider.Slider.Value, Is.EqualTo(config.GetCVar(cvar)), name);
                }
            }
            finally
            {
                menu.Teardown();
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The bloom toggle puts up the Wolfgate bloom overlay with the chosen strength, and never the SCP lamp overlays.
    /// </summary>
    [Test]
    public async Task BloomToggleUsesWolfgateOverlay()
    {
        // Connected, so the client's entity systems, WGBloomSystem among them, are running.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var config = client.ResolveDependency<IConfigurationManager>();
        var overlays = client.ResolveDependency<IOverlayManager>();

        try
        {
            await client.WaitPost(() =>
            {
                config.SetCVar(ScpCCVars.LightBloomStrength, 0.5f);
                config.SetCVar(ScpCCVars.LightBloomEnable, true);
            });
            await client.WaitAssertion(() =>
            {
                Assert.That(overlays.TryGetOverlay<WGBloomOverlay>(out var bloom), Is.True);
                Assert.That(bloom!.Strength, Is.EqualTo(0.5f));
                Assert.That(overlays.HasOverlay<PointLightingOverlay>(), Is.False);
                Assert.That(overlays.HasOverlay<ConeLightingOverlay>(), Is.False);
            });

            await client.WaitPost(() => config.SetCVar(ScpCCVars.LightBloomEnable, false));
            await client.WaitAssertion(() => Assert.That(overlays.HasOverlay<WGBloomOverlay>(), Is.False));
        }
        finally
        {
            await client.WaitPost(() =>
            {
                config.SetCVar(ScpCCVars.LightBloomEnable, ScpCCVars.LightBloomEnable.DefaultValue);
                config.SetCVar(ScpCCVars.LightBloomStrength, ScpCCVars.LightBloomStrength.DefaultValue);
            });
        }

        await pair.CleanReturnAsync();
    }
}
