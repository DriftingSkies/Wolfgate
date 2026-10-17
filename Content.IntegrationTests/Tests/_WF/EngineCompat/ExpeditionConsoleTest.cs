using System.Linq;
using System.Numerics;
using Content.Server.Salvage.Expeditions;
using System.Reflection;
using Content.Client.Salvage.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Station.Systems;
using Content.Shared.Maps;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.EngineCompat;

/// <summary>An expedition shuttle's console shows the station's offers to the player who opens it.</summary>
public sealed class ExpeditionConsoleTest : InteractionTest
{
    [Test]
    public async Task ConsoleListsOffers()
    {
        EntityUid console = default;
        EntityUid station = default;

        await Server.WaitPost(() =>
        {
            var gameMap = Server.ResolveDependency<IPrototypeManager>().Index<GameMapPrototype>("Judiciary");
            Assert.That(SEntMan.System<MapLoaderSystem>().TryLoadGrid(MapId, gameMap.MapPath, out var grid, offset: new Vector2(500f, 500f)));
            station = SEntMan.System<StationSystem>().InitializeNewStation(gameMap.Stations["Judiciary"], [grid!.Value.Owner]);

            var query = SEntMan.EntityQueryEnumerator<SalvageExpeditionConsoleComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid.Value.Owner)
                    console = uid;
            }

            Assert.That(console, Is.Not.EqualTo(EntityUid.Invalid), "Judiciary has no expedition console.");
            SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetComponent<TransformComponent>(console).Coordinates);
        });
        await RunTicks(10);

        var offers = 0;
        await Server.WaitAssertion(() =>
        {
            offers = SEntMan.GetComponent<SalvageExpeditionDataComponent>(station).Missions.Count;
            Assert.That(offers, Is.Positive, "The station made no offers.");
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(console, SalvageConsoleUiKey.Expedition, SPlayer);
        });
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryGetBui(SalvageConsoleUiKey.Expedition, out var bui, SEntMan.GetNetEntity(console)));
            var window = (SalvageExpeditionWindow) typeof(SalvageExpeditionConsoleBoundUserInterface)
                .GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bui);
            Assert.That(window, Is.Not.Null);
            Assert.That(window.FindControl<BoxContainer>("Container").ChildCount, Is.EqualTo(offers), "The console window shows no offers.");
        });

        Target = SEntMan.GetNetEntity(console);
        ushort index = 0;
        await Server.WaitPost(() => index = SEntMan.GetComponent<SalvageExpeditionDataComponent>(station).Missions.Keys.First());
        await SendBui(SalvageConsoleUiKey.Expedition, new ClaimSalvageMessage { Index = index });

        var spawned = false;
        for (var i = 0; i < 60 && !spawned; i++)
        {
            await RunTicks(10);
            await Server.WaitPost(() => spawned = SEntMan.EntityQuery<SalvageExpeditionComponent>(true).Any());
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<SalvageExpeditionDataComponent>(station).ActiveMission, Is.EqualTo(index), "The claim was refused.");
            Assert.That(spawned, Is.True, "The claimed expedition never spawned.");
            // Off the shuttle, so teardown doesn't delete the player along with the FTL map.
            SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetCoordinates(PlayerCoords));
        });
    }
}
