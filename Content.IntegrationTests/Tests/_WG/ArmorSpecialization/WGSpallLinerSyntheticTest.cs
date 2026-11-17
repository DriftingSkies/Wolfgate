#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WG.Wolfmed;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WG.Wolfmed.Compat;
using Content.Shared._WG.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ArmorSpecialization;

/// <summary>A spall liner keeps rounds out of every covered synthetic part, limbs included.</summary>
[TestFixture]
public sealed class WGSpallLinerSyntheticTest : WolfmedGameTest
{
    private static readonly string[] Mobs = { "MobIPC", "MobSynth" };

    private static readonly string[] Suits = { "ClothingOuterHardsuitAshen", "WGRMCArmorM70Flak" };

    private static readonly TargetBodyPart[] Parts =
    {
        TargetBodyPart.Torso, TargetBodyPart.Head, TargetBodyPart.LeftArm, TargetBodyPart.RightLeg,
    };

    [Test]
    public async Task SpallLinerStopsSyntheticEmbedsTest([ValueSource(nameof(Mobs))] string mob, [ValueSource(nameof(Suits))] string suitId)
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var rules = SEntMan.System<WolfmedWoundRuleSystem>();
            rules.ForcedRoll = 0f;
            try
            {
                Assert.Multiple(() =>
                {
                    foreach (var part in Parts)
                    {
                        var bare = SEntMan.SpawnEntity(mob, map.GridCoords);
                        var lined = SEntMan.SpawnEntity(mob, map.GridCoords);
                        var suit = SEntMan.SpawnEntity(suitId, map.GridCoords);
                        Assert.That(SEntMan.System<InventorySystem>().TryEquip(lined, suit, "outerClothing", silent: true, force: true), Is.True);

                        foreach (var body in new[] { bare, lined })
                        {
                            var bullet = SEntMan.SpawnEntity("Bullet9x19mmFMJ", map.GridCoords);
                            var hit = new DamageSpecifier();
                            hit.DamageDict["Piercing"] = FixedPoint2.New(30);
                            SEntMan.System<DamageableSystem>().TryChangeDamage(body, hit, origin: bare, tool: bullet,
                                targetPart: part);
                        }

                        Assert.That(Embedded(bare), Is.GreaterThan(0), $"{mob} {suitId} {part}: a bare part kept nothing.");
                        Assert.That(Embedded(lined), Is.Zero, $"{mob} {suitId} {part}: the spall liner let a round lodge.");
                    }
                });
            }
            finally
            {
                rules.ForcedRoll = null;
            }
        });
    }

    [Test]
    public async Task SpallLinerStopsRobolimbEmbedsTest([ValueSource(nameof(Suits))] string suitId)
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var graph = SEntMan.System<SharedBodySystem>();
            var rules = SEntMan.System<WolfmedWoundRuleSystem>();
            rules.ForcedRoll = 0f;
            try
            {
                var bodies = new EntityUid[2];
                for (var i = 0; i < 2; i++)
                {
                    var body = bodies[i] = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
                    var torso = graph.GetBodyChildrenOfType(body, BodyPartType.Torso).Single().Id;
                    var old = graph.GetBodyChildrenOfType(body, BodyPartType.Leg, symmetry: BodyPartSymmetry.Left).Single().Id;
                    Assert.That(SEntMan.System<WolfmedBodySystem>().TryDetachPart(old), Is.True);
                    var leg = SEntMan.SpawnEntity("SpeedLeftLeg", map.GridCoords);
                    Assert.That(graph.AttachPart(torso, "left leg", leg), Is.True);
                }

                var suit = SEntMan.SpawnEntity(suitId, map.GridCoords);
                Assert.That(SEntMan.System<InventorySystem>().TryEquip(bodies[1], suit, "outerClothing", silent: true, force: true), Is.True);

                foreach (var body in bodies)
                {
                    var bullet = SEntMan.SpawnEntity("Bullet9x19mmFMJ", map.GridCoords);
                    var hit = new DamageSpecifier();
                    hit.DamageDict["Piercing"] = FixedPoint2.New(30);
                    SEntMan.System<DamageableSystem>().TryChangeDamage(body, hit, origin: bodies[0], tool: bullet,
                        targetPart: TargetBodyPart.LeftLeg);
                }

                Assert.Multiple(() =>
                {
                    Assert.That(Embedded(bodies[0]), Is.GreaterThan(0), "a bare robolimb kept nothing.");
                    Assert.That(Embedded(bodies[1]), Is.Zero, "the spall liner let a round lodge in a robolimb.");
                });
            }
            finally
            {
                rules.ForcedRoll = null;
            }
        });
    }

    private int Embedded(EntityUid body)
    {
        var embedded = SEntMan.System<WolfmedEmbeddedObjectSystem>();
        return SEntMan.System<SharedBodySystem>().GetBodyChildren(body).Sum(p => embedded.GetPartCount(p.Id));
    }
}
