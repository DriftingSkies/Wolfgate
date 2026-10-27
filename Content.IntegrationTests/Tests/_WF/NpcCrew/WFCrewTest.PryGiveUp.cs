#nullable enable
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._Mono.CCVar;
using Content.Shared._WF.NpcCrew;
using System.Collections.Generic;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Prying.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>An NPC whose pry never starts gives up on the door instead of retrying every tick.</summary>
    [Test]
    public async Task CrewGivesUpOnUnpryableDoor()
    {
        var config = Client.ResolveDependency<IConfigurationManager>();
        var echo = config.GetCVar(MonoCVars.AreaEchoEnabled);
        await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, false));
        try
        {
            var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
            var crewSystem = Server.System<WFCrewSystem>();
            var readers = Server.System<AccessReaderSystem>();
            Server.System<CrewPryRefuseSystem>();
            EntityUid crew = default, door = default;
            await Server.WaitPost(() =>
            {
                for (var y = 0; y < 9; y++)
                {
                    if (y != 4)
                        SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(4.5f, y + 0.5f)));
                }
                door = SEntMan.SpawnAtPosition(TestDock, new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)));
                SEntMan.GetComponent<DoorComponent>(door).BumpOpen = false;
                SEntMan.EnsureComponent<CrewPryRefuseComponent>(door);
                Assert.That(readers.GetMainAccessReader(door, out var reader), Is.True);
                readers.SetAccesses(reader!.Value, reader.Value.Comp, new List<ProtoId<AccessLevelPrototype>> { "Engineering" });
                crew = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand,
                    new EntityCoordinates(deck, new Vector2(2.5f, 4.5f)), "pry")!.Value;
                Server.System<InventorySystem>().TryUnequip(crew, "id", silent: true, force: true);
                var htn = SEntMan.GetComponent<HTNComponent>(crew);
                htn.Blackboard.SetValue("NavPry", true);
                htn.Blackboard.SetValue("IdleRange", 0.1f);
                SEntMan.GetComponent<WFCrewComponent>(crew).PostRange = 0.25f;
                crewSystem.SetPost(crew, new EntityCoordinates(deck, new Vector2(6.5f, 4.5f)));
            });

            await RunTicks(600);

            await Server.WaitAssertion(() =>
            {
                var attempts = SEntMan.GetComponent<CrewPryRefuseComponent>(door).PryAttempts;
                Assert.That(attempts, Is.GreaterThan(0), "Crew should try the door.");
                Assert.That(attempts, Is.LessThanOrEqualTo(6), "Crew must give up instead of prying every tick.");
                Assert.That(SEntMan.GetComponent<DoorComponent>(door).State, Is.Not.EqualTo(DoorState.Open));
            });
        }
        finally
        {
            await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, echo));
        }
    }
}

/// <summary>Refuses every pry and counts the attempts.</summary>
[RegisterComponent]
public sealed partial class CrewPryRefuseComponent : Component
{
    public int PryAttempts;
}

public sealed class CrewPryRefuseSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrewPryRefuseComponent, BeforePryEvent>(OnPry);
    }

    private void OnPry(Entity<CrewPryRefuseComponent> ent, ref BeforePryEvent args)
    {
        ent.Comp.PryAttempts++;
        args.Cancelled = true;
    }
}
