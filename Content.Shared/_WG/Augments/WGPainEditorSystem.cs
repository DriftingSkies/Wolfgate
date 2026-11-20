using Content.Shared._Onyx.Wounds;

namespace Content.Shared._WG.Augments;

/// <summary>Scales down the pain a body with a pain editor gains.</summary>
public sealed class WGPainEditorSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGPainEditorComponent, ModifyPainGainEvent>(OnModifyPainGain);
    }

    private void OnModifyPainGain(Entity<WGPainEditorComponent> ent, ref ModifyPainGainEvent args)
    {
        args.Multiplier *= ent.Comp.PainMultiplier;
    }
}
