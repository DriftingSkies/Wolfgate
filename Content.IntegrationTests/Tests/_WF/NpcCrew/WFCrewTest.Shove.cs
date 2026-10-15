#nullable enable
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A crate wedged in the only gap in a wall is shoved aside by crew who may; others stay stuck behind it.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task CrewShoveACrateOutOfTheirWay(bool shove)
    {
        var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
        var crewSystem = Server.System<WFCrewSystem>();
        EntityUid crew = default, crate = default;
        await Server.WaitPost(() =>
        {
            for (var y = 0; y < 9; y++)
            {
                if (y != 4)
                    SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(4.5f, y + 0.5f)));
            }

            crate = SEntMan.SpawnAtPosition("CrateGenericSteel", new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)));
            crew = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f, 4.5f)), "shove")!.Value;
            SEntMan.GetComponent<WFCrewComponent>(crew).PostRange = 0.25f;
            crewSystem.SetPost(crew, new EntityCoordinates(deck, new Vector2(6.5f, 4.5f)));
            // Set after the post, which applies the crew defaults to the blackboard.
            var htn = SEntMan.GetComponent<HTNComponent>(crew);
            htn.Blackboard.SetValue("NavShove", shove);
            htn.Blackboard.SetValue("IdleRange", 0.1f);
        });

        if (shove)
        {
            await WaitUntil(() => SEntMan.GetComponent<TransformComponent>(crew).LocalPosition.X > 5.2f,
                900, () => $"Crew should shove the crate and pass. crate={SEntMan.GetComponent<TransformComponent>(crate).LocalPosition} {Describe(crew)}");
            await Server.WaitAssertion(() =>
            {
                var at = SEntMan.GetComponent<TransformComponent>(crate).LocalPosition;
                Assert.That(at.Y < 4f || at.Y > 5f || at.X > 5f, Is.True, $"The crate left the gap: {at}.");
            });
        }
        else
        {
            await RunTicks(300);
            await Server.WaitAssertion(() =>
                Assert.That(SEntMan.GetComponent<TransformComponent>(crew).LocalPosition.X, Is.LessThan(4f), "Without shoving the crate holds them."));
        }
    }
}
