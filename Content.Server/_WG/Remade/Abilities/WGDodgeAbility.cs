using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WG.Remade.Abilities;

/// <summary>A quick step sideways when hurt from range, or when a shot goes past close by, out of the line of fire. <see cref="WGAbility.Range"/> is the furthest shooter it answers to.</summary>
public sealed partial class WGDodgeAbility : WGAbility
{
    /// <summary>How far the step takes it, in tiles.</summary>
    [DataField]
    public float Distance = 1.5f;

    public WGDodgeAbility()
    {
        Cooldown = 20f;
        MinRange = 2.5f;
        Range = 30f;
    }

    public override WGAbilityResult React(in WGAbilityContext ctx, WGAbilityTrigger trigger, EntityUid? origin)
    {
        if (trigger is not (WGAbilityTrigger.Shot or WGAbilityTrigger.NearMiss) || origin is not { } shooter)
            return WGAbilityResult.None;

        var range = ctx.System.Distance(ctx.Owner, shooter);
        if (range < MinRange || range > Range || !ctx.System.Sidestep(ctx.Owner, shooter, Distance))
            return WGAbilityResult.None;

        ctx.System.Play(ctx.Owner, Sound);
        ctx.System.Popup(Loc.GetString("wg-remade-dodge", ("mob", ctx.Owner)), ctx.Owner);
        return WGAbilityResult.Done;
    }
}
