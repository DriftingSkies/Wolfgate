using Content.Server._DV.Feroxi;
using Content.Server.Body.Systems;
using Content.Shared._WF.Species;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;

namespace Content.Server._WF.Species;

/// <summary>
/// Tops thirst back up to Okay on mobs that never need to drink. Checked every tick, so a drain such as a Kelotane
/// overdose never holds long enough to set off thirst effects or a Feroxi's dehydrated lungs.
/// </summary>
public sealed class WFNeverThirstySystem : EntitySystem
{
    [Dependency] private ThirstSystem _thirst = default!;

    public override void Initialize()
    {
        base.Initialize();
        // After chemicals drain it and before the Feroxi lung swap reads it, so a drain never dehydrates them for a tick.
        UpdatesAfter.Add(typeof(MetabolizerSystem));
        UpdatesBefore.Add(typeof(FeroxiDehydrateSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WFNeverThirstyComponent, ThirstComponent>();
        while (query.MoveNext(out var uid, out _, out var thirst))
        {
            var okay = thirst.ThirstThresholds[ThirstThreshold.Okay];
            if (thirst.CurrentThirst < okay)
                _thirst.SetThirst(uid, thirst, okay);
        }
    }
}
