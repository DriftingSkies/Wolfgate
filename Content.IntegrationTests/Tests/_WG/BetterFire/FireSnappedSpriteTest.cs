#nullable enable
using Content.Client.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.BetterFire;

/// <summary>
/// A machine whose sprite snaps to cardinals can burn with the four-directional mob fire: the fire layers turn on
/// their own, and the sprite's bounds still work out. Before, working them out failed a debug assertion in the engine.
/// </summary>
[TestFixture]
[TestOf(typeof(FireVisualizerSystem))]
public sealed class FireSnappedSpriteTest
{
    private const string Machine = "WGBetterFireTestMachine";
    private const string Furnace = "WGBetterFireTestFurnace";
    private const string Sign = "WGBetterFireTestSign";
    private const string Body = "body";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGBetterFireTestMachine
  components:
  - type: Sprite
    sprite: Structures/Power/smes.rsi
    snapCardinals: true
    layers:
    - state: smes
      map: [ ""body"" ]
  - type: Appearance
  - type: FireVisuals
    sprite: Mobs/Effects/onfire.rsi
    normalState: Generic_mob_burning

# Its alternate state has an underlay, so it gets the underlay layer too.
- type: entity
  id: WGBetterFireTestFurnace
  components:
  - type: Sprite
    sprite: Structures/Power/smes.rsi
    snapCardinals: true
    layers:
    - state: smes
      map: [ ""body"" ]
  - type: Appearance
  - type: FireVisuals
    sprite: Mobs/Effects/onfire.rsi
    normalState: Generic_mob_burning
    alternateState: Standing

- type: entity
  id: WGBetterFireTestSign
  components:
  - type: Sprite
    sprite: Structures/Power/smes.rsi
    snapCardinals: true
    layers:
    - state: smes
      map: [ ""body"" ]
  - type: Appearance
  - type: FireVisuals
    sprite: Effects/fire.rsi
    normalState: fire
";

    [Test]
    public async Task SnappedSpriteBurnsWithDirectionalFire()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entMan = client.EntMan;
        var sprites = client.System<SpriteSystem>();
        var appearance = client.System<AppearanceSystem>();

        EntityUid machine = default;
        EntityUid furnace = default;
        EntityUid sign = default;
        await client.WaitPost(() =>
        {
            machine = entMan.SpawnEntity(Machine, MapCoordinates.Nullspace);
            furnace = entMan.SpawnEntity(Furnace, MapCoordinates.Nullspace);
            sign = entMan.SpawnEntity(Sign, MapCoordinates.Nullspace);
        });
        await pair.RunTicksSync(2);

        await client.WaitPost(() =>
        {
            // Above 3 stacks the furnace shows its alternate state and that state's underlay.
            foreach (var (uid, stacks) in new[] { (machine, 2), (furnace, 5), (sign, 2) })
            {
                appearance.SetData(uid, FireVisuals.OnFire, true);
                appearance.SetData(uid, FireVisuals.FireStacks, stacks);
            }
        });
        await pair.RunTicksSync(2);

        (bool Visible, LayerRenderingStrategy Strategy)? FireLayer(EntityUid uid, Enum key)
        {
            var sprite = entMan.GetComponent<SpriteComponent>(uid);
            if (!sprites.LayerMapTryGet((uid, sprite), key, out var index, false)
                || !sprites.TryGetLayer((uid, sprite), index, out var layer, false))
                return null;

            return (layer.Visible, layer.RenderingStrategy);
        }

        LayerRenderingStrategy? BodyLayer(EntityUid uid)
        {
            var sprite = entMan.GetComponent<SpriteComponent>(uid);
            if (!sprites.LayerMapTryGet((uid, sprite), Body, out var index, false)
                || !sprites.TryGetLayer((uid, sprite), index, out var layer, false))
                return null;

            return layer.RenderingStrategy;
        }

        // Fails a debug assertion when a snapped layer shows a state with more than one direction.
        void BoundsWorkOut(EntityUid uid)
        {
            var sprite = entMan.GetComponent<SpriteComponent>(uid);
            Assert.DoesNotThrow(() => sprites.GetLocalBounds((uid, sprite)));
        }

        await client.WaitAssertion(() =>
        {
            var turning = (true, LayerRenderingStrategy.Default);

            Assert.That(entMan.GetComponent<SpriteComponent>(machine).GranularLayersRendering,
                "The burning machine does not render its layers one by one.");
            Assert.That(FireLayer(machine, FireVisualLayers.Fire), Is.EqualTo(turning),
                "The machine's directional fire layer is hidden or still snaps to cardinals.");
            Assert.That(FireLayer(machine, FireUnderlayVisualLayers.Underlay), Is.Null,
                "A fire state without an underlay added an underlay layer.");
            Assert.That(BodyLayer(machine), Is.EqualTo(LayerRenderingStrategy.UseSpriteStrategy),
                "The machine's own layer no longer snaps with its sprite.");
            BoundsWorkOut(machine);

            Assert.That(FireLayer(furnace, FireVisualLayers.Fire), Is.EqualTo(turning),
                "The furnace's directional fire layer is hidden or still snaps to cardinals.");
            Assert.That(FireLayer(furnace, FireUnderlayVisualLayers.Underlay), Is.EqualTo(turning),
                "The furnace's fire underlay is hidden or still snaps to cardinals.");
            Assert.That(BodyLayer(furnace), Is.EqualTo(LayerRenderingStrategy.UseSpriteStrategy),
                "The furnace's own layer no longer snaps with its sprite.");
            BoundsWorkOut(furnace);

            // One-directional fire has no need to turn, and keeps snapping.
            Assert.That(entMan.GetComponent<SpriteComponent>(sign).GranularLayersRendering, Is.False,
                "A snapped sprite with one-directional fire was switched to rendering its layers one by one.");
            BoundsWorkOut(sign);
        });

        await client.WaitPost(() =>
        {
            entMan.DeleteEntity(machine);
            entMan.DeleteEntity(furnace);
            entMan.DeleteEntity(sign);
        });

        await pair.CleanReturnAsync();
    }
}
