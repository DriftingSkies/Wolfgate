using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.Construction;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Construction;

/// <summary>The APC recipe and the RCD refuse a wall face that an APC or APC frame already takes.</summary>
[TestFixture]
[TestOf(typeof(WGApcFaceSystem))]
public sealed class ApcFaceFreeTest
{
    private static readonly ProtoId<ConstructionPrototype> Recipe = "APC";
    private static readonly ProtoId<RCDPrototype> RcdApc = "BaseAPC";

    [Test]
    public async Task SecondApcOnSameFaceIsRefused()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var xforms = entMan.System<SharedTransformSystem>();

        await server.WaitAssertion(() =>
        {
            var recipe = server.ProtoMan.Index(Recipe);
            Assert.That(recipe.Conditions.OfType<WGApcFaceFree>(), Is.Not.Empty, "The APC recipe lost its condition.");

            var condition = new WGApcFaceFree();
            Assert.That(condition.Condition(EntityUid.Invalid, map.GridCoords, Direction.East), Is.True);

            foreach (var proto in new[] { "APCFrame", "APCConstructed", "APCHyperCapacity" })
            {
                var apc = entMan.SpawnEntity(proto, map.GridCoords);
                xforms.SetLocalRotation(apc, Direction.East.ToAngle());

                Assert.Multiple(() =>
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(apc).Anchored, $"{proto} did not anchor.");
                    Assert.That(condition.Condition(EntityUid.Invalid, map.GridCoords, Direction.East), Is.False,
                        $"A second APC was allowed on the face of a {proto}.");
                    Assert.That(condition.Condition(EntityUid.Invalid, map.GridCoords, Direction.West), Is.True,
                        $"A {proto} blocked the opposite face.");
                });

                entMan.DeleteEntity(apc);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The RCD allows several builds at once on a tile and can be turned while they run, so two are started on one
    /// face and the RCD is turned away before they finish.
    /// </summary>
    [Test]
    public async Task RcdBuildsOneApcPerFace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var maps = entMan.System<SharedMapSystem>();
        EntityUid user = default;
        EntityUid rcd = default;
        RCDComponent comp = default!;

        // RCDComponent is write-protected outside RCDSystem; a player sets these from the RCD menu and by rotating.
        void Set(string property, object value)
        {
            typeof(RCDComponent).GetProperty(property)!.SetValue(comp, value);
        }

        void Use(Direction direction)
        {
            Set(nameof(RCDComponent.ConstructionDirection), direction);
            entMan.EventBus.RaiseLocalEvent(rcd, new AfterInteractEvent(user, rcd, null, map.GridCoords, true));
        }

        Direction[] Faces()
        {
            var faces = new List<Direction>();
            var anchored = maps.GetAnchoredEntities(map.Grid.Owner, map.Grid.Comp, Vector2i.Zero);
            while (anchored.MoveNext(out var uid))
            {
                if (entMan.GetComponent<MetaDataComponent>(uid.Value).EntityPrototype?.ID == "BaseAPC")
                    faces.Add(entMan.GetComponent<TransformComponent>(uid.Value).LocalRotation.GetCardinalDir());
            }

            return faces.ToArray();
        }

        await server.WaitAssertion(() =>
        {
            user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<SharedGodmodeSystem>().EnableGodmode(user);
            rcd = entMan.SpawnEntity("RCD", map.GridCoords);
            comp = entMan.GetComponent<RCDComponent>(rcd);
            Set(nameof(RCDComponent.ProtoId), RcdApc);
            entMan.System<RCDSystem>().UpdateCachedPrototype(rcd, comp);

            Use(Direction.East);
            Use(Direction.East);
            Set(nameof(RCDComponent.ConstructionDirection), Direction.West);
        });
        await server.WaitRunTicks(150);
        await server.WaitAssertion(() =>
        {
            Assert.That(Faces(), Is.EqualTo(new[] { Direction.East }), "Two builds queued on one face.");
            Use(Direction.East);
        });
        await server.WaitRunTicks(150);
        await server.WaitAssertion(() =>
        {
            Assert.That(Faces(), Is.EqualTo(new[] { Direction.East }), "A build on a taken face.");
            Use(Direction.West);
        });
        await server.WaitRunTicks(150);
        await server.WaitAssertion(() =>
        {
            Assert.That(Faces(), Is.EquivalentTo(new[] { Direction.East, Direction.West }), "The other face stayed free.");
        });

        await pair.CleanReturnAsync();
    }
}
