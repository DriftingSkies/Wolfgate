using System.Numerics;
using Content.Client._WF.Modsuits;
using Robust.Client.ResourceManagement;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Modsuits;

/// <summary>Ensures the MODsuit window XAML loads and lays out without runtime errors.</summary>
[TestFixture]
public sealed class ModSuitWindowTest
{
    [Test]
    public async Task WindowLoads()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Client.WaitAssertion(() =>
        {
            var cache = pair.Client.ResolveDependency<IResourceCache>();
            var icons = cache.GetResource<RSIResource>("/Textures/_WF/Modsuits/icons.rsi").RSI;
            foreach (var state in new[]
                     {
                         "power-off", "lock", "lock-open", "wifi", "arrow-up", "arrow-down", "eject",
                         "plug", "lightbulb", "bolt", "save", "cog", "thumbtack",
                         "check-square-o", "square-o", "exclamation-triangle", "paint-brush",
                     })
            {
                Assert.That(icons.TryGetState(state, out _), Is.True, $"missing MODsuit UI icon '{state}'");
            }

            using var window = new ModSuitWindow();
            Assert.Multiple(() =>
            {
                Assert.That(window.FindControl<ModSuitIconButton>("ActivateButton").Icon.Texture, Is.Not.Null);
                Assert.That(window.FindControl<ModSuitIconButton>("IdLockButton").Icon.Texture, Is.Not.Null);
                Assert.That(window.FindControl<ModSuitIconButton>("ModLinkButton").Icon.Texture, Is.Not.Null);
                Assert.That(window.FindControl<ModSuitIconButton>("ToggleAllButton").Icon.Texture, Is.Not.Null);
            });
            window.Measure(new Vector2(600, 600));
            window.Arrange(new UIBox2(0, 0, 600, 600));
        });

        await pair.CleanReturnAsync();
    }
}
