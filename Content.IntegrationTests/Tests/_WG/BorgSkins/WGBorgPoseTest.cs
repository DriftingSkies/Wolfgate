#nullable enable
using Content.Shared._WG.BorgSkins;
using Content.Shared.Actions;
using Content.Shared.Silicons.Borgs;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.BorgSkins;

/// <summary>A borg gets an action for each pose its skin lists, and the actions toggle the pose it holds.</summary>
[TestFixture]
[TestOf(typeof(WGBorgPoseSystem))]
public sealed class WGBorgPoseTest
{
    private const string Borg = "BorgChassisSelectable";
    private const string PosedSkin = "WGBorgSkinEngineeringC9T7O";
    private const string PlainSkin = "generic_default";

    [Test]
    public async Task SkinGrantsPoseActions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var borg = entMan.SpawnEntity(Borg, map.GridCoords);
            Assert.That(entMan.HasComponent<WGBorgPoseComponent>(borg), Is.False, "A borg with no skin has poses.");

            var selected = new WGBorgSubtypeSelectedEvent(PosedSkin);
            entMan.EventBus.RaiseLocalEvent(borg, ref selected);

            var offered = prototypes.Index<WGBorgSkinPrototype>(PosedSkin).Poses;
            Assert.That(offered, Is.Not.Empty);
            Assert.That(entMan.TryGetComponent(borg, out WGBorgPoseComponent? pose), "The skin granted no poses.");
            Assert.That(pose!.Actions, Has.Count.EqualTo(offered.Count), "The skin did not grant an action per pose.");
            foreach (var action in pose.Actions)
            {
                Assert.That(entMan.HasComponent<InstantActionComponent>(action));
            }

            var rest = new WGBorgPoseActionEvent { Pose = WGBorgPose.Rest, Performer = borg };
            entMan.EventBus.RaiseLocalEvent(borg, rest);
            Assert.That(pose.Pose, Is.EqualTo(WGBorgPose.Rest), "The action did not strike the pose.");

            var sit = new WGBorgPoseActionEvent { Pose = WGBorgPose.Sit, Performer = borg };
            entMan.EventBus.RaiseLocalEvent(borg, sit);
            Assert.That(pose.Pose, Is.EqualTo(WGBorgPose.Sit), "A second pose did not replace the first.");

            sit = new WGBorgPoseActionEvent { Pose = WGBorgPose.Sit, Performer = borg };
            entMan.EventBus.RaiseLocalEvent(borg, sit);
            Assert.That(pose.Pose, Is.EqualTo(WGBorgPose.None), "Using a held pose's action did not drop it.");

            var actions = pose.Actions.ToArray();
            selected = new WGBorgSubtypeSelectedEvent(PlainSkin);
            entMan.EventBus.RaiseLocalEvent(borg, ref selected);
            Assert.That(entMan.HasComponent<WGBorgPoseComponent>(borg), Is.False, "A plain skin kept the poses.");
            foreach (var action in actions)
            {
                Assert.That(entMan.Deleted(action) || entMan.IsQueuedForDeletion(action), "A pose action outlived its skin.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
