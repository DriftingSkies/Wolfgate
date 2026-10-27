#nullable enable
using System.Linq;
using System.Numerics;
using Content.Shared._Shitmed.Body.Part;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Client.GameObjects;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.MarkingToggles;

/// <summary>
/// Feedback #170: a marking hidden while a viewer is out of PVS range showed again once the viewer came near.
/// </summary>
[TestFixture]
[TestOf(typeof(Server._WF.MarkingToggles.MarkingToggleSystem))]
public sealed class MarkingTogglePvsTest
{
    private const int MaxWaitTicks = 90;

    private static readonly CustomMarking Custom = new(new string('7', CustomMarkingRules.HashLength), CustomMarkingPlacement.Skin);

    /// <summary>The viewer never saw the body before its markings were hidden.</summary>
    [TestCase("MobHuman", "HumanHairAfro")]
    [TestCase("MobIPC", "RobotAntennaTv")]
    public async Task HiddenBeforeFirstSight(string mob, string marking)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var (body, _, near) = await Setup(pair, mob, marking, onViewerMap: false);

        await pair.Server.WaitPost(() => Hide(pair.Server.EntMan, body, marking));
        await pair.RunTicksSync(5);
        await pair.Server.WaitPost(() => pair.Server.System<SharedTransformSystem>().SetCoordinates(body, near));

        await AssertClientSeesHidden(pair, body, marking);
        await pair.CleanReturnAsync();
    }

    /// <summary>The viewer saw the body shown, lost it from PVS, and meets it again after it hid its markings.</summary>
    [TestCase("MobHuman", "HumanHairAfro")]
    [TestCase("MobIPC", "RobotAntennaTv")]
    public async Task HiddenWhileOutOfView(string mob, string marking)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var (body, farMap, near) = await Setup(pair, mob, marking, onViewerMap: true);

        var net = pair.Server.EntMan.GetNetEntity(body);
        await WaitFor(pair, () => pair.Client.EntMan.TryGetEntity(net, out var c) && MarkingVisible(pair.Client.EntMan, c.Value, marking),
            "the client to see the body with its marking shown");

        var xforms = pair.Server.System<SharedTransformSystem>();
        await pair.Server.WaitPost(() => xforms.SetCoordinates(body, farMap));
        await WaitFor(pair, () => !InView(pair, net), "the body to leave the client's view");

        await pair.Server.WaitPost(() => Hide(pair.Server.EntMan, body, marking));
        await pair.RunTicksSync(5);
        await pair.Server.WaitPost(() => xforms.SetCoordinates(body, near));

        await AssertClientSeesHidden(pair, body, marking);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Puts the player in a viewer body and spawns a second body with hair and a custom marking, beside the viewer or
    /// on a map of its own.
    /// </summary>
    private static async Task<(EntityUid Body, EntityCoordinates FarMap, EntityCoordinates Near)> Setup(Pair.TestPair pair, string mob, string marking, bool onViewerMap)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var body = EntityUid.Invalid;
        var farMap = EntityCoordinates.Invalid;
        var near = map.GridCoords.Offset(new Vector2(1, 0));

        // Test pairs run without PVS; the bug needs the client to get the body only when it comes near.
        // TestPair reverts it when the pair is returned.
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CVars.NetPVS, true);

            var mindSys = entMan.System<SharedMindSystem>();
            mindSys.WipeMind(session.ContentData()?.Mind);
            var viewer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, viewer);

            var far = entMan.System<SharedMapSystem>().CreateMap(out _);
            farMap = new EntityCoordinates(far, Vector2.Zero);

            body = entMan.SpawnEntity(mob, onViewerMap ? near : farMap);
            entMan.System<SharedHumanoidAppearanceSystem>().AddMarking(body, marking, Color.Red, forced: true);
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(body);
            humanoid.CustomMarkings.Add(Custom);
            entMan.Dirty(body, humanoid);

            // As a loaded profile does: each limb keeps a copy of the markings on it.
            var bodySys = entMan.System<SharedBodySystem>();
            foreach (var part in bodySys.GetBodyChildren(body))
            {
                entMan.EnsureComponent<BodyPartAppearanceComponent>(part.Id);
            }
        });
        await pair.RunTicksSync(5);

        return (body, farMap, near);
    }

    /// <summary>Hides the marking and the custom marking the way MarkingToggleSystem does on a player's request.</summary>
    private static void Hide(IEntityManager entMan, EntityUid body, string marking)
    {
        var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(body);
        humanoid.MarkingSet.Markings.Values.SelectMany(l => l).Single(m => m.MarkingId == marking).Visible = false;
        humanoid.HiddenCustomMarkings.Add(Custom);
        entMan.Dirty(body, humanoid);
    }

    private static async Task AssertClientSeesHidden(Pair.TestPair pair, EntityUid body, string marking)
    {
        var net = pair.Server.EntMan.GetNetEntity(body);
        await WaitFor(pair, () => InView(pair, net), "the body to come into the client's view");
        await pair.RunTicksSync(5);

        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var clientBody = client.EntMan.GetEntity(net);
            Assert.Multiple(() =>
            {
                Assert.That(client.EntMan.GetComponent<HumanoidAppearanceComponent>(clientBody).MarkingSet.Markings.Values
                    .SelectMany(l => l).Count(m => m.MarkingId == marking), Is.EqualTo(1),
                    "the client holds one copy of the marking");
                Assert.That(MarkingVisible(client.EntMan, clientBody, marking), Is.False, "the client's copy of the marking is hidden");
                Assert.That(LayerVisible(pair, clientBody, marking), Is.False, "the marking is not drawn");
                Assert.That(client.EntMan.GetComponent<HumanoidAppearanceComponent>(clientBody).HiddenCustomMarkings,
                    Does.Contain(Custom), "the client's copy of the custom marking is hidden");
            });
        });
    }

    /// <summary>Whether the client has the body on the viewer's map, as PVS sends it there.</summary>
    private static bool InView(Pair.TestPair pair, NetEntity net)
    {
        var entMan = pair.Client.EntMan;
        return entMan.TryGetEntity(net, out var uid)
               && entMan.GetComponent<TransformComponent>(uid.Value).MapID != MapId.Nullspace
               && entMan.GetComponent<TransformComponent>(uid.Value).MapID
               == entMan.GetComponent<TransformComponent>(pair.Client.Session!.AttachedEntity!.Value).MapID;
    }

    private static bool MarkingVisible(IEntityManager entMan, EntityUid body, string marking)
    {
        var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(body);
        return humanoid.MarkingSet.Markings.Values.SelectMany(l => l).Any(m => m.MarkingId == marking && m.Visible);
    }

    private static bool LayerVisible(Pair.TestPair pair, EntityUid body, string marking)
    {
        var proto = pair.Client.ResolveDependency<MarkingManager>().Markings[marking];
        var state = ((SpriteSpecifier.Rsi) proto.Sprites[0]).RsiState;
        var sprites = pair.Client.System<SpriteSystem>();
        var sprite = pair.Client.EntMan.GetComponent<SpriteComponent>(body);
        Assert.That(sprites.LayerMapTryGet((body, sprite), $"{marking}-{state}", out var index, false), Is.True,
            "the marking has a sprite layer");
        return sprite[index].Visible;
    }

    private static async Task WaitFor(Pair.TestPair pair, Func<bool> condition, string what)
    {
        for (var i = 0; i < MaxWaitTicks && !condition(); i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.That(condition(), Is.True, $"Timed out waiting for {what}.");
    }
}
