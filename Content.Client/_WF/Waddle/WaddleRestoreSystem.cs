using Content.Shared._WF.Waddle;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Waddle;

/// <summary>Takes last frame's waddle step off each wearer before animations and visualizers read the sprite.</summary>
public sealed partial class WaddleRestoreSystem : EntitySystem
{
    [Dependency] private WaddleSystem _waddle = default!;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(AnimationPlayerSystem));
        UpdatesBefore.Add(typeof(AppearanceSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<WaddleWhenWornComponent>();
        while (query.MoveNext(out var waddle))
        {
            if (waddle.Wearer != null)
                _waddle.RemoveStep(waddle);
        }
    }
}
