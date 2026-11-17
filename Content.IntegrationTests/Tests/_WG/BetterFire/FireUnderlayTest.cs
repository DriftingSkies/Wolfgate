using System.Linq;
using Content.Client.Atmos.Components;
using Content.Client.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.BetterFire;

/// <summary>
/// A burning mob draws <c>Standing_underlay</c> below its sprite above the alternate stack count, and fire states
/// without an underlay add no layer.
/// </summary>
[TestFixture]
[TestOf(typeof(FireVisualizerSystem))]
public sealed class FireUnderlayTest
{
    private const string Mob = "WGBetterFireTestMob";
    private const string Item = "WGBetterFireTestItem";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGBetterFireTestMob
  parent: MobFlammable
  components:
  - type: Sprite
    sprite: Mobs/Species/Human/parts.rsi
    layers:
    - state: torso_m
  - type: Appearance
  - type: FireVisuals
    alternateState: Standing

- type: entity
  id: WGBetterFireTestItem
  components:
  - type: Sprite
    sprite: Mobs/Species/Human/parts.rsi
    layers:
    - state: torso_m
  - type: Appearance
  - type: FireVisuals
    sprite: Effects/fire.rsi
    normalState: fire
";

    [Test]
    public async Task UnderlayFollowsFireStacks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entMan = client.EntMan;
        var sprites = client.System<SpriteSystem>();
        var appearance = client.System<AppearanceSystem>();

        EntityUid mob = default;
        EntityUid item = default;
        await client.WaitPost(() =>
        {
            mob = entMan.SpawnEntity(Mob, MapCoordinates.Nullspace);
            item = entMan.SpawnEntity(Item, MapCoordinates.Nullspace);
        });
        await pair.RunTicksSync(2);

        (bool Visible, string State) Layer(EntityUid uid, Enum key)
        {
            var sprite = entMan.GetComponent<SpriteComponent>(uid);
            if (!sprites.LayerMapTryGet((uid, sprite), key, out var index, false)
                || !sprites.TryGetLayer((uid, sprite), index, out var layer, false))
                return (false, string.Empty);

            return (layer.Visible, sprites.LayerGetRsiState((uid, sprite), index).Name);
        }

        async Task SetFire(bool onFire, int stacks)
        {
            await client.WaitPost(() =>
            {
                appearance.SetData(mob, FireVisuals.OnFire, onFire);
                appearance.SetData(mob, FireVisuals.FireStacks, stacks);
            });
            await pair.RunTicksSync(2);
        }

        await client.WaitAssertion(() =>
        {
            var sprite = entMan.GetComponent<SpriteComponent>(mob);
            Assert.That(sprites.LayerMapTryGet((mob, sprite), FireUnderlayVisualLayers.Underlay, out var under, false),
                "The mob has no underlay layer.");
            Assert.That(sprites.LayerMapTryGet((mob, sprite), FireVisualLayers.Fire, out var fire, false));
            Assert.That(under, Is.EqualTo(0), "The underlay is not the bottom layer.");
            Assert.That(fire, Is.EqualTo(sprite.AllLayers.Count() - 1), "The fire layer is not the top layer.");
            Assert.That(Layer(mob, FireUnderlayVisualLayers.Underlay).Visible, Is.False);

            var itemSprite = entMan.GetComponent<SpriteComponent>(item);
            Assert.That(sprites.LayerMapTryGet((item, itemSprite), FireUnderlayVisualLayers.Underlay, out _, false), Is.False,
                "A fire state without an underlay added an underlay layer.");
        });

        await SetFire(true, 2);
        await client.WaitAssertion(() =>
        {
            Assert.That(Layer(mob, FireVisualLayers.Fire), Is.EqualTo((true, "Generic_mob_burning")));
            Assert.That(Layer(mob, FireUnderlayVisualLayers.Underlay).Visible, Is.False);
        });

        await SetFire(true, 5);
        await client.WaitAssertion(() =>
        {
            Assert.That(Layer(mob, FireVisualLayers.Fire), Is.EqualTo((true, "Standing")));
            Assert.That(Layer(mob, FireUnderlayVisualLayers.Underlay), Is.EqualTo((true, "Standing_underlay")));
        });

        await SetFire(false, 0);
        await client.WaitAssertion(() =>
        {
            Assert.That(Layer(mob, FireVisualLayers.Fire).Visible, Is.False);
            Assert.That(Layer(mob, FireUnderlayVisualLayers.Underlay).Visible, Is.False);
        });

        await client.WaitPost(() => entMan.RemoveComponent<FireVisualsComponent>(mob));
        await client.WaitAssertion(() =>
        {
            var sprite = entMan.GetComponent<SpriteComponent>(mob);
            Assert.That(sprites.LayerMapTryGet((mob, sprite), FireUnderlayVisualLayers.Underlay, out _, false), Is.False);
            Assert.That(sprite.AllLayers.Count(), Is.EqualTo(1), "Removing fire visuals left layers behind.");
        });

        await client.WaitPost(() =>
        {
            entMan.DeleteEntity(mob);
            entMan.DeleteEntity(item);
        });

        await pair.CleanReturnAsync();
    }
}
