using System.Collections.Generic;
using Content.Client._WF.LegStyle;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.LegStyle;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;

namespace Content.IntegrationTests.Tests._WF.LegStyle;

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
            var sprite = CEntMan.GetComponent<SpriteComponent>(CPlayer);
            Assert.That(sprite.LayerMapTryGet(Leg, out _), Is.True, "The leg marking isn't drawn.");
            Assert.That(sprite.LayerMapTryGet($"{Leg}-displacement", out _), Is.True, "The leg marking wasn't reshaped.");
            Assert.That(sprite.LayerMapTryGet($"{Foot}-displacement", out _), Is.True, "The foot marking wasn't reshaped.");
            Assert.That(sprite.LayerMapTryGet($"{Leg}-legmask", out _) && sprite.LayerMapTryGet($"{Leg}-footmask", out _), Is.True,
                "The leg marking isn't cut to its own leg, so a side view shows it on both.");
            Assert.That(sprite.LayerMapTryGet(Leg, out var leg) && sprite[leg] is SpriteComponent.Layer { ShaderPrototype: var shader }
                && shader == "WFLegMarkingDisplaced", Is.True, "The leg marking isn't drawn with the cutting shader.");
        });

        await Server.WaitPost(() => humanoids.LoadProfile(SPlayer, profile));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(CPlayer);
            Assert.That(sprite.LayerMapTryGet(Leg, out _), Is.True, "The leg marking went with the digitigrade legs.");
            Assert.That(sprite.LayerMapTryGet($"{Leg}-displacement", out _), Is.False, "Plantigrade legs kept the reshaping.");
            Assert.That(sprite.LayerMapTryGet($"{Foot}-displacement", out _), Is.False, "Plantigrade feet kept the reshaping.");
            Assert.That(sprite.LayerMapTryGet($"{Leg}-legmask", out _), Is.False, "Plantigrade legs kept the outline.");
        });
    }
}
