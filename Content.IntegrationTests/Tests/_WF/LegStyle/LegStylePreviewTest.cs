using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.LegStyle;
using Content.Shared._WF.LegStyle;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.LegStyle;

/// <summary>
/// The creator's preview doll, dressed and reloaded the way the editor does on every change, never keeps a
/// reshaping layer whose marking is gone: drawing one throws and stops the rest of the sprite.
/// </summary>
[TestFixture]
[TestOf(typeof(LegStyleSystem))]
public sealed class LegStylePreviewTest
{
    [Test]
    public async Task PreviewDollKeepsNoStrayReshapingTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entities = client.ResolveDependency<IEntityManager>();
        var humanoids = entities.System<SharedHumanoidAppearanceSystem>();
        var inventory = entities.System<InventorySystem>();

        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
        profile = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(new List<Marking>
        {
            new("GauzeUpperLegLeft", 1),
            new("GauzeLowerLegLeft", 1),
        }));
        var digitigrade = profile.WithLegStance(LegStance.Digitigrade);

        await client.WaitAssertion(() =>
        {
            var doll = entities.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            humanoids.LoadProfile(doll, digitigrade);
            var suit = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", MapCoordinates.Nullspace);
            Assert.That(inventory.TryEquip(doll, suit, "jumpsuit", true, true), Is.True);
            AssertNoStrays(entities, doll, "dressed");

            humanoids.LoadProfile(doll, digitigrade);
            AssertNoStrays(entities, doll, "reloaded");
            humanoids.LoadProfile(doll, profile);
            AssertNoStrays(entities, doll, "back on plantigrade legs");
            humanoids.LoadProfile(doll, digitigrade);
            AssertNoStrays(entities, doll, "digitigrade again");

            var sprite = entities.GetComponent<SpriteComponent>(doll);
            Assert.That(sprite.LayerMapTryGet("GauzeLowerLegLeft-gauze_lowerleg_l-displacement", out _), Is.True,
                "The foot marking lost its reshaping.");
            entities.DeleteEntity(doll);
        });

        await pair.CleanReturnAsync();
    }

    private static void AssertNoStrays(IEntityManager entities, EntityUid doll, string when)
    {
        var sprite = entities.GetComponent<SpriteComponent>(doll);
        var strays = sprite.AllLayers
            .OfType<SpriteComponent.Layer>()
            .Select(layer => layer.CopyToShaderParameters?.LayerKey)
            .Where(key => key != null && !sprite.LayerMapTryGet(key, out _))
            .ToList();
        Assert.That(strays, Is.Empty, $"Reshaping layers for missing layers once {when}: {string.Join(", ", strays)}");
    }
}
