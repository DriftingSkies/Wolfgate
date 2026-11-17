using System.Linq;
using System.Numerics;
using Content.Client._WG.Roadmap;
using Content.Shared._WG.Roadmap;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.Roadmap;

public sealed class RoadmapTest
{
    /// <summary>
    /// Every column, item and state has its strings, and all roadmap markup parses.
    /// </summary>
    [Test]
    public async Task EntriesResolve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Client.ResolveDependency<IPrototypeManager>();
        var loc = pair.Client.ResolveDependency<ILocalizationManager>();

        await pair.Client.WaitAssertion(() =>
        {
            var columns = proto.EnumeratePrototypes<RoadmapColumnPrototype>().ToList();
            Assert.That(columns, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                foreach (var state in Enum.GetValues<RoadmapItemState>())
                {
                    Assert.That(loc.HasString(RoadmapItem.StateLocFor(state)), $"No status string for {state}.");
                }

                var header = loc.GetString("wg-roadmap-header", ("year", "2026"));
                Assert.That(FormattedMessage.TryFromMarkup(header, out _, out var headerError), headerError);

                foreach (var column in columns)
                {
                    Assert.That(loc.HasString(column.Name), $"{column.ID} has no string {column.Name}.");
                    Assert.That(column.Items, Is.Not.Empty, $"{column.ID} is empty.");

                    foreach (var item in column.Items)
                    {
                        Assert.That(loc.HasString(item.Name), $"{column.ID} has no string {item.Name}.");
                        if (item.Description is not { } description)
                            continue;

                        Assert.That(loc.HasString(description), $"{column.ID} has no string {description}.");
                        Assert.That(FormattedMessage.TryFromMarkup(loc.GetString(description), out _, out var error),
                            $"{description}: {error}");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The window lays out one column per prototype side by side, and the controller opens and closes it.
    /// </summary>
    [Test]
    public async Task WindowBuildsAndToggles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Client.ResolveDependency<IPrototypeManager>();
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();

        await pair.Client.WaitAssertion(() =>
        {
            var columns = proto.EnumeratePrototypes<RoadmapColumnPrototype>()
                .OrderBy(c => c.Order)
                .ThenBy(c => c.ID, StringComparer.Ordinal)
                .ToList();

            using (var window = new RoadmapWindow())
            {
                Assert.That(window.Columns.ChildCount, Is.EqualTo(columns.Count));

                var size = new Vector2(900, 800);
                window.SetSize = size;
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));

                for (var i = 0; i < columns.Count; i++)
                {
                    var column = window.Columns.GetChild(i);
                    var items = column.Children.OfType<RoadmapItem>().ToList();
                    Assert.Multiple(() =>
                    {
                        Assert.That(items, Has.Count.EqualTo(columns[i].Items.Count), columns[i].ID);
                        Assert.That(column.Size.X, Is.GreaterThan(0), $"{columns[i].ID} has no width.");
                        Assert.That(column.GlobalPosition.X + column.Size.X, Is.LessThanOrEqualTo(size.X),
                            $"{columns[i].ID} runs past the window's right edge.");
                        Assert.That(items.Select(item => item.HeaderText),
                            Is.EqualTo(columns[i].Items.Select(item => Loc.GetString(item.Name))));
                        Assert.That(items.Select(item => item.ItemState),
                            Is.EqualTo(columns[i].Items.Select(item => item.State)));
                        Assert.That(items.Any(item => item.Expanded), Is.False, "Items start collapsed.");
                    });
                }
            }

            var controller = ui.GetUIController<RoadmapUIController>();

            // A pooled client that has been in the lobby already opened it once.
            if (controller.IsOpen)
                controller.ToggleRoadmap();

            controller.ToggleRoadmap();
            Assert.That(controller.IsOpen);
            Assert.That(ui.WindowRoot.Children.OfType<RoadmapWindow>().Count(), Is.EqualTo(1));

            controller.ToggleRoadmap();
            Assert.That(controller.IsOpen, Is.False);
            Assert.That(ui.WindowRoot.Children.OfType<RoadmapWindow>(), Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The window shrinks to fit short viewports, including 720p at 1.5x UI scale, and keeps its design size when
    /// there is room; the scroll container takes whatever no longer fits.
    /// </summary>
    [TestCase(1920, 1080)]
    [TestCase(1366, 768)]
    [TestCase(1280, 720)]
    [TestCase(853, 480)]
    public async Task WindowFitsViewport(int width, int height)
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Client.WaitAssertion(() =>
        {
            var viewport = new Vector2(width, height);
            using var window = new RoadmapWindow();
            var design = window.SetSize;

            window.FitTo(viewport);
            window.Measure(viewport);
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, window.DesiredSize));

            var scroll = window.Contents.Children.OfType<ScrollContainer>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(window.Size.X, Is.LessThanOrEqualTo(width), "Wider than the viewport.");
                Assert.That(window.Size.Y, Is.LessThanOrEqualTo(height), "Taller than the viewport.");
                Assert.That(scroll.GlobalPosition.Y + scroll.Size.Y, Is.LessThanOrEqualTo(window.Size.Y),
                    "The scroll area runs past the window's bottom edge.");

                if (width >= design.X + 40 && height >= design.Y + 40)
                    Assert.That(window.Size, Is.EqualTo(design), "A roomy viewport keeps the design size.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
