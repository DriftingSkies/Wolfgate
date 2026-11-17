#nullable enable
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WG.Encounters;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

/// <summary>A ship's neutral companies are left alone by its zones.</summary>
public sealed partial class WGEncounterTest
{
    /// <summary>A ship of a company the guard leaves alone is neither warned nor fired on inside its zones; the same ship without the flag is.</summary>
    [Test]
    public async Task ZonesLeaveNeutralCompaniesAlone()
    {
        EntityUid encounter = default, intruder = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestAuditZoneGuardedProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(23000, 23000), MapData.MapId), out encounter), Is.True);
            SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["guarded"].NeutralCompanies.Add("WGUnderground");

            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(23100, 23000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
            SEntMan.EnsureComponent<CompanyComponent>(intruder).CompanyName = "WGUnderground";
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(state.Warned, Is.Empty, "A neutral ship is not warned.");
            Assert.That(state.Engaged, Is.Empty, "Nor fired on.");
            Assert.That(Server.System<WGCrewAlertSystem>().GetHostileShips(state.Grid, state.Group), Is.Empty);
            SEntMan.RemoveComponent<CompanyComponent>(intruder);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(state.Engaged.Contains(intruder), Is.True, "A ship of no company is an intruder.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WGEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }
}
