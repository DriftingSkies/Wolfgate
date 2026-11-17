#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WG.Wolfmed.Life;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Wolfmed;

/// <summary>
/// What <see cref="WolfmedGameTest"/> is for. The barotrauma timer rolls over once a second, so three seconds cover
/// every phase a test can start at: a body on the Wolfmed test map is untouched after them, and one on the pair's
/// bare map has been hit.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedGameTest))]
public sealed class WolfmedTestMapTest : WolfmedGameTest
{
    [Test]
    public async Task TestMapLeavesABodyAloneTest()
    {
        var air = await CreateTestMap();
        var bare = await CreateVacuumTestMap();
        EntityUid safe = default, exposed = default;

        await Server.WaitPost(() =>
        {
            safe = SEntMan.SpawnEntity("MobHuman", air.GridCoords);
            exposed = SEntMan.SpawnEntity("MobHuman", bare.GridCoords);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            var pressure = SEntMan.GetComponent<DamageableComponent>(exposed).Damage.DamageDict;
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<DamageableComponent>(safe).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                    "the test map's air hurt the body.");
                Assert.That(Wounds(safe), Is.Zero, "the body on the test map is wounded.");
                Assert.That(SEntMan.System<WolfmedBreathingSystem>().IsSuffocating(safe), Is.False,
                    "the body on the test map cannot breathe.");

                // The control: the same three seconds on the pair's own map, two or three rollovers of Blunt 2.
                Assert.That(pressure.GetValueOrDefault("Blunt"), Is.GreaterThanOrEqualTo(FixedPoint2.New(4)),
                    "the bare map dealt no pressure damage in three seconds, so this test proves nothing.");
                Assert.That(Wounds(exposed), Is.GreaterThan(0));
            });
        });
    }

    private int Wounds(EntityUid body) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .Sum(part => SEntMan.System<WoundSystem>().GetWounds(part.Id).Count());
}
