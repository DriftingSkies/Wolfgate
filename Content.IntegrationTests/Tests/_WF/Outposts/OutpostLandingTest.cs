#nullable enable
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>
/// The landing guard: a hull can't descend from orbit over an outpost's claim, and one falling without lift over an
/// outpost comes down beside it instead, the outpost untouched.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostLandingTest
{
    /// <summary>Where a 15x15 hull covers the outpost founded at the site.</summary>
    private static readonly Vector2 OverOutpost = new(0f, -7f);

    [Test]
    public async Task HullsKeepOffClaims()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var levels = server.System<CEZLevelsSystem>();
        var transform = server.System<SharedTransformSystem>();

        EntityUid outpost = default;
        EntityUid console = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var found, out var reason), Is.True, $"Founding was refused: {reason}.");
                outpost = found!.Value;
                console = entMan.GetComponent<WFOutpostComponent>(outpost).Console;
            });

            // Over the outpost in orbit: the descent is refused, naming the claim.
            var orbiter = await BuildHull(pair, entMan.GetComponent<MapComponent>(layers[^1]).MapId, OverOutpost);
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<WFOrbitEntrySystem>().TryEnterAtmosphere(FindShuttleConsole(entMan, orbiter), true, out var reason),
                    Is.False, "A hull can't descend onto an outpost's claim.");
                Assert.That(reason, Does.Contain(entMan.GetComponent<MetaDataComponent>(outpost).EntityName), "The refusal names the outpost.");
                entMan.DeleteEntity(orbiter);
            });

            // Over the outpost in the air with no lift: it falls, and comes down beside the outpost.
            var faller = await BuildHull(pair, entMan.GetComponent<MapComponent>(layers[1]).MapId, OverOutpost);
            var landed = false;
            for (var i = 0; i < 120 && !landed; i++)
            {
                await pair.RunTicksSync(30);
                await server.WaitPost(() => landed = entMan.GetComponent<TransformComponent>(faller).MapUid == ground);
            }

            await server.WaitAssertion(() =>
            {
                Assert.That(landed, Is.True, "The hull never reached the ground.");
                Assert.That(levels.WfClaimUnder(new[] { faller }, ground), Is.Null, "The hull came down clear of the claim.");
                Assert.That(entMan.EntityExists(console) && entMan.HasComponent<WFOutpostComponent>(outpost), Is.True,
                    "The outpost and its console stand.");

                var distance = Vector2.Distance(transform.GetWorldPosition(faller), transform.GetWorldPosition(outpost));
                Assert.That(distance, Is.LessThan(48f), "It came down next to the outpost, not far off.");
                Assert.That(Vector2.Distance(transform.GetWorldPosition(faller), OverOutpost), Is.GreaterThan(1f),
                    "Precondition: the hull was built over the outpost and moved off it.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
