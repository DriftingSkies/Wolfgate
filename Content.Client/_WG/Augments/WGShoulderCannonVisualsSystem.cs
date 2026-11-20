using Content.Shared._WG.Augments;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WG.Augments;

/// <summary>
/// Draws a raised shoulder cannon on its body: just under the helmet where the cannon is on the near side, under the
/// whole body where it is on the far side, with an unshaded glow over each while it charges.
/// </summary>
public sealed class WGShoulderCannonVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private const string FrontKey = "wg-shoulder-cannon";
    private const string FrontGlowKey = "wg-shoulder-cannon-glow";
    private const string BackKey = "wg-shoulder-cannon-back";
    private const string BackGlowKey = "wg-shoulder-cannon-back-glow";

    /// <summary>The helmet's layer; the near-side cannon goes just under it.</summary>
    private const string HelmetKey = "head";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShoulderCannonDeployedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WGShoulderCannonDeployedComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WGShoulderCannonDeployedComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<WGShoulderCannonDeployedComponent> ent, ref ComponentStartup args)
    {
        UpdateLayers(ent);
    }

    private void OnState(Entity<WGShoulderCannonDeployedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateLayers(ent);
    }

    private void OnShutdown(Entity<WGShoulderCannonDeployedComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        foreach (var key in new[] { FrontKey, FrontGlowKey, BackKey, BackGlowKey })
        {
            if (_sprite.LayerMapTryGet((ent, sprite), key, out var layer, false))
                _sprite.LayerSetVisible((ent, sprite), layer, false);
        }
    }

    private void UpdateLayers(Entity<WGShoulderCannonDeployedComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        Entity<SpriteComponent> target = (ent, sprite);
        var comp = ent.Comp;
        // Inserting a layer shifts the ones above it, so each is looked up by key once all exist.
        Ensure(target, FrontKey, comp.State, () =>
            _sprite.LayerMapTryGet(target.AsNullable(), HelmetKey, out var helmet, false) ? helmet : null);
        Ensure(target, FrontGlowKey, comp.GlowState, () => _sprite.LayerMapGet(target.AsNullable(), FrontKey) + 1);
        Ensure(target, BackKey, comp.State + "-back", () => 0);
        Ensure(target, BackGlowKey, comp.GlowState + "-back", () => _sprite.LayerMapGet(target.AsNullable(), BackKey) + 1);

        Set(FrontKey, comp.State, true, false);
        Set(BackKey, comp.State + "-back", true, false);
        Set(FrontGlowKey, comp.GlowState, comp.Charging, true);
        Set(BackGlowKey, comp.GlowState + "-back", comp.Charging, true);

        void Set(string key, string state, bool visible, bool unshaded)
        {
            var layer = _sprite.LayerMapGet(target.AsNullable(), key);
            _sprite.LayerSetSprite(target.AsNullable(), layer, new SpriteSpecifier.Rsi(comp.Rsi, state));
            _sprite.LayerSetVisible(target.AsNullable(), layer, visible);
            if (unshaded)
                sprite.LayerSetShader(layer, "unshaded");
        }
    }

    /// <summary>Adds a layer under the key at the given index (or on top), unless it is already there.</summary>
    private void Ensure(Entity<SpriteComponent> sprite, string key, string state, Func<int?> index)
    {
        if (_sprite.LayerMapTryGet(sprite.AsNullable(), key, out _, false))
            return;

        var rsi = Comp<WGShoulderCannonDeployedComponent>(sprite).Rsi;
        var layer = _sprite.AddLayer(sprite.AsNullable(), new SpriteSpecifier.Rsi(rsi, state), index());
        _sprite.LayerMapSet(sprite.AsNullable(), key, layer);
    }
}
