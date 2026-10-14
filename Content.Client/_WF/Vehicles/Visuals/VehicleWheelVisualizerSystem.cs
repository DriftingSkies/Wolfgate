using System;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WF.Vehicles.Visuals;

/// <summary>Animates the wheel layer while the vehicle moves and darkens it as the wheels wear out.</summary>
public sealed partial class VehicleWheelVisualizerSystem : VisualizerSystem<VehicleWheelSlotsComponent>
{
    [Dependency] private IGameTiming _timing = default!;

    protected override void OnAppearanceChange(EntityUid uid, VehicleWheelSlotsComponent component, ref AppearanceChangeEvent args)
    {
        UpdateWheelVisuals(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var enumerator = EntityQueryEnumerator<VehicleWheelSlotsComponent, SpriteComponent>();
        while (enumerator.MoveNext(out var uid, out _, out var sprite))
        {
            UpdateWheelVisuals(uid, sprite);
        }
    }

    private void UpdateWheelVisuals(EntityUid uid, SpriteComponent? sprite = null)
    {
        if (sprite == null && !TryComp(uid, out sprite))
            return;

        if (!SpriteSystem.LayerMapTryGet((uid, sprite), VehicleWheelLayers.Wheels, out var layer, false))
            return;

        var installed = 0;
        if (AppearanceSystem.TryGetData(uid, VehicleWheelVisuals.WheelCount, out int count))
            installed = count;

        if (AppearanceSystem.TryGetData(uid, VehicleWheelVisuals.HasAllWheels, out bool present) && installed == 0)
            installed = present ? 1 : 0;

        if (installed <= 0)
        {
            SpriteSystem.LayerSetVisible((uid, sprite), layer, false);
            return;
        }

        var isMoving = false;
        if (TryComp<GridVehicleMoverComponent>(uid, out var mover))
        {
            isMoving = Math.Abs(mover.CurrentSpeed) > 0.01f ||
                (mover.TurnInPlace &&
                 (MathF.Abs(mover.AngularVelocityDegrees) > 0.001f ||
                  mover.InPlaceTurnBlockUntil > _timing.CurTime));
        }

        var functional = installed;
        var averageIntegrity = 1f;

        if (AppearanceSystem.TryGetData(uid, VehicleWheelVisuals.WheelFunctionalCount, out int functionalCount))
            functional = functionalCount;

        if (AppearanceSystem.TryGetData(uid, VehicleWheelVisuals.WheelIntegrityFraction, out float integrityFraction))
            averageIntegrity = integrityFraction;

        var fraction = Math.Clamp((float) functional / installed, 0f, 1f);

        var integrityBrightness = 0.3f + 0.7f * averageIntegrity;
        var functionalBrightness = 0.3f + 0.7f * fraction;
        var brightness = MathF.Min(integrityBrightness, functionalBrightness);
        var destroyed = functional <= 0;

        if (destroyed)
            brightness = 1f;

        var targetState = destroyed ? "wheels_1" : "wheels_0";

        if (SpriteSystem.LayerGetRsiState((uid, sprite), layer).Name != targetState)
        {
            SpriteSystem.LayerSetRsiState((uid, sprite), layer, targetState);

            if (isMoving && !destroyed)
                SpriteSystem.LayerSetAnimationTime((uid, sprite), layer, 0f);
        }

        SpriteSystem.LayerSetAutoAnimated((uid, sprite), layer, isMoving && !destroyed);
        SpriteSystem.LayerSetColor((uid, sprite), layer, new Color(brightness, brightness, brightness, sprite.Color.A));
        SpriteSystem.LayerSetVisible((uid, sprite), layer, true);
    }
}
