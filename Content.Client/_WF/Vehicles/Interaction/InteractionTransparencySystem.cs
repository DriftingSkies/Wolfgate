using Content.Shared._WF.Vehicles.Compat;
using Robust.Client.GameObjects;
using Robust.Shared.Graphics;

namespace Content.Client._WF.Vehicles.Interaction;

/// <summary>Answers whether a click should pass through an entity because the local player stands inside its sprite.</summary>
public sealed partial class InteractionTransparencySystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>True when <paramref name="target"/> is interaction transparent and the player's position is within its sprite bounds.</summary>
    public bool IsTransparent(EntityUid target, EntityUid? localEntity, IEye? eye)
    {
        if (localEntity is not { } user ||
            eye == null ||
            !TryComp(target, out InteractionTransparencyComponent? transparency) ||
            !transparency.Enabled)
        {
            return false;
        }

        if (!TryComp(target, out TransformComponent? targetXform) ||
            !TryComp(target, out SpriteComponent? sprite) ||
            !TryComp(user, out TransformComponent? playerXform))
        {
            return false;
        }

        var (spritePos, spriteRot) = _transform.GetWorldPositionRotation(targetXform);
        var spriteBox = _sprite.CalculateBounds((target, sprite), spritePos, spriteRot, eye.Rotation);
        var playerPos = _transform.GetMapCoordinates(playerXform).Position;

        return spriteBox.Contains(playerPos);
    }
}
