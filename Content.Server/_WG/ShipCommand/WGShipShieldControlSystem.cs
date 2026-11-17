using Content.Server.Administration.Logs;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipCommand;
using Content.Shared._WG.ShipShields;
using Content.Shared.Database;
using Content.Shared.UserInterface;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A commanded ship's staff-controlled shield generator: only the staff list and up may open it, and they may set the
/// field to any strength. Power at full load stays the same, and the field takes as long to refill, at any strength.
/// </summary>
public sealed class WGShipShieldControlSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private WGShipCommandSystem _command = default!;

    /// <summary>The weakest a field can be set to, which keeps its power maths finite.</summary>
    public const float MinStrength = 1f;

    /// <summary>The most a field can be set to, which keeps the float maths well behaved.</summary>
    public const float MaxStrength = 1_000_000_000f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipShieldStrengthControlComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        Subs.BuiEvents<WGShipShieldStrengthControlComponent>(WGShipShieldUiKey.Key,
            subs => subs.Event<WGShipShieldSetStrengthMessage>(OnSetStrength));
    }

    /// <summary>Whether someone stands high enough with the ship to work this generator; true for any other generator, and off a commanded ship.</summary>
    public bool CanControl(EntityUid user, EntityUid generator)
    {
        if (!HasComp<WGShipShieldStrengthControlComponent>(generator)
            || Transform(generator).GridUid is not { } grid || !HasComp<WGShipCommandComponent>(grid))
            return true;

        return _command.GetAuthority(user, grid) >= WGShipAuthority.Staff;
    }

    private void OnOpenAttempt(Entity<WGShipShieldStrengthControlComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!CanControl(args.User, ent))
            args.Cancel();
    }

    private void OnSetStrength(Entity<WGShipShieldStrengthControlComponent> ent, ref WGShipShieldSetStrengthMessage args)
    {
        if (TryComp<ShipShieldEmitterComponent>(ent, out var emitter) && CanControl(args.Actor, ent)
            && SetStrength((ent, emitter), args.Strength))
        {
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(args.Actor):actor} set {ToPrettyString(ent.Owner):generator} to a shield strength of {emitter.DamageLimit}");
        }
    }

    /// <summary>
    /// Sets how much damage the field takes before it overloads, keeping its health as a share and its time to refill.
    /// Power demand peaks at the same draw as before, so a strong field doesn't brown out the ship. False for a
    /// nonsense value.
    /// </summary>
    public bool SetStrength(Entity<ShipShieldEmitterComponent> ent, float strength)
    {
        if (!float.IsFinite(strength) || strength < MinStrength)
            return false;

        var emitter = ent.Comp;
        strength = MathF.Min(strength, MaxStrength);
        var ratio = strength / emitter.DamageLimit;
        emitter.Damage *= ratio;
        emitter.HealPerSecond *= ratio;
        emitter.DamageLimit = strength;
        emitter.PowerModifier = (float) (emitter.MaxDraw / Math.Pow(strength, emitter.DamageExp));
        return true;
    }
}
