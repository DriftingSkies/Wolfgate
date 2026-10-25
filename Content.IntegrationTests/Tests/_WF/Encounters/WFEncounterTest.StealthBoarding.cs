#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WF.Encounters;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Stealth.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    /// <summary>
    /// A human with a stealth field up stands among a Reaver crew and nobody turns on him; the same human without it is
    /// set on.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task StealthedBoarderIsLeftAlone(bool stealthed)
    {
        EntityUid encounter = default;
        await Server.WaitPost(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestSimRaider"),
                new MapCoordinates(new Vector2(stealthed ? -60000 : -64000, 52000), MapData.MapId), out encounter), Is.True);
        });
        await Until(() => !Ship(encounter).Virtual && !Ship(encounter).Building, 30, "The raider's hull is built.");

        EntityUid boarder = default;
        await Server.WaitPost(() =>
        {
            var group = Ship(encounter).Group;
            var crewman = SEntMan.EntityQuery<WFCrewComponent>(true).First(member => member.Group == group
                && SEntMan.GetComponent<TransformComponent>(member.Owner).GridUid == Ship(encounter).Grid
                && !Server.System<Robust.Shared.Containers.SharedContainerSystem>().IsEntityInContainer(member.Owner)).Owner;
            boarder = SEntMan.SpawnEntity("MobHuman", SEntMan.GetComponent<TransformComponent>(crewman).Coordinates);
            if (stealthed)
                SEntMan.EnsureComponent<StealthComponent>(boarder);
        });

        var targeted = false;
        for (var second = 0; second < 20 && !targeted; second++)
        {
            await RunTicks(STiming.TickRate);
            await Server.WaitPost(() =>
            {
                var group = Ship(encounter).Group;
                foreach (var member in SEntMan.EntityQuery<WFCrewComponent>(true).Where(member => member.Group == group))
                {
                    var htn = SEntMan.GetComponent<HTNComponent>(member.Owner);
                    if (htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, SEntMan) && target == boarder)
                    {
                        targeted = true;
                        TestContext.Out.WriteLine($"Second {second}: {SEntMan.ToPrettyString(member.Owner)} targets the boarder; unseen {Server.System<WFCrewStealthSystem>().Unseen(boarder)}.");
                    }
                }
            });
        }

        Assert.That(targeted, Is.EqualTo(!stealthed), stealthed ? "Nobody turns on a stealthed boarder." : "A boarder in plain view is set on.");
        await Server.WaitPost(() =>
        {
            SEntMan.DeleteEntity(boarder);
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>A ship with its IFF hidden sits inside a guarded ship's attack ring and is never taken for a threat.</summary>
    [Test]
    public async Task CloakedShipInsideTheRingIsNotSeen()
    {
        EntityUid encounter = default, intruder = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestAuditZoneGuarded");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(23000, 23000), MapData.MapId), out encounter), Is.True);

            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Robust.Shared.Maths.Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(23100, 23000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
            Server.System<Content.Server.Shuttles.Systems.ShuttleSystem>().AddIFFFlag(intruder, Content.Shared.Shuttles.Components.IFFFlags.Hide);
        });
        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(state.Grid, state.Group), Does.Not.Contain(intruder),
                "A cloaked ship in the attack ring is not seen.");

            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }

    /// <summary>A crew sent after a ship gives the attack up once that ship cloaks.</summary>
    [Test]
    public async Task AttackOnAShipThatCloaksIsGivenUp()
    {
        EntityUid encounter = default, prey = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestAuditZoneGuarded");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(24000, 24000), MapData.MapId), out encounter), Is.True);

            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            prey = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Robust.Shared.Maths.Vector2i.Zero, new Tile(1));
            Server.System<SharedTransformSystem>().SetCoordinates(prey, new EntityCoordinates(MapData.MapUid, new Vector2(24400, 24000)));
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(Server.System<WFCrewObjectiveSystem>().SetQueue(state.Grid, state.Group, new System.Collections.Generic.List<Content.Shared._WF.NpcCrew.WFCrewObjective>
            {
                new() { Kind = Content.Shared._WF.NpcCrew.WFCrewObjectiveKind.Attack, Target = SEntMan.GetNetEntity(prey), Duration = 600 },
            }), Is.True);
        });
        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(Server.System<WFCrewObjectiveSystem>().AttackTarget(state.Grid, state.Group), Is.EqualTo(prey), "The crew goes after the ship.");
            Server.System<Content.Server.Shuttles.Systems.ShuttleSystem>().AddIFFFlag(prey, Content.Shared.Shuttles.Components.IFFFlags.Hide);
        });
        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(Server.System<WFCrewObjectiveSystem>().AttackTarget(state.Grid, state.Group), Is.Null, "Once it cloaks the crew loses it.");
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(prey);
        });
        await RunTicks(10);
    }
}
