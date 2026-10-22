#nullable enable
using System.Linq;
using System.Numerics;
using Content.Shared._WF.RMCCore.Actions;
using Content.Shared.Actions;
using Robust.Shared.Timing;
using Content.Shared._WF.Xenonids.Charge;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared._WF.Xenonids.Stomp;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The crusher's stomp knocks a neighbour down and its charge launches it across the floor.</summary>
[TestFixture]
public sealed class XenoCrusherTest
{
    [Test]
    public async Task StompAndChargeWork()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var xforms = entities.System<SharedTransformSystem>();

        EntityUid crusher = default, human = default;
        await server.WaitPost(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            for (var x = -2; x <= 10; x++)
            for (var y = -2; y <= 2; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
            crusher = entities.SpawnEntity("RMCXenoCrusher", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            human = entities.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 1.5f));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var plasma = entities.GetComponent<XenoPlasmaComponent>(crusher);
            entities.System<XenoPlasmaSystem>().SetPlasma((crusher, plasma), plasma.MaxPlasma);

            // go through the real action pipeline, as a click would
            var actions = entities.System<SharedActionsSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var stompAction = entities.System<SharedRMCActionsSystem>().GetActionsWithEvent<XenoStompActionEvent>(crusher).First();
            Assert.That(actions.ValidAction(stompAction.Comp), "The stomp action is usable.");
            var stomp = new XenoStompActionEvent();
            actions.PerformAction(crusher, null, stompAction, stompAction.Comp, stomp, timing.CurTime);
            Assert.That(stomp.Handled, "The stomp action is accepted.");
        });

        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<StunnedComponent>(human) || entities.HasComponent<KnockedDownComponent>(human),
                "The stomp knocks the neighbour down.");
        });

        Vector2 before = default;
        await server.WaitAssertion(() =>
        {
            before = xforms.GetWorldPosition(crusher);
            var actions = entities.System<SharedActionsSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var target = new EntityCoordinates(map.Grid, 6.5f, 0.5f);
            var chargeAction = entities.System<SharedRMCActionsSystem>().GetActionsWithEvent<XenoChargeActionEvent>(crusher).First();
            var world = entities.GetComponent<WorldTargetActionComponent>(chargeAction);
            Assert.That(actions.ValidateWorldTarget(crusher, target, (chargeAction.Owner, world)), "The charge accepts a tile six away.");
            var charge = new XenoChargeActionEvent { Target = target };
            actions.PerformAction(crusher, null, chargeAction, chargeAction.Comp, charge, timing.CurTime);
            Assert.That(charge.Handled, "The charge action is accepted.");
        });

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var after = xforms.GetWorldPosition(crusher);
            Assert.That(Vector2.Distance(before, after), Is.GreaterThan(1f), "The charge moves the crusher.");
        });

        await pair.CleanReturnAsync();
    }
}
