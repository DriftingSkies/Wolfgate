using System.Numerics;
using Content.Shared.Popups;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WG.Remade.Abilities;

/// <summary>A twitcher jumps a few tiles at once towards its target, or off to one side when shot at from range.</summary>
public sealed partial class WGBlinkAbility : WGAbility
{
    /// <summary>Tiles it jumps towards the target.</summary>
    [DataField]
    public float Distance = 3.2f;

    /// <summary>Tiles it jumps sideways when shot at.</summary>
    [DataField]
    public float EvadeDistance = 2.5f;

    /// <summary>Times it has blinked.</summary>
    [ViewVariables]
    public int Blinks;

    public WGBlinkAbility()
    {
        Cooldown = 6f;
        MinRange = 4f;
        Range = 12f;
    }

    public override WGAbilityResult TryStart(in WGAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range))
            return WGAbilityResult.None;

        return Jump(ctx, ctx.System.Toward(ctx.Owner, target), Distance) ? WGAbilityResult.Done : WGAbilityResult.None;
    }

    public override WGAbilityResult React(in WGAbilityContext ctx, WGAbilityTrigger trigger, EntityUid? origin)
    {
        if (trigger != WGAbilityTrigger.Shot || origin is not { } shooter)
            return WGAbilityResult.None;

        var away = -ctx.System.Toward(ctx.Owner, shooter);
        var side = new Vector2(-away.Y, away.X) * (ctx.System.RandomRange(0f, 1f) < 0.5f ? 1f : -1f);
        return Jump(ctx, side, EvadeDistance) ? WGAbilityResult.Done : WGAbilityResult.None;
    }

    private bool Jump(in WGAbilityContext ctx, Vector2 direction, float distance)
    {
        if (!ctx.System.Elite.Blink(ctx.Owner, direction, distance))
            return false;

        Blinks++;
        ctx.System.Play(ctx.Owner, Sound);
        ctx.System.Popup(Loc.GetString("wg-remade-elite-blink", ("mob", ctx.Owner)), ctx.Owner);
        return true;
    }
}
