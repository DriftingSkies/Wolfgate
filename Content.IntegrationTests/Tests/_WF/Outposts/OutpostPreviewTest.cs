#nullable enable
using System.Collections.Generic;
using Content.Client._WF.Outposts;
using Content.Client._WF.ShipPreview;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Shared._WF.Outposts;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>The owner's client gets a save's grid on request, rate limited, and the ship previewer loads it.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostPreviewTest
{
    [Test]
    public async Task PreviewASave()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var outposts = server.System<WFOutpostSystem>();
        var owner = pair.Player!.UserId;
        var received = new List<WFOutpostPreviewEvent>();

        async Task Request(int slot)
        {
            var count = received.Count;
            await client.WaitPost(() => client.System<WFOutpostSavesSystem>().RequestPreview(slot));
            for (var i = 0; i < 600 && received.Count == count; i++)
            {
                await pair.RunTicksSync(1);
            }

            Assert.That(received, Has.Count.EqualTo(count + 1), $"No answer to a preview of slot {slot}.");
        }

        try
        {
            await client.WaitPost(() => client.System<WFOutpostSavesSystem>().PreviewReceived += received.Add);
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, layers[0]),
                    new EntityCoordinates(layers[0], site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                Assert.That(outposts.TrySave(user, (grid!.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value)), 0, out reason), Is.True,
                    $"Saving was refused: {reason}.");
            });
            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(owner), maxTicks: 600);

            await Request(0);
            var preview = received[^1];
            Assert.That(preview.Slot, Is.EqualTo(0));
            Assert.That(preview.Yaml, Is.Not.Null, $"The preview came back empty: {preview.Error}.");

            // Asked again at once, the server says to wait.
            await Request(2);
            Assert.That(received[^1].Error, Is.EqualTo("wf-outpost-preview-wait"));

            await client.WaitAssertion(() =>
            {
                var previews = client.System<ShipPreviewSystem>();
                var handle = previews.Acquire();
                try
                {
                    Assert.That(previews.TryLoadText(handle, new ResPath($"/WFOutpostSave/0/{preview.Saved.Ticks}.yml"),
                        preview.Yaml!, null, out var loaded), Is.True, "The previewer couldn't load the save.");
                    Assert.That(loaded.TileCount, Is.EqualTo(9), "The founding square has 3 x 3 tiles.");
                }
                finally
                {
                    previews.Release(handle);
                }
            });
        }
        finally
        {
            await client.WaitPost(() => client.System<WFOutpostSavesSystem>().PreviewReceived -= received.Add);
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
