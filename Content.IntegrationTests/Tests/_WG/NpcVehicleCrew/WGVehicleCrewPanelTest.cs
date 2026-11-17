#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.NpcVehicleCrew;
using Content.Shared._WG.NpcVehicleCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.NpcVehicleCrew;

/// <summary>The admin's vehicle crew panel: spawn crews beside you, order one, delete one, clear the rest.</summary>
[TestFixture]
[TestOf(typeof(WGVehicleCrewSystem))]
public sealed class WGVehicleCrewPanelTest
{
    /// <summary>A crew defined by <see cref="WGVehicleCrewTest"/>'s test prototypes.</summary>
    private const string Crew = "WGVehicleCrewTestCrew";

    [Test]
    public async Task ThePanelSpawnsOrdersAndClearsCrews()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var crews = server.System<WGVehicleCrewSystem>();
        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -4; x <= 30; x++)
            for (var y = -4; y <= 30; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            var admin = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, admin);
        });
        await pair.RunTicksSync(5);

        var states = new List<WGVehicleCrewPanelState>();
        var panel = client.System<Content.Client._WG.NpcVehicleCrew.WGVehicleCrewPanelSystem>();
        void Record(WGVehicleCrewPanelState state) => states.Add(state);
        panel.Received += Record;

        async Task Send(WGVehicleCrewPanelRequest request)
        {
            await client.WaitPost(() => panel.Send(request));
            await pair.RunTicksSync(10);
        }

        try
        {
            await Send(new WGVehicleCrewPanelRequest
            {
                Action = WGVehicleCrewPanelAction.Spawn, Crew = Crew, Behaviour = WGVehicleBehaviour.Hold, Count = 2, Distance = 8,
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(crews.GetCrews(), Has.Count.EqualTo(2), "The panel should spawn two crews.");
                Assert.That(crews.GetCrews().All(crew => crew.Comp.Behaviour == WGVehicleBehaviour.Hold), Is.True);
            });
            Assert.That(states.Last().Error, Is.False, states.Last().Message);
            Assert.That(states.Last().Crews, Has.Count.EqualTo(2), "The client should be told about both crews.");

            // The window itself: its roster lists every crew, and a refresh brings in the two out there.
            Content.Client._WG.NpcVehicleCrew.WGVehicleCrewPanel? window = null;
            await client.WaitPost(() =>
            {
                window = new Content.Client._WG.NpcVehicleCrew.WGVehicleCrewPanel();
                window.OpenCentered();
            });
            await pair.RunTicksSync(10);
            await client.WaitAssertion(() =>
            {
                var text = string.Join("\n", Descendants(window!).OfType<Robust.Client.UserInterface.Controls.Label>().Select(label => label.Text));
                Assert.That(text, Does.Contain(Crew), "The roster doesn't list the test crew.");
                Assert.That(Descendants(window!).OfType<Robust.Client.UserInterface.Controls.Button>()
                    .Count(button => button.Text == Robust.Shared.Localization.Loc.GetString("wg-vehiclecrew-panel-goto")), Is.EqualTo(2),
                    "The window should list both live crews.");
                window!.Close();
            });

            var first = states.Last().Crews[0].Vehicle;
            await Send(new WGVehicleCrewPanelRequest { Action = WGVehicleCrewPanelAction.Order, Vehicle = first, Behaviour = WGVehicleBehaviour.Patrol });
            await server.WaitAssertion(() =>
            {
                var crew = entMan.GetComponent<WGVehicleCrewComponent>(entMan.GetEntity(first));
                Assert.That(crew.Behaviour, Is.EqualTo(WGVehicleBehaviour.Patrol), "The order didn't take.");
            });

            await Send(new WGVehicleCrewPanelRequest { Action = WGVehicleCrewPanelAction.Delete, Vehicle = first });
            await server.WaitAssertion(() => Assert.That(crews.GetCrews(), Has.Count.EqualTo(1), "Delete should take one crew."));

            await Send(new WGVehicleCrewPanelRequest { Action = WGVehicleCrewPanelAction.ClearAll });
            await server.WaitAssertion(() => Assert.That(crews.GetCrews(), Is.Empty, "Clear all should take the rest."));
            Assert.That(states.Last().Crews, Is.Empty);
        }
        finally
        {
            panel.Received -= Record;
        }

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Robust.Client.UserInterface.Control> Descendants(Robust.Client.UserInterface.Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
