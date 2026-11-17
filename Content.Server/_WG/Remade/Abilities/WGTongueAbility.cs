using Content.Shared._WG.Remade.Limbs;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WG.Remade.Abilities;

/// <summary>
/// A tongue lashes out at someone it can see, wraps them and hauls them in, then the Remade stabs them as they arrive.
/// It needs its tongue (<see cref="WGRemadeLimbStat.Grab"/>).
/// </summary>
public sealed partial class WGTongueAbility : WGAbility
{
    /// <summary>Seconds it stands and winds up before the tongue goes.</summary>
    [DataField]
    public float WindUp = 0.5f;

    /// <summary>Seconds the haul takes before the stab.</summary>
    [DataField]
    public float PullTime = 0.8f;

    /// <summary>How close the victim is dragged.</summary>
    [DataField]
    public float Hold = 1.3f;

    [DataField]
    public float PullSpeed = 12f;

    /// <summary>The stab's reach from where the victim ends up.</summary>
    [DataField]
    public float StabReach = 2.4f;

    [DataField]
    public float KnockdownTime = 1.5f;

    [DataField]
    public EntProtoId Beam = "WGRemadeTongueBeam";

    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public SoundSpecifier? PullSound;

    [DataField]
    public SoundSpecifier? HitSound;

    private enum Phase : byte
    {
        Idle,
        WindUp,
        Pull,
    }

    /// <summary>Victims it has hauled in.</summary>
    [ViewVariables]
    public int Hauls;

    /// <summary>Whether the last haul ended in a stab.</summary>
    [ViewVariables]
    public bool Stabbed;

    private Phase _phase;
    private TimeSpan _phaseEnd;
    private EntityUid _target;

    public WGTongueAbility()
    {
        Cooldown = 14f;
        MinRange = 3f;
        Range = 10f;
        Needs.Add(WGRemadeLimbStat.Grab);
    }

    public override float SpeedModifier => _phase == Phase.Idle ? 1f : 0f;

    public override WGAbilityResult TryStart(in WGAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range))
            return WGAbilityResult.None;

        _phase = Phase.WindUp;
        _target = target;
        Stabbed = false;
        _phaseEnd = ctx.Now + TimeSpan.FromSeconds(WindUp);
        ctx.System.Play(ctx.Owner, Sound);
        return WGAbilityResult.Running;
    }

    public override bool Update(in WGAbilityContext ctx, float frameTime)
    {
        if (!ctx.System.IsLive(_target))
            return false;

        ctx.System.KeepTarget(ctx.Owner, _target);
        if (_phase == Phase.WindUp)
        {
            if (ctx.Now < _phaseEnd)
                return true;

            var offset = ctx.System.Toward(ctx.Owner, _target);
            var distance = offset.Length();
            if (distance > Range || !ctx.System.Roster.Can(ctx.Owner, WGRemadeLimbStat.Grab))
                return false;

            Hauls++;
            _phase = Phase.Pull;
            _phaseEnd = ctx.Now + TimeSpan.FromSeconds(PullTime);
            ctx.System.Play(ctx.Owner, PullSound);
            ctx.System.Popup(Loc.GetString("wg-remade-elite-tongue-hit", ("mob", ctx.Owner), ("target", _target)), _target, PopupType.LargeCaution);
            ctx.System.Knockdown(_target, PullTime + 0.4f);
            ctx.System.Elite.Throw(_target, -offset, Math.Max(distance - Hold, 0f), PullSpeed);
            ctx.System.DrawBeam(ctx.Owner, _target, Beam);
            return true;
        }

        if (ctx.Now < _phaseEnd)
            return true;

        if (ctx.System.Distance(ctx.Owner, _target) <= StabReach)
        {
            Stabbed = true;
            ctx.System.Roster.Strike(ctx.Owner, _target, Damage);
            ctx.System.Knockdown(_target, KnockdownTime);
            ctx.System.Play(_target, HitSound);
            ctx.System.Popup(Loc.GetString("wg-remade-elite-impale", ("mob", ctx.Owner), ("target", _target)), _target, PopupType.LargeCaution);
        }

        return false;
    }

    public override void Stop(in WGAbilityContext ctx)
    {
        _phase = Phase.Idle;
    }
}
