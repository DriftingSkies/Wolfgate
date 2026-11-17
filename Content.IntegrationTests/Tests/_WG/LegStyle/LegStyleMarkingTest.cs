using System.Collections.Generic;
using Content.Client._WG.LegStyle;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.LegStyle;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;

namespace Content.IntegrationTests.Tests._WG.LegStyle;

/// <summary>
/// A human on digitigrade legs has its leg and foot markings reshaped like clothing, and loses the reshaping when it
/// goes back to its own legs. Each marking is cut to its own leg.
/// </summary>
[TestOf(typeof(LegStyleSystem))]
public sealed class LegStyleMarkingTest : InteractionTest
{
    private const string Leg = "GauzeUpperLegLeft-gauze_upperleg_l";
    private const string Foot = "GauzeLowerLegLeft-gauze_lowerleg_l";

    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task DigitigradeMarkingsTest()
    {
        var humanoids = SEntMan.System<SharedHumanoidAppearanceSystem>();
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
        profile = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(new List<Marking>
        {
            new("GauzeUpperLegLeft", 1),
            new("GauzeLowerLegLeft", 1),
        }));

        await Server.WaitPost(() => humanoids.LoadProfile(SPlayer, profile.WithLegStance(LegStance.Digitigrade)));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var spriteUid = CPlayer;
            var sprites = CEntMan.System<SpriteSystem>();
            var sprite = CEntMan.GetComponent<SpriteComponent>(spriteUid);
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), Leg, out _, false), Is.True, "The leg marking isn't drawn.");
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), $"{Leg}-displacement", out _, false), Is.True, "The leg marking wasn't reshaped.");
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), $"{Foot}-displacement", out _, false), Is.True, "The foot marking wasn't reshaped.");
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), $"{Leg}-legmask", out _, false) && sprites.LayerMapTryGet((spriteUid, sprite), $"{Leg}-footmask", out _, false), Is.True,
                "The leg marking isn't cut to its own leg, so a side view shows it on both.");
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), Leg, out var leg, false) && sprite[leg] is SpriteComponent.Layer { ShaderPrototype: var shader }
                && shader == "WGLegMarkingDisplaced", Is.True, "The leg marking isn't drawn with the cutting shader.");
        });

        await Server.WaitPost(() => humanoids.LoadProfile(SPlayer, profile));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var spriteUid = CPlayer;
            var sprites = CEntMan.System<SpriteSystem>();
            var sprite = CEntMan.GetComponent<SpriteComponent>(spriteUid);
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), Leg, out _, false), Is.True, "The leg marking went with the digitigrade legs.");
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), $"{Leg}-displacement", out _, false), Is.False, "Plantigrade legs kept the reshaping.");
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), $"{Foot}-displacement", out _, false), Is.False, "Plantigrade feet kept the reshaping.");
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), $"{Leg}-legmask", out _, false), Is.False, "Plantigrade legs kept the outline.");
        });
    }
}
