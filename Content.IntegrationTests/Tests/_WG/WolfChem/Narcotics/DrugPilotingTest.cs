using System.Numerics;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Server.Physics.Controllers;
using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared.Drunk;
using Content.Shared.StatusEffect;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// Flying high: stimulants sharpen the ship and their crash dulls it, opioids make it answer late and coast on after
/// the stick is let go, cannabis softens the brakes, drink makes the heading wander, and a sober pilot is untouched.
/// </summary>
[TestFixture]
[TestOf(typeof(WGDrugPilotingSystem))]
public sealed class DrugPilotingTest
{
    private const float Frame = 1f / 30f;

    private static GetShuttleInputsEvent Fly(IEntityManager entMan, EntityUid pilot, ShuttleInput? input)
    {
        var ev = new GetShuttleInputsEvent(Frame, pilot, input, true);
        entMan.EventBus.RaiseLocalEvent(pilot, ref ev);
        return ev;
    }

    [Test]
    public async Task HighsShapeFlyingTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var piloting = server.System<WGDrugPilotingSystem>();
            var ahead = new ShuttleInput(new Vector2(0f, 1f), 0f, 0f);

            // Sober, with the component still on: nothing changes.
            var sober = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<WGIntoxicationComponent>(sober);
            var plain = Fly(entMan, sober, ahead);
            Assert.That(plain.Input, Is.EqualTo(ahead));
            Assert.That(plain.AngularMul, Is.EqualTo(1f));
            Assert.That(plain.AccelMul, Is.EqualTo(1f));

            // Stimulants: sharper, and duller in the crash.
            var fast = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var fastComp = entMan.EnsureComponent<WGIntoxicationComponent>(fast);
            fastComp.Levels["WGCrystal"] = 10f;
            var sharp = Fly(entMan, fast, ahead);
            Assert.That(sharp.AngularMul, Is.GreaterThan(1f));
            Assert.That(sharp.AccelMul, Is.GreaterThan(1f));
            Assert.That(sharp.Input, Is.EqualTo(ahead), "A stimulant high leaves the stick alone.");

            fastComp.Levels["WGCrystal"] = 0f;
            fastComp.Crashes["WGCrystal"] = 60f;
            var dull = Fly(entMan, fast, ahead);
            Assert.That(dull.AngularMul, Is.LessThan(1f / sharp.AngularMul), "The crash costs more than the high gave.");
            Assert.That(dull.AccelMul, Is.LessThan(1f / sharp.AccelMul));

            // Opioids: the ship trails the stick, and coasts on after it is let go.
            var slow = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<WGIntoxicationComponent>(slow).Levels["WGHeroin"] = 10f;
            var late = Fly(entMan, slow, ahead);
            Assert.That(late.Input, Is.Not.Null);
            Assert.That(late.Input!.Value.Strafe.Y, Is.InRange(0.01f, 0.5f), "The first frame of thrust is only part of the stick.");

            for (var i = 0; i < 60; i++)
            {
                late = Fly(entMan, slow, ahead);
            }

            Assert.That(late.Input!.Value.Strafe.Y, Is.GreaterThan(0.9f), "Held long enough, the ship catches up.");
            var coast = Fly(entMan, slow, null);
            Assert.That(coast.Input, Is.Not.Null, "Letting go doesn't stop the thrust at once.");
            Assert.That(coast.Input!.Value.Strafe.Y, Is.GreaterThan(0.5f));

            for (var i = 0; i < 300; i++)
            {
                coast = Fly(entMan, slow, null);
            }

            Assert.That(coast.Input, Is.Null, "The stick comes to rest.");

            // Cannabis: soft brakes and a slower turn.
            var mellow = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<WGIntoxicationComponent>(mellow).Levels["WGCannabis"] = 10f;
            var braking = Fly(entMan, mellow, new ShuttleInput(Vector2.Zero, 0f, 1f));
            Assert.That(braking.Input!.Value.Brakes, Is.EqualTo(0.65f).Within(0.001f));
            Assert.That(braking.AngularMul, Is.LessThan(1f));

            // Drink: the heading wanders while flying, and a parked ship stays parked.
            var drunk = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(server.System<StatusEffectsSystem>().TryAddStatusEffect<DrunkComponent>(drunk,
                SharedDrunkSystem.DrunkKey, TimeSpan.FromSeconds(WGAlcoholSystem.HeavySeconds + 30f), true), Is.True);
            Assert.That(piloting.GetHandling(drunk).Wander, Is.GreaterThan(0f));
            Assert.That(Fly(entMan, drunk, null).Input, Is.Null, "No input stays no input.");

            var widest = 0f;
            for (var i = 0; i < 300; i++)
            {
                widest = MathF.Max(widest, MathF.Abs(Fly(entMan, drunk, ahead).Input!.Value.Rotation));
            }

            Assert.That(widest, Is.GreaterThan(0.05f), "A drunk pilot's heading wanders.");
        });

        await pair.CleanReturnAsync();
    }
}
