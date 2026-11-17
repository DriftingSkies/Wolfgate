using Content.Server._WG.Remade.Conversion;
using Robust.Shared.Timing;

namespace Content.Server._WG.Remade.Elites;

/// <summary>Shortens the turning of a corpse when the Infector doing it is an enhanced one.</summary>
public sealed partial class WGReanimateHasteSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGRemadeConvertingComponent>();
        while (query.MoveNext(out var uid, out var converting))
        {
            if (HasComp<WGReanimateHastenedComponent>(uid)
                || converting.Reanimator is not { } infector
                || !TryComp<WGReanimateHasteComponent>(infector, out var haste))
            {
                continue;
            }

            EnsureComp<WGReanimateHastenedComponent>(uid);
            var remaining = converting.EndsAt - now;
            if (remaining > TimeSpan.Zero)
                converting.EndsAt = now + remaining * haste.Share;
        }
    }
}
