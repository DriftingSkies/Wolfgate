using Content.Client._WG.Waddle;
using Content.Shared._WG.PixelShift;
using Robust.Client.GameObjects;

namespace Content.Client._WG.PixelShift;

/// <summary>Takes last frame's pixel shift off each sprite before animations and visualizers read it.</summary>
public sealed class WGPixelShiftRestoreSystem : EntitySystem
{
    [Dependency] private WGPixelShiftVisualsSystem _visuals = default!;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(AnimationPlayerSystem));
        UpdatesBefore.Add(typeof(AppearanceSystem));
        UpdatesAfter.Add(typeof(WaddleRestoreSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<WGPixelShiftComponent>();
        while (query.MoveNext(out var uid, out var shift))
        {
            _visuals.Remove((uid, shift));
        }
    }
}
