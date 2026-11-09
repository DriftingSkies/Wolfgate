#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Construction.Components;
using Content.Shared.Construction.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Power.Components;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>Outpost gizmos deploy from their pack onto the outpost floor and pack up again as they were.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostDeployTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFTestGizmo
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Battery
    maxCharge: 1000
    startingCharge: 0
  - type: ContainerContainer
    containers:
      gizmo_contents: !type:Container
  - type: WFRepackable
    pack: WFTestGizmoPack
  - type: StaticPrice
    price: 250

- type: entity
  id: WFTestGizmoPack
  components:
  - type: Item
  - type: WFDeployable
    machine: WFTestGizmo
  - type: StaticPrice
    price: 250
";

    [Test]
    public async Task DeployRepackKeepsState()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var transform = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();
        var hands = server.System<SharedHandsSystem>();
        var containers = server.System<SharedContainerSystem>();
        var battery = server.System<BatterySystem>();
        var pricing = server.System<PricingSystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var found, out var reason), Is.True, $"Founding was refused: {reason}.");
                var outpost = found!.Value;
                var grid = entMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(outpost);

                // Standing on the corner tile, facing north: the tile in front is free floor.
                transform.SetCoordinates(user, maps.GridTileToLocal(outpost, grid, new Vector2i(0, 0)));
                transform.SetWorldRotation(user, Direction.North.ToAngle());

                var pack = entMan.SpawnEntity("WFTestGizmoPack", entMan.GetComponent<TransformComponent>(user).Coordinates);
                Assert.That(hands.TryPickupAnyHand(user, pack), "Precondition: the pack is in hand.");
                Assert.That(outposts.TryDeploy(user, (pack, entMan.GetComponent<WFDeployableComponent>(pack)), out var machine, out reason), Is.True,
                    $"Deploying was refused: {reason}.");
                var gizmo = machine!.Value;
                var xform = entMan.GetComponent<TransformComponent>(gizmo);
                Assert.That(xform.GridUid, Is.EqualTo(outpost));
                Assert.That(maps.TileIndicesFor(outpost, grid, xform.Coordinates), Is.EqualTo(new Vector2i(0, 1)));
                Assert.That(xform.Anchored, "The gizmo is anchored.");

                // Used: charged, and something put in it.
                battery.SetCharge(gizmo, 321f);
                var pen = entMan.SpawnEntity("Pen", xform.Coordinates);
                Assert.That(containers.Insert(pen, containers.GetContainer(gizmo, "gizmo_contents")));

                Assert.That(outposts.TryRepack(user, (gizmo, entMan.GetComponent<WFRepackableComponent>(gizmo)), out var repacked, out reason), Is.True,
                    $"Packing up was refused: {reason}.");
                Assert.That(entMan.GetComponent<TransformComponent>(gizmo).Anchored, Is.False);
                Assert.That(containers.IsEntityInContainer(gizmo), "The gizmo itself went into its pack.");
                Assert.That(pricing.GetPrice(repacked!.Value), Is.EqualTo(pricing.GetPrice(gizmo)),
                    "A pack holding its gizmo is worth the gizmo, not both.");

                Assert.That(outposts.TryDeploy(user, (repacked.Value, entMan.GetComponent<WFDeployableComponent>(repacked.Value)), out var again, out reason), Is.True,
                    $"Deploying again was refused: {reason}.");
                Assert.That(again, Is.EqualTo(gizmo), "The same gizmo came back out.");
                Assert.That(entMan.GetComponent<BatteryComponent>(gizmo).CurrentCharge, Is.EqualTo(321f).Within(0.5f), "The charge survived.");
                Assert.That(containers.GetContainer(gizmo, "gizmo_contents").ContainedEntities, Is.EquivalentTo(new[] { pen }), "The contents survived.");
                Assert.That(entMan.GetComponent<TransformComponent>(gizmo).Anchored, "The gizmo is anchored again.");

                // Facing off the outpost there is no floor to set it on.
                transform.SetWorldRotation(user, Direction.West.ToAngle());
                var spare = entMan.SpawnEntity("WFTestGizmoPack", entMan.GetComponent<TransformComponent>(user).Coordinates);
                Assert.That(outposts.TryDeploy(user, (spare, entMan.GetComponent<WFDeployableComponent>(spare)), out _, out reason), Is.False);
                Assert.That(reason, Is.EqualTo("wf-outpost-deploy-no-floor"));

                // A prefab wall stands square with the outpost whichever way it was set down, and packs up again.
                transform.SetWorldRotation(user, Direction.East.ToAngle());
                var panel = entMan.SpawnEntity("WFPrefabWallPanel", entMan.GetComponent<TransformComponent>(user).Coordinates);
                Assert.That(outposts.TryDeploy(user, (panel, entMan.GetComponent<WFDeployableComponent>(panel)), out var wall, out reason), Is.True,
                    $"The wall panel wasn't deployed: {reason}.");
                Assert.That(transform.GetWorldRotation(wall!.Value).Theta, Is.EqualTo(transform.GetWorldRotation(outpost).Theta).Within(0.001));
                Assert.That(outposts.TryRepack(user, (wall.Value, entMan.GetComponent<WFRepackableComponent>(wall.Value)), out _, out reason), Is.True, reason);

                // The console packs up only once the outpost is back as it was founded, and gives the founding back.
                var console = entMan.GetComponent<WFOutpostComponent>(outpost).Console;
                var consoleRepack = (console, entMan.GetComponent<WFRepackableComponent>(console));
                Assert.That(outposts.TryRepack(user, consoleRepack, out _, out reason), Is.False);
                Assert.That(reason, Is.EqualTo("wf-outpost-repack-console-built"));
                Assert.That(outposts.TryRepack(user, (gizmo, entMan.GetComponent<WFRepackableComponent>(gizmo)), out _, out reason), Is.True, reason);
                Assert.That(outposts.TryRepack(user, consoleRepack, out var kit, out reason), Is.True, $"The console wouldn't pack up: {reason}.");
                Assert.That(entMan.HasComponent<WFOutpostKitComponent>(kit!.Value));
                Assert.That(entMan.Deleted(outpost), "Packing up the console gives up the outpost.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A prefab wall's builder welds it fast with a few seconds' welding, and it can't be packed up; cutting the welds
    /// frees it, and the Pack up verb is offered again.
    /// </summary>
    [Test]
    public async Task WeldingFixesPrefabWalls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var transform = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();
        var hands = server.System<SharedHandsSystem>();
        var verbs = server.System<SharedVerbSystem>();
        var repackText = Robust.Shared.Localization.Loc.GetString("wf-outpost-repack-verb");

        EntityUid wall = default;
        EntityUid welder = default;
        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var found, out var reason), Is.True, $"Founding was refused: {reason}.");
                var outpost = found!.Value;
                var grid = entMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(outpost);
                transform.SetCoordinates(user, maps.GridTileToLocal(outpost, grid, new Vector2i(0, 0)));
                transform.SetWorldRotation(user, Direction.North.ToAngle());

                var panel = entMan.SpawnEntity("WFPrefabWallPanel", entMan.GetComponent<TransformComponent>(user).Coordinates);
                Assert.That(outposts.TryDeploy(user, (panel, entMan.GetComponent<WFDeployableComponent>(panel)), out var deployed, out reason), Is.True,
                    $"The wall panel wasn't deployed: {reason}.");
                wall = deployed!.Value;
                Assert.That(HasRepackVerb(wall), "A loose prefab wall offers Pack up.");

                welder = entMan.SpawnEntity("Welder", entMan.GetComponent<TransformComponent>(user).Coordinates);
                Assert.That(hands.TryPickupAnyHand(user, welder), "Precondition: the welder is in hand.");
                Assert.That(server.System<ItemToggleSystem>().TryActivate(welder, user), "Precondition: the welder is lit.");
            });

            wall = await Weld(wall, "WFPrefabWallFixed");
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(wall).Anchored, "The welded wall stands anchored.");
                Assert.That(HasRepackVerb(wall), Is.False, "A welded prefab wall doesn't pack up.");
            });

            wall = await Weld(wall, "WFPrefabWall");
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(wall).Anchored, "The cut wall stands anchored.");
                Assert.That(HasRepackVerb(wall), "A prefab wall with its welds cut offers Pack up again.");
                Assert.That(outposts.TryRepack(user, (wall, entMan.GetComponent<WFRepackableComponent>(wall)), out _, out var reason), Is.True, reason);
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();

        bool HasRepackVerb(EntityUid target)
        {
            return verbs.GetLocalVerbs(target, user, typeof(AlternativeVerb)).Any(v => v.Text == repackText);
        }

        // Welds the wall, checking it takes a few seconds and not less, and returns what it became.
        async Task<EntityUid> Weld(EntityUid target, string into)
        {
            await server.WaitAssertion(() =>
            {
                var interact = new InteractUsingEvent(user, welder, target, entMan.GetComponent<TransformComponent>(target).Coordinates);
                entMan.EventBus.RaiseLocalEvent(target, interact);
                Assert.That(interact.Handled, $"Nothing took the welder to {entMan.ToPrettyString(target)}.");
                Assert.That(entMan.GetComponent<DoAfterComponent>(user).DoAfters, Is.Not.Empty, "The welding started no do-after.");
            });

            await pair.RunSeconds(2f);
            await server.WaitAssertion(() => Assert.That(entMan.Deleted(target), Is.False, "The weld finished in under 2 seconds."));
            await pair.RunSeconds(5f);

            EntityUid result = default;
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.Deleted(target), "The welded structure wasn't replaced.");
                var coords = transform.GetMapCoordinates(user);
                foreach (var near in server.System<EntityLookupSystem>().GetEntitiesInRange(coords, 2f))
                {
                    if (entMan.GetComponent<MetaDataComponent>(near).EntityPrototype?.ID == into)
                        result = near;
                }

                Assert.That(result, Is.Not.EqualTo(default(EntityUid)), $"No {into} stands where the wall was.");
            });
            return result;
        }
    }

    /// <summary>
    /// Every outpost gizmo comes in a pack that deploys it, is worth what its pack is, and can't be taken apart into a
    /// machine board: it has no Machine, MachineBoard or Construction component.
    /// </summary>
    [Test]
    public async Task GizmoRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var pricing = server.System<PricingSystem>();

        await server.WaitAssertion(() =>
        {
            var checkedAny = false;
            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryGetComponent<WFRepackableComponent>(out var repack, factory))
                    continue;

                checkedAny = true;
                foreach (var forbidden in new[] { typeof(MachineComponent), typeof(MachineBoardComponent), typeof(ConstructionComponent) })
                {
                    Assert.That(proto.Components.ContainsKey(factory.GetComponentName(forbidden)), Is.False,
                        $"{proto.ID} has {forbidden.Name}, so it could be taken apart for its board.");
                }

                Assert.That(protos.TryIndex(repack.Pack, out var pack), Is.True, $"{proto.ID} packs into a missing {repack.Pack}.");
                Assert.That(pack!.TryGetComponent<WFDeployableComponent>(out var deploy, factory), Is.True, $"{repack.Pack} doesn't deploy.");
                if (pack.TryGetComponent<WFOutpostKitComponent>(out _, factory))
                    continue;

                Assert.That(deploy!.Machine.Id, Is.EqualTo(proto.ID), $"{repack.Pack} deploys {deploy.Machine}, not {proto.ID}.");
                Assert.That(pricing.GetEstimatedPrice(proto), Is.EqualTo(pricing.GetEstimatedPrice(pack)),
                    $"{proto.ID} isn't worth what its pack {pack.ID} is.");
            }

            Assert.That(checkedAny, "No outpost gizmo was checked.");
        });

        await pair.CleanReturnAsync();
    }
}
