#nullable enable
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Shared._WG.Encounters;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

public sealed partial class WGEncounterTest
{
    /// <summary>
    /// An encounter an admin started pinned has no clock, takes no slot under the cap, and keeps its ships after it
    /// resolves, however far away everyone is, until an admin ends it; one started to jump out as usual does so.
    /// </summary>
    [Test]
    public async Task AdminEncountersStayUntilEnded()
    {
        EntityUid encounter = default, ship = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryRivalProto);
            var admin = SEntMan.GetEntity(Player);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-41000, -41000), MapData.MapId), out encounter, admin, pinned: true), Is.True);
            var comp = SEntMan.GetComponent<WGEncounterComponent>(encounter);
            ship = comp.Ships["rival"].Grid;
            Assert.That(comp.Pinned, Is.True, "An admin's encounter is pinned.");
            Assert.That(comp.Expires, Is.Null, "With no clock on it.");
            Assert.That(comp.OffBudget, Is.True, "And no slot under the cap.");

            Server.System<WGEncounterSystem>().Resolve(encounter, WGEncounterResolution.Completed);
            Assert.That(comp.JumpAt, Is.Null, "Resolved, its ships do not jump out.");

            // The same encounter started by the same admin, but left to the usual rules.
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-43000, -43000), MapData.MapId), out var loose, admin), Is.True);
            var looseComp = SEntMan.GetComponent<WGEncounterComponent>(loose);
            Assert.That(looseComp.Pinned, Is.False, "An admin may start an encounter that follows the usual rules.");
            Server.System<WGEncounterSystem>().Resolve(loose, WGEncounterResolution.Completed);
            Assert.That(looseComp.JumpAt, Is.Not.Null, "Resolved, that one jumps out.");
            Server.System<WGEncounterSystem>().End(loose);
        });
        // Well past the jump delay and several cleanup polls, with the player nowhere near.
        await RunTicks(900);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(encounter), Is.True, "The encounter is still there.");
            Assert.That(SEntMan.EntityExists(ship), Is.True, "And so is its ship.");

            Server.System<WGEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(ship), Is.False, "Ending it removes the ship.");
        });
    }
}
