using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._White.Overlays;
using Content.Client.Overlays;
using Content.Shared._White.Overlays;
using Content.Shared._WG.Augments;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Flash.Components;
using Content.Shared.Overlays;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// The Tapetum eyes: a body that gets them gets thermal and night vision and the cycle action, and each use steps the
/// vision through every band, thermal or night vision, and then off, on the server and on the owner's client.
/// </summary>
[TestFixture]
[TestOf(typeof(WGThermalTintsSystem))]
public sealed class AugmentThermalEyesTest
{
    [Test]
    public async Task CycleTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid body = default;
        List<WGThermalTint> tints = default!;
        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, body);
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugments(
                new Dictionary<WGAugmentSlot, WGAugmentChoice> { [WGAugmentSlot.Eyes] = new("WGAugmentThermalTintEyes", null) });
            var ev = new WGApplyAugmentsEvent(profile);
            entities.EventBus.RaiseLocalEvent(body, ref ev);

            Assert.That(entities.TryGetComponent<WGThermalTintsComponent>(body, out var eyes), "The eyes gave no tints.");
            Assert.That(entities.TryGetComponent<ThermalVisionComponent>(body, out var thermal), "The eyes gave no thermal vision.");
            Assert.That(eyes!.ActionEntity, Is.Not.Null, "The eyes gave no cycle action.");
            Assert.That(thermal!.ToggleActionEntity, Is.Null, "The thermal vision kept its own toggle.");
            Assert.That(thermal.IsActive, Is.False);
            Assert.That(entities.TryGetComponent<NightVisionComponent>(body, out var night), "The eyes gave no night vision.");
            Assert.That(night!.Enabled, Is.False);
            Assert.That(night.ActionEntity, Is.Null, "The night vision has its own toggle.");
            Assert.That(entities.HasComponent<EyeProtectionComponent>(body));
            Assert.That(entities.HasComponent<FlashImmunityComponent>(body), Is.False, "The eyes are flash-proof.");
            Assert.That(eyes.Tints, Has.Count.GreaterThan(1));
            Assert.That(eyes.Tints[0].NightVision, Is.False, "The first band isn't thermal.");
            Assert.That(eyes.Tints.Skip(1).All(t => t.NightVision), "A later band isn't night vision.");
            tints = eyes.Tints;
        });
        await pair.RunTicksSync(5);

        var overlays = client.ResolveDependency<IOverlayManager>();
        var clientBody = pair.ToClientUid(body);

        async Task Cycle()
        {
            await server.WaitPost(() =>
                entities.EventBus.RaiseLocalEvent(body, new WGCycleThermalTintEvent { Performer = body }));
            await pair.RunTicksSync(5);
        }

        async Task AssertBand(int index)
        {
            var isThermal = index >= 0 && !tints[index].NightVision;
            var isNight = index >= 0 && tints[index].NightVision;
            await server.WaitAssertion(() =>
            {
                var thermal = entities.GetComponent<ThermalVisionComponent>(body);
                var night = entities.GetComponent<NightVisionComponent>(body);
                Assert.That(entities.GetComponent<WGThermalTintsComponent>(body).Index, Is.EqualTo(index));
                Assert.That(thermal.IsActive, Is.EqualTo(isThermal), $"Thermal is wrong at band {index}.");
                Assert.That(night.Enabled, Is.EqualTo(isNight), $"Night vision is wrong at band {index}.");
                if (isThermal)
                    Assert.That(thermal.Color, Is.EqualTo(tints[index].Color));
                if (isNight)
                    Assert.That(night.PhosphorColor, Is.EqualTo(tints[index].Color));
            });
            await client.WaitAssertion(() =>
            {
                var thermal = client.EntMan.GetComponent<ThermalVisionComponent>(clientBody);
                var night = client.EntMan.GetComponent<NightVisionComponent>(clientBody);
                Assert.That(thermal.IsActive, Is.EqualTo(isThermal), $"The client's thermal is wrong at band {index}.");
                Assert.That(overlays.HasOverlay<ThermalVisionOverlay>(), Is.EqualTo(isThermal), $"The thermal overlay is wrong at band {index}.");
                Assert.That(night.Enabled, Is.EqualTo(isNight), $"The client's night vision is wrong at band {index}.");
                Assert.That(overlays.HasOverlay<NightVisionOverlay>(), Is.EqualTo(isNight), $"The night overlay is wrong at band {index}.");
                if (isNight)
                {
                    Assert.That(night.PhosphorColor, Is.EqualTo(tints[index].Color), $"The client's phosphor is wrong at band {index}.");
                    // The phosphor is only drawn by the goggle shader.
                    Assert.That(overlays.TryGetOverlay<NightVisionOverlay>(out var drawn));
                    Assert.That(drawn!.GogglesEnabled, $"Band {index} draws no phosphor tint.");
                    Assert.That(drawn.PhosphorColor, Is.EqualTo(tints[index].Color));
                }
                if (!isThermal)
                    return;
                Assert.That(thermal.Color, Is.EqualTo(tints[index].Color));
                Assert.That(Vector3.Distance(thermal.Tint, tints[index].Tint), Is.LessThan(0.001f), $"The client's wash is wrong at band {index}.");
            });
        }

        await AssertBand(-1);
        for (var i = 0; i < tints.Count; i++)
        {
            await Cycle();
            await AssertBand(i);
        }

        await Cycle();
        await AssertBand(-1);
        await Cycle();
        await AssertBand(0);

        await server.WaitPost(() => entities.DeleteEntity(body));
        await pair.CleanReturnAsync();
    }
}
