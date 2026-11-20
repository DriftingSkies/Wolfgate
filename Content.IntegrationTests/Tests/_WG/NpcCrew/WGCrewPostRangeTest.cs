#nullable enable
using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

/// <summary>
/// A crewman walks to a new post from anywhere past his arrival range, and only once he has reached it does the wider
/// leave range keep a crewmate's nudge from sending him back, also when his orders are re-applied; a new patrol point
/// starts over.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCrewSystem))]
public sealed class WGCrewPostRangeTest
{
    [Test]
    public async Task NewPostUsesArrivalRangeUntilReached()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            for (var x = 0; x < 5; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            var crewSystem = entMan.System<WGCrewSystem>();
            var crewman = crewSystem.SpawnCrewman(WGCrewRoles.Deckhand, map.GridCoords, "posttest")!.Value;
            var htn = entMan.GetComponent<HTNComponent>(crewman);
            entMan.System<NPCSystem>().SleepNPC(crewman, htn);
            var crew = entMan.GetComponent<WGCrewComponent>(crewman);
            var xform = entMan.GetComponent<TransformComponent>(crewman);
            var transform = entMan.System<SharedTransformSystem>();

            float LeaveRange() =>
                htn.Blackboard.TryGetValue<float>(WGCrewSystem.PostLeaveRangeKey, out var range, entMan) ? range : float.NaN;

            // A post two tiles off: past the arrival range, inside the leave range. He must still walk there.
            var post = new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 0.5f));
            crew.Post = post;
            crewSystem.SetPost(crewman, crew, htn, post);
            Assert.That(crew.PostReached, Is.False);
            Assert.That(LeaveRange(), Is.EqualTo(crew.PostRange), "Until he arrives, the arrival range decides.");
            crewSystem.MarkPostReached(crewman, crew, htn, xform);
            Assert.That(crew.PostReached, Is.False, "Two tiles off is not there yet.");

            // On the post: the wider leave range takes over.
            transform.SetCoordinates(crewman, post);
            crewSystem.MarkPostReached(crewman, crew, htn, xform);
            Assert.That(crew.PostReached, Is.True);
            Assert.That(LeaveRange(), Is.EqualTo(crew.PostLeaveRange), "Once there, a nudge doesn't send him back.");

            // Nudged two tiles off, then his orders re-applied with the same post (an engagement change): he keeps
            // having arrived, so the nudge still doesn't send him back.
            transform.SetCoordinates(crewman, new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));
            crewSystem.SetEngagement((crewman, crew), WGCrewEngagement.WhenAttacked);
            Assert.That(crew.PostReached, Is.True, "Re-applying the same post keeps his arrival.");
            Assert.That(LeaveRange(), Is.EqualTo(crew.PostLeaveRange));

            // A new patrol point starts over with the arrival range.
            crewSystem.SetPost(crewman, crew, htn, new EntityCoordinates(map.Grid.Owner, new Vector2(4.5f, 0.5f)));
            Assert.That(crew.PostReached, Is.False);
            Assert.That(LeaveRange(), Is.EqualTo(crew.PostRange));
        });

        await pair.CleanReturnAsync();
    }
}
