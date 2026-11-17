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
/// A Vulpkanin on its own digitigrade legs has the DeltaV markings, drawn for plantigrade legs, reshaped, and the
/// Starlight ones, drawn for its legs, left alone. On plantigrade legs neither is reshaped.
/// </summary>
[TestOf(typeof(LegStyleSystem))]
public sealed class LegStyleVulpMarkingTest : InteractionTest
{
    private const string DeltaV = "VulpPointsFeet-points_feet";
    private const string Starlight = "PawSocksFeet-pawsocks_feet";

    protected override string PlayerPrototype => "MobVulpkanin";

    [Test]
    public async Task VulpkaninMarkingsTest()
    {
        var humanoids = SEntMan.System<SharedHumanoidAppearanceSystem>();
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Vulpkanin");
        profile = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(new List<Marking>
        {
            new("VulpPointsFeet", 1),
            new("PawSocksFeet", 1),
        }));

        await Server.WaitPost(() => humanoids.LoadProfile(SPlayer, profile));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var spriteUid = CPlayer;
            var sprites = CEntMan.System<SpriteSystem>();
            var sprite = CEntMan.GetComponent<SpriteComponent>(spriteUid);
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), DeltaV, out _, false) && sprites.LayerMapTryGet((spriteUid, sprite), Starlight, out _, false), Is.True,
                "The markings aren't drawn.");
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), $"{DeltaV}-displacement", out _, false), Is.True,
                "A plantigrade marking wasn't reshaped on the vulpkanin's own legs.");
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), $"{Starlight}-displacement", out _, false), Is.False,
                "A marking drawn for these legs was reshaped.");
        });

        await Server.WaitPost(() => humanoids.LoadProfile(SPlayer, profile.WithLegStance(LegStance.Plantigrade)));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var spriteUid = CPlayer;
            var sprites = CEntMan.System<SpriteSystem>();
            var sprite = CEntMan.GetComponent<SpriteComponent>(spriteUid);
            Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), $"{DeltaV}-displacement", out _, false), Is.False,
                "Plantigrade legs kept the reshaping.");
        });
    }
}
