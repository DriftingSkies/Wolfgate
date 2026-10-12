using Content.Shared._Mono.Weapons.Hitscan.Components;
using Content.Shared._RMC14.Random;
using Content.Shared.Item;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Mech.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Ranged.Systems;

/// <summary>
/// Links the projectiles a shooter's client predicts to the server's copies, and seeds recoil so both
/// sides fire the same way.
/// </summary>
public abstract partial class SharedGunSystem
{
    /// <summary>
    /// The shoot request being processed, so Shoot can link fired projectiles to the shooter's predicted copies.
    /// </summary>
    protected PredictedShot? PredictedShotContext;

    /// <summary>
    /// Handles a player's shoot request, on the server and when the shooter's client predicts it.
    /// Returns one slot per projectile fired, holding the client-side id of its predicted copy or 0,
    /// or null when nothing was predicted.
    /// </summary>
    public List<int>? ShootRequested(NetEntity gun, NetCoordinates coordinates, NetEntity? target, List<int>? shot, ICommonSession session, bool predicted = false)
    {
        var user = session.AttachedEntity;
        if (user == null || !_combatMode.IsInCombatMode(user))
            return null;

        if (TryComp<MechPilotComponent>(user.Value, out var mechPilot))
            user = mechPilot.Mech;

        if (!TryGetGun(user.Value, out var ent) || HasComp<ItemComponent>(user) || ent.Owner != GetEntity(gun))
            return null;

        ent.Comp.ShootCoordinates = GetCoordinates(coordinates);

        // Goob edit - a locked burst keeps its target
        if (ent.Comp.Target == null || !ent.Comp.BurstActivated || !ent.Comp.LockOnTargetBurst)
            ent.Comp.Target = GetEntity(target);

        var predictedShot = new PredictedShot(session, ent, shot, predicted);
        PredictedShotContext = predictedShot;
        try
        {
            AttemptShoot(user.Value, ent, ent.Comp);
        }
        finally
        {
            PredictedShotContext = null;
        }

        return predictedShot.Slots.Exists(id => id != 0) ? predictedShot.Slots : null;
    }

    /// <summary>
    /// Recoil RNG for one volley, seeded from the tick and gun so the shooter's client predicts the same spread.
    /// </summary>
    protected Xoroshiro64S GetRecoilRandom(EntityUid gunUid)
    {
        return new Xoroshiro64S(((long) Timing.CurTick.Value << 32) | (uint) GetNetEntity(gunUid).Id);
    }

    /// <summary>
    /// Advances the gun's recoil for one round and returns the round's direction.
    /// </summary>
    protected Angle GetRecoilAngle(TimeSpan curTime, EntityUid gunUid, GunComponent component, Angle direction, ref Xoroshiro64S random)
    {
        var timeSinceLastFire = (curTime - component.LastFire).TotalSeconds;
        var newTheta = MathHelper.Clamp(component.CurrentAngle.Theta + component.AngleIncreaseModified.Theta - component.AngleDecayModified.Theta * timeSinceLastFire, component.MinAngleModified.Theta, component.MaxAngleModified.Theta);
        component.CurrentAngle = new Angle(newTheta);
        component.LastFire = component.NextFire;
        DirtyField(gunUid, component, nameof(GunComponent.CurrentAngle));
        DirtyField(gunUid, component, nameof(GunComponent.LastFire));

        // Convert it so angle can go either side.
        var spread = component.CurrentAngle.Theta * random.NextFloat(-0.5f, 0.5f);
        DebugTools.Assert(spread <= component.MaxAngleModified.Theta);
        return new Angle(direction.Theta + spread);
    }

    /// <summary>
    /// Gets a linear spread of angles between start and end.
    /// </summary>
    /// <param name="start">Start angle in degrees</param>
    /// <param name="end">End angle in degrees</param>
    /// <param name="intervals">How many shots there are</param>
    protected static Angle[] LinearSpread(Angle start, Angle end, int intervals)
    {
        var angles = new Angle[intervals];
        DebugTools.Assert(intervals > 1);

        for (var i = 0; i <= intervals - 1; i++)
        {
            angles[i] = new Angle(start + (end - start) * i / (intervals - 1));
        }

        return angles;
    }

    /// <summary>
    /// Whether the shooter's client drew this shot's beams itself, so the server leaves them out of its own.
    /// The client's word decides it, which keeps the two sides from disagreeing over what is predictable.
    /// </summary>
    protected bool IsPredictedHitscan(EntityUid gunUid)
    {
        return GunPrediction && PredictedShotContext is { Predicting: true } shot && shot.Gun == gunUid;
    }

    /// <summary>
    /// Predicts the beams of any raycast hitscan, diffraction included. Range doesn't matter: the client has
    /// everything the player can see, so a beam it overshoots only runs long off-screen. Jumps and reflections
    /// follow from the server as their own beams.
    /// </summary>
    public bool CanPredictHitscan(EntityUid hitscan)
    {
        return (HasComp<HitscanBasicRaycastComponent>(hitscan) || HasComp<HitscanMultiRaycastComponent>(hitscan))
               && HasComp<HitscanBasicVisualsComponent>(hitscan);
    }

    /// <summary>
    /// Links the projectiles fired for one shoot request to the copies the shooter's client predicted.
    /// </summary>
    protected sealed class PredictedShot(ICommonSession shooter, EntityUid gun, List<int>? clientIds, bool predicting)
    {
        /// <summary>Whether the shooter's client draws this shot's own effects.</summary>
        public readonly bool Predicting = predicting;

        /// <summary>The player who sent the request.</summary>
        public readonly ICommonSession Shooter = shooter;

        /// <summary>The gun being fired, so a nested shot from another gun is left alone.</summary>
        public readonly EntityUid Gun = gun;

        /// <summary>Server: the client's predicted ids, one slot per fired projectile, 0 where it predicted nothing.</summary>
        public readonly List<int>? ClientIds = clientIds;

        /// <summary>Client: the slots filled while firing, sent to the server with the request.</summary>
        public readonly List<int> Slots = new();

        /// <summary>Server: the slot of the next fired projectile.</summary>
        public int Next;
    }
}
