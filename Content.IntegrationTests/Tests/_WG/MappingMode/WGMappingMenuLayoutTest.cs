#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WG.MappingMode;
using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingTools;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WG.MappingMode.WGMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WG.MappingMode;

/// <summary>
/// At its opening size every tab of the Mapping window shows all its buttons in full without scrolling or resizing,
/// and picking a reference file shows its floor plan from the server.
/// </summary>
[TestFixture]
public sealed class WGMappingMenuLayoutTest
{
    private static IEnumerable<Control> Visible(Control root)
    {
        foreach (var child in root.Children)
        {
            if (!child.Visible)
                continue;

            yield return child;
            foreach (var inner in Visible(child))
            {
                yield return inner;
            }
        }
    }

    [Test]
    public async Task EveryTabFitsAndPreviewShows()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var client = pair.Client;
        var clientSys = client.EntMan.System<WGMappingClientSystem>();
        var uiMan = client.ResolveDependency<IUserInterfaceManager>();
        await Enable(pair);

        await client.WaitPost(() => clientSys.Send(new WGMappingCreateRequest("menu test")));
        for (var i = 0; i < 100 && !clientSys.InSession; i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(20);

        WGMappingMenuWindow window = default!;
        await client.WaitPost(() =>
        {
            uiMan.GetUIController<WGMappingMenuUIController>().OpenWindow();
            window = uiMan.WindowRoot.Children.OfType<WGMappingMenuWindow>().Single();
        });

        // Let the window's own library and grid answers land before filling it with a full list.
        await pair.RunTicksSync(20);
        await client.WaitPost(() =>
        {
            var versions = Enumerable.Range(1, 3)
                .Select(n => new WGMappingLibraryVersion(
                    new WGMappingVersionName(n, new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc),
                        WGMappingVersionKind.Manual), 2, 1024 * 1024, true))
                .ToList();
            var map = new WGMappingLibraryMap("A rather long map name for the row", versions);
            window.SetLibrary(new WGMappingLibraryEvent(new List<WGMappingLibraryMap> { map }, 1024, 1024 * 1024, 50, 25));
            window.LibraryList.ExpandForTest(map.Name);
            window.SetGrids(Enumerable.Range(0, 6)
                .Select(n => new WGMappingGridInfo(NetEntity.Invalid, $"Grid number {n}", 1000, 5000, n))
                .ToList());
            window.SetPendingDelete(new WGMappingGridInfo(NetEntity.Invalid, "Grid number 0", 1000, 5000));
        });

        for (var tab = 0; tab < window.Tabs.ChildCount; tab++)
        {
            var current = tab;
            await client.WaitPost(() => window.Tabs.CurrentTab = current);
            await pair.RunTicksSync(5);
            await client.WaitAssertion(() =>
            {
                var title = window.Tabs.GetActualTabTitle(current);
                var bounds = Box2.FromDimensions(window.GlobalPosition, window.Size);
                TestContext.Out.WriteLine($"{title}: window {window.Size}, desired {window.DesiredSize}");
                var page = window.Tabs.GetChild(current);
                Assert.Multiple(() =>
                {
                    foreach (var button in Visible(page).OfType<BaseButton>())
                    {
                        var rect = Box2.FromDimensions(button.GlobalPosition, button.Size);
                        var name = $"{title}: {(button as Button)?.Text} at {rect} in {bounds}";
                        Assert.That(rect.Left >= bounds.Left - 1 && rect.Right <= bounds.Right + 1, Is.True,
                            $"Runs off the side. {name}");
                        Assert.That(rect.Bottom >= bounds.Bottom - 1 && rect.Top <= bounds.Top + 1, Is.True,
                            $"Runs off the bottom. {name}");
                    }

                    foreach (var scroll in Visible(page).Prepend(page).OfType<ScrollContainer>())
                    {
                        var content = scroll.Children.First(c => c is not ScrollBar).DesiredSize;
                        Assert.That(content.Y, Is.LessThanOrEqualTo(scroll.Size.Y + 1),
                            $"{title} has to scroll: {content} in {scroll.Size}.");
                        Assert.That(content.X, Is.LessThanOrEqualTo(scroll.Size.X + 1),
                            $"{title} is too wide: {content} in {scroll.Size}.");
                    }
                });
            });
        }

        // The Reference tab fetched the list when it opened; picking a file fetches its floor plan.
        await client.WaitPost(() => window.Tabs.CurrentTab = 2);
        for (var i = 0; i < 100 && clientSys.ReferenceFiles.Count == 0; i++)
        {
            await pair.RunTicksSync(1);
        }

        await client.WaitPost(() =>
        {
            Assert.That(clientSys.ReferenceFiles.Any(f => f.Source == MappingMapSource.Ships), Is.True);
            var ship = window.ReferenceList.First(item =>
                item.Metadata is string path &&
                clientSys.ReferenceFiles.Any(f => f.Path == path && f.Source == MappingMapSource.Ships));
            ship.Selected = true;
        });

        // The window asks after a delay in real time, which a fast runner can tick past.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30) && window.ReferencePreview.Preview == null)
        {
            await pair.RunTicksSync(1);
        }

        await client.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine(window.ReferenceInfo.Text);
            var preview = window.ReferencePreview.Preview;
            Assert.That(preview, Is.Not.Null, $"No preview: {window.ReferenceInfo.Text}");
            Assert.That(preview!.Grids, Is.Not.Empty);
            Assert.That(preview.Tiles, Is.Positive);
            Assert.That(preview.Entities, Is.Positive);
            Assert.That(preview.Grids.Sum(g => g.Walls.Count), Is.Positive, "A ship has walls.");
            Assert.That(window.ReferencePlaceButton.Disabled, Is.False);
            window.Close();
        });

        await pair.CleanReturnAsync();
    }
}
