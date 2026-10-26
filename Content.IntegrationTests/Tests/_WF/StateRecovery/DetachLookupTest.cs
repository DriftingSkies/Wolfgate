#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Client._WF.StateRecovery;
using Content.IntegrationTests.Pair;
using Robust.Client.GameStates;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.IntegrationTests.Tests._WF.StateRecovery;

/// <summary>
/// An entity that leaves view in the same batch as its parent must leave the client's lookup tree.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDetachLookupSystem))]
public sealed class DetachLookupTest
{
    [Test]
    public async Task ChildDetachedBeforeParentLeavesTree()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var parent = EntityUid.Invalid;
        var nets = new List<NetEntity>();
        await server.WaitPost(() =>
        {
            parent = em.SpawnEntity(null, new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));
            var child = em.SpawnEntity(null, new EntityCoordinates(parent, Vector2.Zero));
            nets.Add(em.GetNetEntity(child));
            nets.Add(em.GetNetEntity(parent));
        });
        await pair.RunTicksSync(5);
        await AssertClean(pair, "before detaching");

        // Child first: the order that makes the engine skip the child.
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientGameStateManager>().DetachImmediate(nets));
        await AssertClean(pair, "after detaching");

        await server.WaitPost(() => em.DeleteEntity(parent));
        await pair.RunTicksSync(5);
        await AssertClean(pair, "after the server deleted them");
        await pair.CleanReturnAsync();
    }

    private static async Task AssertClean(TestPair pair, string when)
    {
        var problems = new List<string>();
        await pair.Client.WaitPost(() => problems.AddRange(StaleEntries(pair.Client.EntMan)));
        Assert.That(problems, Is.Empty, $"{when}:\n{string.Join("\n", problems)}");
    }

    /// <summary>Sundries tree entries whose entity is deleted or no longer on that broadphase.</summary>
    private static List<string> StaleEntries(IEntityManager em)
    {
        var found = new List<string>();
        var lookup = em.System<EntityLookupSystem>();
        var query = em.AllEntityQueryEnumerator<BroadphaseComponent>();
        while (query.MoveNext(out var owner, out var broad))
        {
            foreach (var tree in new[] { broad.SundriesTree, broad.StaticSundriesTree })
            {
                foreach (var uid in tree)
                {
                    if (!em.TryGetComponent(uid, out TransformComponent? xform))
                        found.Add($"{em.ToPrettyString(owner)} holds deleted {uid}");
                    else if (lookup.GetCurrentBroadphase(xform)?.Owner != owner)
                        found.Add($"{em.ToPrettyString(owner)} holds {em.ToPrettyString(uid)}, which is not on it");
                }
            }
        }

        return found;
    }
}
