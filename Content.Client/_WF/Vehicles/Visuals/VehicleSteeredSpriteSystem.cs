using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Turret;
using Content.Client._WF.Vehicles.Turret;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Maths;

namespace Content.Client._WF.Vehicles.Visuals;

/// <summary>
/// Draws a freely steered vehicle, and every turret, as one of its four sprites turned by what is left of its angle.
/// The side sprites show within 22.5 degrees of east and west; the front and back sprites cover every other angle.
/// </summary>
public sealed partial class VehicleSteeredSpriteSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    // Hulls drawn this way, with the sprite settings they had before, put back when they go to the four-way drive.
    private readonly Dictionary<EntityUid, (bool NoRotation, bool Granular)> _steered = new();

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(VehicleTurretVisualSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        var vehicles = EntityQueryEnumerator<GridVehicleMoverComponent, SpriteComponent>();
        while (vehicles.MoveNext(out var uid, out var mover, out var sprite))
        {
            if (mover.FreeSteering)
            {
                _steered.TryAdd(uid, (sprite.NoRotation, sprite.GranularLayersRendering));
                ApplyDirection(uid, sprite);
            }
            else if (_steered.Remove(uid, out var before))
            {
                Restore(uid, sprite, before.NoRotation, before.Granular);
            }
        }

        var turrets = EntityQueryEnumerator<VehicleTurretVisualComponent, SpriteComponent>();
        while (turrets.MoveNext(out var uid, out _, out var sprite))
        {
            ApplyDirection(uid, sprite);
        }
    }

    private void Restore(EntityUid uid, SpriteComponent sprite, bool noRotation, bool granular)
    {
        sprite.EnableDirectionOverride = false;
        sprite.NoRotation = noRotation;

        var index = 0;
        foreach (var layer in sprite.AllLayers)
        {
            var i = index++;
            if (layer.ActualRsi is { } rsi &&
                rsi.TryGetState(layer.RsiState, out var state) &&
                state.RsiDirections == RsiDirectionType.Dir4)
            {
                _sprite.LayerSetRotation((uid, sprite), i, Angle.Zero);
            }
        }

        _sprite.SetGranularLayersRendering((uid, sprite), granular);
    }

    private void ApplyDirection(EntityUid uid, SpriteComponent sprite)
    {
        var screenRotation = _transform.GetWorldRotation(uid) + _eye.CurrentEye.Rotation;
        var direction = VehicleTurretDirectionHelpers.GetRenderAlignedCardinalDir(screenRotation);

        sprite.EnableDirectionOverride = true;
        sprite.DirectionOverride = direction;

        // The engine turns a layer by the direction it would have picked itself, which is not the one shown here.
        // That turn is switched off, and each four-way layer is turned by the shown direction instead.
        sprite.NoRotation = true;
        _sprite.SetGranularLayersRendering((uid, sprite), true);

        var index = 0;
        foreach (var layer in sprite.AllLayers)
        {
            var i = index++;
            _sprite.LayerSetRenderingStrategy((uid, sprite), i, LayerRenderingStrategy.Default);

            if (layer.ActualRsi is not { } rsi ||
                !rsi.TryGetState(layer.RsiState, out var state) ||
                state.RsiDirections != RsiDirectionType.Dir4)
            {
                continue;
            }

            _sprite.LayerSetRotation((uid, sprite), i, -direction.ToAngle());
        }
    }
}
