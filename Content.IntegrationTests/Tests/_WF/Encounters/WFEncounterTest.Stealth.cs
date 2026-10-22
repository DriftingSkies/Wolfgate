#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared.Shuttles.Components;
using Content.Shared.Stealth.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    /// <summary>
    /// A crew doesn't see a player with a stealth field up, or a ship with its IFF hidden, until either does something
    /// hostile; a theft in plain view is still noticed.
    /// </summary>
    [Test]
    public async Task CrewsDoNotSeeStealthUntilItActsHostile()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(Convoy);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-51000, 51000), MapData.MapId), out var encounter), Is.True);
            var lead = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["lead"];
            var containers = Server.System<Robust.Shared.Containers.SharedContainerSystem>();
            var crewman = SEntMan.EntityQuery<WFCrewComponent>(true).First(member => member.Group == lead.Group
                && SEntMan.GetComponent<TransformComponent>(member.Owner).GridUid == lead.Grid
                && !containers.IsEntityInContainer(member.Owner)).Owner;

            var transform = Server.System<SharedTransformSystem>();
            var player = SEntMan.GetEntity(Player);
            var at = SEntMan.GetComponent<TransformComponent>(crewman).Coordinates;
            transform.SetCoordinates(player, at);

            var weapons = Server.System<WFCrewWeaponSystem>();
            var stealth = Server.System<WFCrewStealthSystem>();
            Assert.That(weapons.CanSee(crewman, player), Is.True, "A player beside a crewman is seen.");

            SEntMan.EnsureComponent<StealthComponent>(player);
            Assert.That(weapons.CanSee(crewman, player), Is.False, "With a stealth field up he is not.");
            Assert.That(weapons.CanSee(crewman, player, ignoreStealth: true), Is.True, "A theft in plain view is still noticed.");

            // The test body has no mob state; a crew only answers a mob.
            SEntMan.EnsureComponent<Content.Shared.Mobs.Components.MobStateComponent>(player);
            Server.System<WFCrewSystem>().AnswerAttack(lead.Grid, lead.Group, player);
            // The crewman may take shelter out of sight; what matters is that the field no longer hides him.
            Assert.That(stealth.Unseen(player), Is.False, "Once he attacks the crew he is seen.");
            SEntMan.RemoveComponent<StealthComponent>(player);
            SEntMan.RemoveComponent<Content.Shared.Mobs.Components.MobStateComponent>(player);

            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            transform.SetCoordinates(grid.Owner, new EntityCoordinates(MapData.MapUid, new Vector2(-51000, 51400)));
            Server.System<ShuttleSystem>().AddIFFFlag(grid.Owner, IFFFlags.Hide);
            Assert.That(stealth.ShipUnseen(grid.Owner), Is.True, "A cloaked ship is not seen.");
            Server.System<WFCrewAlertSystem>().ReportShipThreat(lead.Grid, lead.Group, grid.Owner);
            Assert.That(stealth.ShipUnseen(grid.Owner), Is.False, "Once it attacks it is seen.");

            transform.SetCoordinates(player, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(grid.Owner);
        });
        await RunTicks(10);
    }
}
