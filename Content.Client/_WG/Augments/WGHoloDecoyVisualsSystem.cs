using Content.Shared._WG.Augments;
using Robust.Client.GameObjects;

namespace Content.Client._WG.Augments;

/// <summary>Dresses a holo projector's copy in its source's sprite, once the source is known to the client.</summary>
public sealed partial class WGHoloDecoyVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<WGHoloDecoyComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var decoy, out var sprite))
        {
            if (decoy.Copied || decoy.Source is not { } sourceUid || !TryComp<SpriteComponent>(sourceUid, out var source))
                continue;

            _sprite.CopySprite((sourceUid, source), (uid, sprite));
            // The source's hover outline comes along as a post-shader with no prototype.
            _sprite.ClearPostShaders(sprite);
            _sprite.SetVisible((uid, sprite), true);
            decoy.Copied = true;
        }
    }
}
