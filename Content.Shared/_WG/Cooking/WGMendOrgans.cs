using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Cooking;

/// <summary>
/// A soup's or stew's gentle mending: each metabolism tick restores a little health to the eater's inner organs, never
/// past <see cref="Cap"/> of their maximum and never to a failed one, so home cooking helps between doctor's visits but
/// doesn't replace them.
/// </summary>
public sealed partial class WGMendOrgans : EntityEffect
{
    /// <summary>Health restored to each organ a tick, before reagent scaling.</summary>
    [DataField]
    public float Amount = 0.08f;

    /// <summary>The organ slots it mends; empty for heart, lungs, liver, kidneys and stomach.</summary>
    [DataField]
    public List<string> Organs = new();

    /// <summary>Organs it favours (a liver stew the liver), mended <see cref="FavourFactor"/> times as fast.</summary>
    [DataField]
    public List<string> Favours = new();

    [DataField]
    public float FavourFactor = 3f;

    /// <summary>The share of an organ's maximum it mends up to.</summary>
    [DataField]
    public float Cap = 0.8f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wg-mend-organs", ("cap", (int) (Cap * 100)));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale.Float() : 1f;
        var ev = new WGMendOrgansEvent(args.TargetEntity, Amount * scale, Organs, Favours, FavourFactor, Cap);
        args.EntityManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
    }
}

/// <summary>Raised broadcast to mend <paramref name="Target"/>'s inner organs a little.</summary>
[ByRefEvent]
public readonly record struct WGMendOrgansEvent(EntityUid Target, float Amount, List<string> Organs, List<string> Favours, float FavourFactor, float Cap);
