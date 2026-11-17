using Content.Shared._WG.Remade.Limbs;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WG.Remade.Abilities;

/// <summary>A cry the moment it first sees a target. Everyone living nearby shakes and has their view kicked.</summary>
public sealed partial class WGShoutAbility : WGAbility
{
    /// <summary>How far the cry shakes people.</summary>
    [DataField]
    public float Radius = 8f;

    /// <summary>How long people shake at the edge. Closer is longer.</summary>
    [DataField]
    public float ShakeTime = 3f;

    public WGShoutAbility()
    {
        Cooldown = 8f;
        Range = 20f;
        Needs.Add(WGRemadeLimbStat.Voice);
    }

    public override WGAbilityResult TryStart(in WGAbilityContext ctx)
    {
        if (!ctx.Sighted || !InRange(ctx, out _))
            return WGAbilityResult.None;

        ctx.System.Play(ctx.Owner, Sound);
        ctx.System.Frighten(ctx.Owner, Radius, ShakeTime, Loc.GetString("wg-remade-shout-hear", ("mob", ctx.Owner)));
        return WGAbilityResult.Done;
    }
}
