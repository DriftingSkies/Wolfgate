using Content.Shared._WF.Barricades;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Barricades;

/// <summary>Draws south-facing barricades over mobs so they cover whoever stands behind them.</summary>
public sealed class RotationDrawDepthSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<RotationDrawDepthComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var rotation, out var sprite, out var xform))
        {
            var depth = xform.LocalRotation.GetCardinalDir() == Direction.South
                ? rotation.SouthDrawDepth
                : rotation.DefaultDrawDepth;

            if (sprite.DrawDepth != depth)
                _sprite.SetDrawDepth((uid, sprite), depth);
        }
    }
}
