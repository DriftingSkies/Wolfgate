using Content.Shared._WG.Remade.Limbs;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WG.Remade.Abilities;

/// <summary>A swing of the tail at whatever is in front of it, after a short wind-up. It needs its tail. Numbers from DS13's leaper tail stab.</summary>
public sealed partial class WGTailStabAbility : WGAbility
{
    /// <summary>Seconds it rears back before it strikes.</summary>
    [DataField]
    public float WindUp = 0.6f;

    /// <summary>The widest angle the stab covers, in degrees.</summary>
    [DataField]
    public float Arc = 90f;

    /// <summary>How far the tail reaches.</summary>
    [DataField]
    public float Reach = 1.8f;

    [DataField]
    public float KnockdownTime;

    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public SoundSpecifier? HitSound;

    /// <summary>Stabs it has made.</summary>
    [ViewVariables]
    public int Stabs;

    /// <summary>Things the last stab hit.</summary>
    [ViewVariables]
    public int Hits;

    private bool _winding;
    private TimeSpan _strikeAt;
    private System.Numerics.Vector2 _aim;

    public WGTailStabAbility()
    {
        Cooldown = 9f;
        Range = 1.8f;
        Needs.Add(WGRemadeLimbStat.Leap);
    }

    public override float SpeedModifier => _winding ? 0f : 1f;

    public override WGAbilityResult TryStart(in WGAbilityContext ctx)
    {
        if (!InRange(ctx, out var target))
            return WGAbilityResult.None;

        _aim = ctx.System.Toward(ctx.Owner, target);
        _winding = true;
        _strikeAt = ctx.Now + TimeSpan.FromSeconds(WindUp);
        Hits = 0;
        ctx.System.Play(ctx.Owner, Sound);
        return WGAbilityResult.Running;
    }

    public override bool Update(in WGAbilityContext ctx, float frameTime)
    {
        if (ctx.Now < _strikeAt)
            return true;

        Stabs++;
        var half = Angle.FromDegrees(Arc / 2f);
        var aim = new Angle(_aim);
        foreach (var prey in ctx.System.Prey(ctx.Owner, Reach))
        {
            var offset = ctx.System.Toward(ctx.Owner, prey);
            if (offset.LengthSquared() > 0.04f && Math.Abs(Angle.ShortestDistance(aim, new Angle(offset)).Theta) > half.Theta)
                continue;

            Hits++;
            ctx.System.Roster.Strike(ctx.Owner, prey, Damage);
            if (KnockdownTime > 0f)
                ctx.System.Knockdown(prey, KnockdownTime);

            ctx.System.Play(prey, HitSound);
            ctx.System.Popup(Loc.GetString("wg-remade-stab-hit", ("mob", ctx.Owner), ("target", prey)), prey, PopupType.LargeCaution);
        }

        return false;
    }

    public override void Stop(in WGAbilityContext ctx)
    {
        _winding = false;
    }
}
