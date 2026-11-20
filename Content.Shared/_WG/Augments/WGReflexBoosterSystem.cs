using Content.Shared._Onyx.Wounds;

namespace Content.Shared._WG.Augments;

/// <summary>Scales up the pain a body with a reflex booster gains.</summary>
public sealed partial class WGReflexBoosterSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGReflexBoosterComponent, ModifyPainGainEvent>(OnModifyPainGain);
    }

    private void OnModifyPainGain(Entity<WGReflexBoosterComponent> ent, ref ModifyPainGainEvent args)
    {
        args.Multiplier *= ent.Comp.PainMultiplier;
    }
}
