#nullable enable
using System.Reflection;
using Content.Client.Flash;
using Content.Shared._WG.DarkFlash;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WG.DarkFlash;

/// <summary>Dark flash swaps the flashed effect for the still shader, and back.</summary>
[TestFixture]
[TestOf(typeof(FlashOverlay))]
public sealed class DarkFlashShaderTest
{
    [Test]
    public async Task DarkFlashUsesStillShader()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var cfg = client.ResolveDependency<IConfigurationManager>();
        var pick = typeof(FlashOverlay).GetMethod("WgFlashShader", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var usual = typeof(FlashOverlay).GetField("_shader", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await client.WaitAssertion(() =>
        {
            // The overlay is built by FlashSystem; it can't be constructed on its own.
            var overlay = (FlashOverlay) typeof(FlashSystem)
                .GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(client.System<FlashSystem>())!;
            try
            {
                Assert.That(pick.Invoke(overlay, null), Is.SameAs(usual.GetValue(overlay)), "Off uses the usual shader.");

                cfg.SetCVar(DarkFlashCVars.Enabled, true);
                var still = (ShaderInstance) pick.Invoke(overlay, null)!;
                Assert.That(still, Is.Not.SameAs(usual.GetValue(overlay)), "On uses the still shader.");
                Assert.That(pick.Invoke(overlay, null), Is.SameAs(still), "The still shader is made once.");
            }
            finally
            {
                cfg.SetCVar(DarkFlashCVars.Enabled, false);
            }
        });

        await pair.CleanReturnAsync();
    }
}
