using System.Collections.Generic;
using Content.Server._WG.Augments;
using Content.Server.Physics.Controllers;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Augments;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Projectiles;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// The reflex booster: its body flies ships better, can slip a bullet, and feels more pain, and it takes the head's one
/// implant slot, so it can't share a head with the pain inhibitor.
/// </summary>
[TestFixture]
[TestOf(typeof(WGReflexBoosterServerSystem))]
public sealed class AugmentReflexBoosterTest
{
    private static readonly ProtoId<WGAugmentPrototype> Booster = "WGAugmentReflexBooster";

    [Test]
    public async Task ReflexBoosterTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var humanoids = server.System<SharedHumanoidAppearanceSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // One implant to a head: the booster or the pain inhibitor, not both.
            var head = WGAugmentRules.ValidateImplants("Human", new Dictionary<WGAugmentSlot, WGAugmentChoice>(),
                [Booster, "WGAugmentPainEditor"], new ProtoId<TraitPrototype>[0], proto, factory);
            Assert.That(head, Has.Count.EqualTo(1), "The head took two implants.");

            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            var plain = entities.SpawnEntity("MobHuman", map.GridCoords);
            humanoids.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugmentImplants([Booster]));
            Assert.That(entities.TryGetComponent<WGReflexBoosterComponent>(mob, out var booster), "The booster wasn't fitted.");

            // Sharper flying.
            (float Accel, float Angular) Handling(EntityUid body)
            {
                var ev = new GetShuttleInputsEvent(0.1f, EntityUid.Invalid);
                entities.EventBus.RaiseLocalEvent(body, ref ev);
                return (ev.AccelMul, ev.AngularMul);
            }

            Assert.That(Handling(plain), Is.EqualTo((1f, 1f)));
            Assert.That(Handling(mob).Accel, Is.EqualTo(booster!.ShipHandling).Within(1e-4));
            Assert.That(Handling(mob).Angular, Is.EqualTo(booster.ShipHandling).Within(1e-4));

            // More pain from the same wound.
            float PainGain(EntityUid body)
            {
                var ev = new ModifyPainGainEvent(1f);
                entities.EventBus.RaiseLocalEvent(body, ref ev);
                return ev.Multiplier;
            }

            Assert.That(PainGain(mob), Is.EqualTo(PainGain(plain) * booster.PainMultiplier).Within(1e-4));

            // A bullet it slips flies on; one it doesn't hits.
            var bullet = entities.SpawnEntity("BulletMinigun", MapCoordinates.Nullspace);
            var projectile = entities.GetComponent<ProjectileComponent>(bullet);
            bool Dodges(float chance)
            {
                booster.DodgeChance = chance;
                var ev = new ProjectileReflectAttemptEvent(bullet, projectile, false);
                entities.EventBus.RaiseLocalEvent(mob, ref ev);
                return ev.Cancelled;
            }

            Assert.That(Dodges(1f), "A certain dodge didn't happen.");
            Assert.That(Dodges(0f), Is.False);

            entities.DeleteEntity(bullet);
            entities.DeleteEntity(mob);
            entities.DeleteEntity(plain);
        });
        await pair.CleanReturnAsync();
    }
}
