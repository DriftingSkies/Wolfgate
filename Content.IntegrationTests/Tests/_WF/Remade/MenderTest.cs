#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Elites;
using Content.Server._WF.Remade.Limbs;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using static Content.IntegrationTests.Tests._WF.Remade.WFRemadeDirectorTest;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The mender: dismembered, it drags its limbs back and gets up, unless a limb is burnt or carried off.</summary>
[TestFixture]
public sealed class MenderTest
{
    private const string Mender = "WFRemadeMender";

    /// <summary>Spawns a mender and cuts off both arms and the left leg, which puts it down.</summary>
    private static async Task<EntityUid> CutDown(TestPair pair, TestMapData map)
    {
        var entMan = pair.Server.EntMan;
        EntityUid mender = default;
        await pair.Server.WaitPost(() =>
        {
            LayDeck(pair, map);
            mender = entMan.SpawnEntity(Mender, At(map, 0, 0));
        });

        await pair.RunTicksSync(5);
        await pair.Server.WaitPost(() =>
        {
            foreach (var limb in new[] { "l_arm", "r_arm", "l_leg" })
                Assert.That(entMan.System<WFRemadeLimbsSystem>().TrySever(mender, limb), Is.True, $"{limb} wouldn't come off.");
        });

        // The test map has no gravity, so a flung limb would drift for ever: lay them out still, a step or two off.
        await pair.RunSeconds(0.5f);
        await pair.Server.WaitPost(() =>
        {
            var spots = new[] { At(map, 2, 0), At(map, -2, 0), At(map, 0, 2) };
            var items = LimbItems(entMan, mender);
            for (var i = 0; i < items.Count; i++)
            {
                entMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(items[i], Vector2.Zero);
                entMan.System<SharedTransformSystem>().SetCoordinates(items[i], spots[i % spots.Length]);
            }
        });

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(mender).Severed, Has.Count.EqualTo(3), "three limbs didn't come off.");
            Assert.That(entMan.System<MobStateSystem>().IsDead(mender), Is.True, "the mender stood with three limbs off.");
            Assert.That(LimbItems(entMan, mender), Has.Count.EqualTo(3), "the severed limbs aren't lying on the floor as its own.");
        });
        return mender;
    }

    private static List<EntityUid> LimbItems(IEntityManager entMan, EntityUid mender)
    {
        var items = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<WFSeveredLimbComponent>();
        while (query.MoveNext(out var uid, out var limb))
        {
            if (limb.Source == mender)
                items.Add(uid);
        }

        return items;
    }

    [Test]
    public async Task AMenderDragsItsLimbsBackAndGetsUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mender = await CutDown(pair, map);
        EntityUid far = default;

        // One a few metres off, so it has to be dragged.
        await server.WaitPost(() =>
        {
            far = LimbItems(entMan, mender).First();
            entMan.System<SharedTransformSystem>().SetCoordinates(far, At(map, 4, 0));
        });

        // It lies still for a moment before the tentacles come out.
        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<WFReclaimTentacleComponent>(far), Is.False, "the tentacles came out before it had lain still."));

        await pair.RunSeconds(1.8f);
        await server.WaitAssertion(() =>
        {
            var transforms = entMan.System<SharedTransformSystem>();
            var reach = entMan.GetComponent<WFReclaimComponent>(mender);
            var rises = entMan.GetComponent<WFRisesComponent>(mender);
            Assert.That(entMan.HasComponent<WFReclaimTentacleComponent>(far), Is.True,
                $"no tentacle reached the far limb: limb at {transforms.GetWorldPosition(far)}, mender at {transforms.GetWorldPosition(mender)}, "
                + $"reaching {reach.Reaching}, reach at {reach.ReachAt}, out of reach [{string.Join(",", reach.OutOfReach.Keys)}], "
                + $"rise at {rises.RiseAt}, final {rises.Final}, reattached {reach.Reattached}, "
                + $"severed [{string.Join(",", entMan.GetComponent<WFRemadeLimbsComponent>(mender).Severed)}], far limb {entMan.GetComponent<WFSeveredLimbComponent>(far).Limb}.");
            var position = entMan.System<SharedTransformSystem>().GetWorldPosition(far);
            Assert.That(position.X, Is.LessThan(4.1f), "the far limb isn't being dragged in.");
        });

        var reclaim = entMan.GetComponent<WFReclaimComponent>(mender);
        await RunUntil(pair, () => !entMan.System<MobStateSystem>().IsDead(mender), 12);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(mender), Is.False, "the mender was deleted.");
            Assert.That(reclaim.Reattached, Is.EqualTo(3),
                $"it didn't take all three limbs back: state {entMan.GetComponent<Content.Shared.Mobs.Components.MobStateComponent>(mender).CurrentState}, "
                + $"severed [{string.Join(",", entMan.GetComponent<WFRemadeLimbsComponent>(mender).Severed)}], limbs on the floor {LimbItems(entMan, mender).Count}, "
                + $"rises {entMan.GetComponent<WFRisesComponent>(mender).Rises}, final {entMan.GetComponent<WFRisesComponent>(mender).Final}.");
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(mender).Severed, Is.Empty);
            Assert.That(entMan.System<MobStateSystem>().IsAlive(mender), Is.True, "whole again, it didn't get up.");
            Assert.That(LimbItems(entMan, mender), Is.Empty, "a limb it took back is still on the floor.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CarryingALimbOutOfReachKeepsItDown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mender = await CutDown(pair, map);

        await server.WaitPost(() => entMan.System<SharedTransformSystem>().SetCoordinates(LimbItems(entMan, mender).First(), At(map, 15, 0)));
        await pair.RunSeconds(12f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFRisesComponent>(mender).Final, Is.True, "a limb out of reach didn't keep it down.");
            Assert.That(entMan.System<MobStateSystem>().IsDead(mender), Is.True);
        });

        await pair.RunSeconds(20f);
        await server.WaitAssertion(() => Assert.That(entMan.System<MobStateSystem>().IsDead(mender), Is.True, "it got up after all."));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABurntLimbKeepsItDown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mender = await CutDown(pair, map);
        EntityUid burnt = default;

        await server.WaitPost(() =>
        {
            burnt = LimbItems(entMan, mender).First();
            LimbsTest.Hit(pair, burnt, "Heat", 30, null);
        });

        await pair.RunSeconds(5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(burnt) || entMan.IsQueuedForDeletion(burnt), Is.True, "the limb didn't burn away.");
            Assert.That(entMan.GetComponent<WFRisesComponent>(mender).Final, Is.True, "a burnt limb didn't keep it down.");
        });
        await pair.CleanReturnAsync();
    }
}
