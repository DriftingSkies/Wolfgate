using Content.Shared._WG.Remade.Limbs;
using Content.Shared.Popups;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WG.Remade.Abilities;

/// <summary>A cry, a few seconds of swelling and shaking while it keeps coming, then it bursts. It needs its pustule (<see cref="WGRemadeLimbStat.Burst"/>); lose it mid-fuse and the fuse is cut.</summary>
public sealed partial class WGExplodeAbility : WGAbility
{
    /// <summary>Seconds between the cry and the blast.</summary>
    [DataField]
    public float Fuse = 2.2f;

    /// <summary>How fast it moves while the fuse burns, as a multiple of normal.</summary>
    [DataField]
    public float WalkSpeed = 0.6f;

    /// <summary>Times it has lit the fuse.</summary>
    [ViewVariables]
    public int Lit;

    private bool _burning;
    private TimeSpan _end;

    public WGExplodeAbility()
    {
        Cooldown = 12f;
        Range = 3.5f;
        Needs.Add(WGRemadeLimbStat.Burst);
    }

    public override float SpeedModifier => _burning ? WalkSpeed : 1f;

    public override WGAbilityResult TryStart(in WGAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range))
            return WGAbilityResult.None;

        Lit++;
        _burning = true;
        _end = ctx.Now + TimeSpan.FromSeconds(Fuse);
        ctx.System.Play(ctx.Owner, Sound);
        ctx.System.Swell(ctx.Owner, Fuse);
        ctx.System.Popup(Loc.GetString("wg-remade-explode-start", ("mob", ctx.Owner)), ctx.Owner, PopupType.MediumCaution);
        return WGAbilityResult.Running;
    }

    public override bool Update(in WGAbilityContext ctx, float frameTime)
    {
        if (!ctx.System.Roster.Can(ctx.Owner, WGRemadeLimbStat.Burst))
            return false;

        if (ctx.Now < _end)
            return true;

        ctx.System.Exploder.Detonate(ctx.Owner);
        return false;
    }

    public override void Stop(in WGAbilityContext ctx)
    {
        _burning = false;
    }
}
