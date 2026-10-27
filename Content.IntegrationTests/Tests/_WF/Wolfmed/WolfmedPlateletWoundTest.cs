#nullable enable
using System.Linq;
using Content.Server._Mono.Traits.Physical;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Platelet Factories healed the damage but left the wounds open at minor, with nothing left to close them.
/// </summary>
[TestFixture]
[TestOf(typeof(PlateletFactoriesSystem))]
public sealed class WolfmedPlateletWoundTest : WolfmedGameTest
{
    /// <summary>The trait closes a bruise whose part shows no damage left.</summary>
    [Test]
    public async Task PlateletsCloseAWoundWithNoDamageLeftTest()
    {
        var map = await CreateTestMap();
        EntityUid torso = default;
        EntityUid bruise = default;

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            torso = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            bruise = SEntMan.System<WoundSystem>().CreateOrMergeWound(torso, "BluntWound", 10)!.Value;
            Assert.That(SEntMan.GetComponent<DamageableComponent>(body).TotalDamage, Is.EqualTo(FixedPoint2.Zero));

            var platelets = SEntMan.AddComponent<PlateletFactoriesComponent>(body);
            platelets.DamagePerInterval = new DamageSpecifier { DamageDict = { ["Blunt"] = -1.5 } };
        });

        await Server.WaitRunTicks(Server.Timing.TickRate * 12);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<WoundSystem>().GetWounds(torso).Any(open => open.Owner == bruise), Is.False,
                "the bruise closes."));
    }
}
