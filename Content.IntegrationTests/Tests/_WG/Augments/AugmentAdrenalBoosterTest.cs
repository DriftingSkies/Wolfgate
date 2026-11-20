using System.Linq;
using Content.Server._WG.Augments;
using Content.Server.Atmos.Components;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Augments;
using Content.Shared._WG.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Stunnable;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// The adrenal booster's second wind: a stamina stun ends early with stamina restored, then the booster rests; a slip
/// doesn't spend it; and a pain shock's adrenaline lasts twice as long.
/// </summary>
[TestFixture]
[TestOf(typeof(WGAdrenalBoosterSystem))]
public sealed class AugmentAdrenalBoosterTest
{
    [Test]
    public async Task SecondWindTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var humanoids = server.System<SharedHumanoidAppearanceSystem>();
        var stamina = server.System<StaminaSystem>();
        var stun = server.System<SharedStunSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var map = await pair.CreateTestMap();

        EntityUid wearer = default, control = default, slipper = default, shocked = default;
        await server.WaitAssertion(() =>
        {
            EntityUid Spawn(float x, bool booster)
            {
                var mob = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(x, 0f)));
                // The test map is airless, and low pressure deals Blunt.
                entities.RemoveComponent<BarotraumaComponent>(mob);
                if (booster)
                {
                    humanoids.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Human")
                        .WithAugmentImplants(["WGAugmentAdrenalBooster"]));
                }
                return mob;
            }

            wearer = Spawn(0f, true);
            control = Spawn(3f, false);
            slipper = Spawn(6f, true);
            shocked = Spawn(9f, true);
            Assert.That(entities.HasComponent<WGAdrenalBoosterComponent>(wearer), "The booster wasn't fitted.");
            Assert.That(entities.HasComponent<VigorComponent>(wearer), "The booster lost its stamina bonus.");

            stamina.TakeStaminaDamage(wearer, 200f);
            stamina.TakeStaminaDamage(control, 200f);
            stun.TryKnockdown(slipper, TimeSpan.FromSeconds(4), true);
            Assert.That(entities.HasComponent<KnockedDownComponent>(wearer), "The stamina stun didn't drop the wearer.");
        });

        // Not at once: the stun lands first.
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<StunnedComponent>(wearer), "The booster cancelled the stun outright."));

        // A second and a half in, the wearer is back up with full stamina; the plain body and the slip are still down.
        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            var stam = entities.GetComponent<StaminaComponent>(wearer);
            Assert.That(entities.HasComponent<StunnedComponent>(wearer), Is.False, "The wearer is still stunned.");
            Assert.That(entities.HasComponent<KnockedDownComponent>(wearer), Is.False, "The wearer is still knocked down.");
            Assert.That(stam.Critical, Is.False);
            Assert.That(stam.StaminaDamage, Is.Zero, "The surge didn't restore stamina.");
            Assert.That(entities.HasComponent<StunnedComponent>(control), "A body without the booster got up early.");
            Assert.That(entities.HasComponent<KnockedDownComponent>(slipper), "A slip spent the booster.");
            Assert.That(entities.GetComponent<WGAdrenalBoosterComponent>(slipper).Spent, Is.False);

            // Spent, it lets the next stun run its course.
            stamina.TakeStaminaDamage(wearer, 200f);
        });
        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<StunnedComponent>(wearer), "The booster fired again during its cooldown."));

        // A pain shock's adrenaline window is twice the usual.
        await server.WaitAssertion(() =>
        {
            var torso = server.System<SharedBodySystem>().GetBodyChildren(shocked)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            Assert.That(server.System<PainSystem>().SetPain(torso, FixedPoint2.New(200)));
            var ends = entities.GetComponent<PainShockTargetComponent>(shocked).AdrenalineEnds;
            Assert.That(ends, Is.Not.Null, "No pain shock.");
            Assert.That((ends!.Value - timing.CurTime).TotalSeconds,
                Is.EqualTo(cfg.GetCVar(WolfmedCVars.AdrenalineSeconds) * 2f).Within(0.1));
        });

        await server.WaitPost(() =>
        {
            foreach (var mob in new[] { wearer, control, slipper, shocked })
                entities.DeleteEntity(mob);
        });
        await pair.CleanReturnAsync();
    }
}
