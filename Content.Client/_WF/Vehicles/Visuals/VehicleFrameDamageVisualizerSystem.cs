using System;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Hardpoint;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Vehicles.Visuals;

/// <summary>Fades the damaged-frame layer in as the vehicle's frame integrity drops.</summary>
public sealed partial class VehicleFrameDamageVisualizerSystem : VisualizerSystem<HardpointIntegrityComponent>
{
    private const float ShowThreshold = 0.9f;
    private const float MinAlpha = 0.1f;

    protected override void OnAppearanceChange(EntityUid uid, HardpointIntegrityComponent component, ref AppearanceChangeEvent args)
    {
        var sprite = args.Sprite;
        if (sprite == null)
            return;

        if (!SpriteSystem.LayerMapTryGet((uid, sprite), VehicleFrameDamageLayers.DamagedFrame, out var layer, false))
            return;

        float fraction;
        if (!AppearanceSystem.TryGetData(uid, VehicleFrameDamageVisuals.IntegrityFraction, out fraction))
        {
            var max = component.MaxIntegrity > 0f ? component.MaxIntegrity : 1f;
            fraction = Math.Clamp(component.Integrity / max, 0f, 1f);
        }

        if (fraction >= ShowThreshold)
        {
            SpriteSystem.LayerSetVisible((uid, sprite), layer, false);
            return;
        }

        var t = fraction / ShowThreshold;
        var alpha = MinAlpha + (1f - MinAlpha) * (1f - t);

        SpriteSystem.LayerSetVisible((uid, sprite), layer, true);
        SpriteSystem.LayerSetColor((uid, sprite), layer, sprite.Color.WithAlpha(alpha));
    }
}
