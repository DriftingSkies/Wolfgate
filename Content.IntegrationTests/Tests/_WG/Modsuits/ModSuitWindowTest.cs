using System.Linq;
using System.Numerics;
using Content.Client._WG.Modsuits;
using Content.Shared._WG.Modsuits.Components;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Modsuits;

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
            var icons = cache.GetResource<RSIResource>("/Textures/_WG/Modsuits/icons.rsi").RSI;
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

    /// <summary>Long module names must not push the power columns out of line or out of the window.</summary>
    [Test]
    public async Task ModulePowerColumnsAlignWithHeader()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;

        var map = await pair.CreateTestMap();
        var netSuit = NetEntity.Invalid;
        await server.WaitPost(() =>
        {
            var suit = server.EntMan.SpawnEntity("ClothingModsuitStandardPowerCell", map.GridCoords);
            netSuit = server.EntMan.GetNetEntity(suit);
            var modules = server.EntMan.GetComponent<ModSuitComponent>(suit).ModulesContainer.ContainedEntities;
            Assert.That(modules, Is.Not.Empty, "the suit should start with modules");
            // Force a name far wider than the column so the old layout pushes the values out of the window.
            server.EntMan.System<MetaDataSystem>().SetEntityName(modules[0], new string('W', 80));
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var suit = client.EntMan.GetEntity(netSuit);
            using var window = new ModSuitWindow();
            window.SetSuit(suit);
            window.Measure(new Vector2(600, 600));
            window.Arrange(new UIBox2(0, 0, 600, 600));

            var header = window.FindControl<BoxContainer>("ModulesHeader");
            var list = window.FindControl<BoxContainer>("ModulesList");
            var icons = header.Children.OfType<TextureRect>().ToList();
            Assert.That(icons, Has.Count.EqualTo(4), "the header should have four power columns");
            Assert.That(list.Children, Is.Not.Empty, "the suit should have modules");

            foreach (var child in list.Children)
            {
                var row = (BoxContainer) child;
                Assert.That(row.ChildCount, Is.EqualTo(8), "a module row has three actions, a name and four values");

                for (var i = 0; i < 4; i++)
                {
                    var value = row.GetChild(row.ChildCount - 4 + i);
                    Assert.Multiple(() =>
                    {
                        Assert.That(value.Position.X, Is.EqualTo(icons[i].Position.X).Within(0.5f),
                            "a power value is not under its header icon");
                        Assert.That(value.Size.X, Is.EqualTo(icons[i].Size.X).Within(0.5f),
                            "a power value column has a different width than its header icon");
                    });
                }

                var last = row.GetChild(row.ChildCount - 1);
                Assert.That(last.GlobalPosition.X + last.Width,
                    Is.LessThanOrEqualTo(window.GlobalPosition.X + window.Width + 0.5f),
                    "the power values run past the default window width");

                var nameBox = (BoxContainer) row.GetChild(3);
                var collapsible = (Collapsible) nameBox.GetChild(0);
                var heading = (CollapsibleHeading) collapsible.Heading!;
                Assert.That(heading.Label.Size.X, Is.GreaterThan(0f), "the clipped module name has no width");
            }
        });

        await pair.CleanReturnAsync();
    }
}
