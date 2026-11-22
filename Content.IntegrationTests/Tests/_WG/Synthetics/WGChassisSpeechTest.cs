using Content.Server.Body.Components;
using Content.Server.Speech;
using Content.Shared.FixedPoint;
using Content.Shared.Speech.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Synthetics;

/// <summary>
/// A lightly damaged IPC speaks clearly: damage garbles nothing under its dead zone, and low fluid only stutters
/// from the Downed line.
/// </summary>
[TestFixture]
public sealed class WGChassisSpeechTest
{
    private static readonly EntProtoId Ipc = "MobIPC";

    [Test]
    public async Task ScratchedChassisSpeaksClearlyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var prototype = server.ProtoMan.Index(Ipc);
            Assert.That(prototype.TryGetComponent<DamagedSiliconAccentComponent>(out var accent, factory), Is.True);
            Assert.That(prototype.TryGetComponent<BloodstreamComponent>(out var blood, factory), Is.True);
            Assert.That(blood!.BloodlossThreshold, Is.EqualTo(0.5f).Within(1e-4), "Low fluid stutters above the Downed line.");

            Assert.That(accent!.DamageAtStartCorruption, Is.GreaterThan(FixedPoint2.Zero), "Any scratch garbles an IPC again.");

            var speaker = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var comp = entities.AddComponent<DamagedSiliconAccentComponent>(speaker);
            comp.DamageAtStartCorruption = accent.DamageAtStartCorruption;
            comp.DamageAtMaxCorruption = accent.DamageAtMaxCorruption;
            comp.MaxDamageCorruption = accent.MaxDamageCorruption;
            comp.OverrideChargeLevel = 1f;

            string Say(FixedPoint2 damage)
            {
                comp.OverrideTotalDamage = damage;
                var ev = new AccentGetEvent(speaker, new string('a', 400));
                entities.EventBus.RaiseLocalEvent(speaker, ev);
                return ev.Message;
            }

            var clean = new string('a', 400);
            Assert.That(Say(accent.DamageAtStartCorruption - FixedPoint2.New(1)), Is.EqualTo(clean),
                "A chassis under the dead zone garbled its speech.");
            Assert.That(Say(accent.DamageAtMaxCorruption), Is.Not.EqualTo(clean), "A wrecked chassis spoke clearly.");

            entities.DeleteEntity(speaker);
        });

        await pair.CleanReturnAsync();
    }
}
