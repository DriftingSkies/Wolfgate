using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Hardpoint;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client._WF.Vehicles.Visuals;

/// <summary>Shows or hides each hardpoint layer of the vehicle sprite from the layer states the server publishes.</summary>
public sealed partial class VehicleHardpointVisualizerSystem : VisualizerSystem<VehicleHardpointVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, VehicleHardpointVisualsComponent component, ref AppearanceChangeEvent args)
    {
        var sprite = args.Sprite;
        if (sprite == null)
            return;

        if (!AppearanceSystem.TryGetData(uid, VehicleHardpointVisualsVisuals.Layers, out string? encoded) ||
            encoded == null)
        {
            return;
        }

        foreach (var entry in VehicleHardpointLayerCodec.Decode(encoded))
        {
            UpdateLayer((uid, sprite), entry.Layer, entry.State);
        }
    }

    private void UpdateLayer(Entity<SpriteComponent> sprite, string layerMap, string state)
    {
        if (!SpriteSystem.LayerMapTryGet(sprite.AsNullable(), layerMap, out var layer, false))
            return;

        if (string.IsNullOrWhiteSpace(state))
        {
            SpriteSystem.LayerSetVisible(sprite.AsNullable(), layer, false);
            return;
        }

        SpriteSystem.LayerSetRsiState(sprite.AsNullable(), layer, state);
        SpriteSystem.LayerSetVisible(sprite.AsNullable(), layer, true);
    }
}
