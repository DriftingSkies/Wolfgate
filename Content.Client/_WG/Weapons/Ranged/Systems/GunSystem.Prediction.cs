using System.Numerics;
using Content.Shared._Goobstation.Wizard.Projectiles;
using Content.Shared._RMC14.Random;
using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using Content.Shared.Clumsy;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.GameStates;
using Robust.Shared.Map;

namespace Content.Client.Weapons.Ranged.Systems;

/// <summary>
/// Fires predicted copies of the server's projectiles, so shots appear the moment they're fired.
/// </summary>
public sealed partial class GunSystem
{
    [Dependency] private IClientGameStateManager _gameState = default!;

    /// <summary>
    /// Recoil and spawn state for one call to Shoot. Mirrors the server's Shoot, so predicted copies fly the same
    /// way and line up with the server's projectiles slot by slot.
    /// </summary>
    private sealed class PredictedVolley
    {
        public EntityUid Gun;
        public GunComponent GunComp = default!;
        public EntityUid? User;
        public MapCoordinates FromMap;
        public EntityCoordinates From;
        public Vector2 Direction;
        public Angle BaseAngle;
        public Xoroshiro64S Random;
        public float Offset = -1f;
        public int Count;

        /// <summary>Set only on the first-time tick of a request this client sent.</summary>
        public PredictedShot? Shot;
        public EntityCoordinates FromEnt;
        public Vector2 GunVelocity;
    }

    /// <summary>
    /// Sets up one call to Shoot: always the recoil state, and the spawn state too when this client is the shooter
    /// and can predict what the server is about to fire.
    /// </summary>
    private PredictedVolley BeginVolley(EntityUid gunUid, GunComponent gun, EntityCoordinates fromCoordinates, EntityCoordinates toCoordinates, EntityUid? user, int count)
    {
        var fromMap = TransformSystem.ToMapCoordinates(fromCoordinates);
        var direction = TransformSystem.ToMapCoordinates(toCoordinates).Position - fromMap.Position;
        var volley = new PredictedVolley
        {
            Gun = gunUid,
            GunComp = gun,
            User = user,
            FromMap = fromMap,
            From = fromCoordinates,
            Direction = direction,
            BaseAngle = direction.ToAngle(),
            Random = GetRecoilRandom(gunUid),
            Count = count,
        };

        // Repredicted ticks only replay recoil; the copies already exist.
        if (!GunPrediction || !_gameState.IsPredictionEnabled || !Timing.IsFirstTimePredicted
            || PredictedShotContext is not { } shot || shot.Gun != gunUid)
        {
            return volley;
        }

        // A clumsy shooter's gun fails on a server-side roll the client can't predict, so leave those shots alone.
        if (user != null && TryComp<ClumsyComponent>(user.Value, out var clumsy) && clumsy.ClumsyGuns && !gun.ClumsyProof)
            return volley;

        // Same origin and inherited velocity as the server's Shoot.
        volley.Shot = shot;
        volley.FromEnt = MapManager.TryFindGridAt(fromMap, out var gridUid, out _)
            ? TransformSystem.WithEntityId(fromCoordinates, gridUid)
            : TransformSystem.ToCoordinates(fromMap);
        volley.GunVelocity = Physics.GetMapLinearVelocity(gunUid) - Physics.GetMapLinearVelocity(volley.FromEnt);
        return volley;
    }

    /// <summary>
    /// Advances recoil for the next round, the same way the server does.
    /// </summary>
    private void NextRound(PredictedVolley volley)
    {
        volley.Offset = volley.Offset == -1f ? 0f : volley.Offset + 1f / volley.Count;
        var angle = GetRecoilAngle(Timing.CurTime, volley.Gun, volley.GunComp, volley.Direction.ToAngle(), ref volley.Random);
        var toMap = volley.FromMap.Position + angle.ToVec() * volley.Direction.Length();
        volley.Direction = toMap - volley.FromMap.Position;
    }

    /// <summary>
    /// Fires a predicted copy of what a cartridge shoots.
    /// </summary>
    private void PredictCartridge(PredictedVolley volley, CartridgeAmmoComponent cartridge)
    {
        if (volley.Shot == null)
            return;

        var round = Spawn(cartridge.Prototype, volley.FromEnt);
        if (!PredictRound(volley, round))
            Del(round);
    }

    /// <summary>
    /// Fires ammo that shoots itself as a predicted copy. Returns false if the caller should clean it up.
    /// </summary>
    private bool PredictAmmo(PredictedVolley volley, EntityUid ammo)
    {
        if (volley.Shot == null)
            return false;

        // The server fires networked ammo as the real item, so only keep its slots in step.
        if (!IsClientSide(ammo))
        {
            ReserveRound(volley, ammo);
            return false;
        }

        return PredictRound(volley, ammo);
    }

    /// <summary>
    /// Keeps a slot for an item the server fires as a projectile but the client doesn't predict.
    /// </summary>
    private void ReserveSlot(PredictedVolley volley, EntityUid? uid)
    {
        if (volley.Shot != null && uid != null && IsProjectileShot(uid.Value))
            volley.Shot.Slots.Add(0);
    }

    /// <summary>
    /// Keeps slots for a networked round and the pellets the server spawns alongside it.
    /// </summary>
    private void ReserveRound(PredictedVolley volley, EntityUid round)
    {
        ReserveSlot(volley, round);
        if (!TryComp<ProjectileSpreadComponent>(round, out var spread) || !ProtoManager.TryIndex(spread.Proto, out var pellet))
            return;

        for (var i = 1; i < spread.Count; i++)
        {
            if (!pellet.TryComp<HitscanAmmoComponent>(out _, Factory) && pellet.TryComp<ProjectileComponent>(out _, Factory))
                volley.Shot!.Slots.Add(0);
        }
    }

    /// <summary>
    /// Mirrors the server's CreateAndFireProjectiles. Returns whether the round itself was fired.
    /// </summary>
    private bool PredictRound(PredictedVolley volley, EntityUid round)
    {
        if (!TryComp<ProjectileSpreadComponent>(round, out var spread))
            return PredictProjectile(volley, round, volley.Direction);

        var ev = new GunGetAmmoSpreadEvent(spread.Spread);
        RaiseLocalEvent(volley.Gun, ref ev);
        var angles = LinearSpread(volley.BaseAngle - ev.Spread / 2, volley.BaseAngle + ev.Spread / 2, spread.Count);

        var fired = PredictProjectile(volley, round, angles[0].ToVec());
        for (var i = 1; i < spread.Count; i++)
        {
            var pellet = Spawn(spread.Proto, volley.FromEnt);
            if (!PredictProjectile(volley, pellet, angles[i].ToVec()))
                Del(pellet);
        }

        return fired;
    }

    /// <summary>
    /// Mirrors the server's ShootOrThrow for one predicted projectile. Returns false if nothing was fired.
    /// </summary>
    private bool PredictProjectile(PredictedVolley volley, EntityUid uid, Vector2 direction)
    {
        // Hitscan rounds are drawn and then cleaned up; neither they nor thrown items take a slot.
        if (HasComp<HitscanAmmoComponent>(uid))
        {
            PredictHitscan(volley, uid, direction);
            return false;
        }

        if (!TryComp<ProjectileComponent>(uid, out var projectile))
            return false;

        if (!CanPredictProjectile(uid, projectile))
        {
            volley.Shot!.Slots.Add(0);
            return false;
        }

        if (volley.GunComp.Target is { } target && !TerminatingOrDeleted(target))
            EnsureComp<TargetedProjectileComponent>(uid).Target = target;

        ShootProjectile(uid, direction, volley.GunVelocity, volley.Gun, volley.User, volley.GunComp.ProjectileSpeedModified, volley.Offset);
        volley.Shot!.Slots.Add(uid.Id);
        return true;
    }

    /// <summary>Whether this client drew the beams for the shot it last requested.</summary>
    public bool DrewHitscan { get; private set; }

    /// <summary>
    /// Runs the shared hitscan trace locally, so the shooter sees the beam on the tick they fired it.
    /// </summary>
    private void PredictHitscan(PredictedVolley volley, EntityUid? hitscan, Vector2 direction)
    {
        if (volley.Shot == null || hitscan == null || !CanPredictHitscan(hitscan.Value))
            return;

        ShootHitscan(hitscan.Value, volley.From, direction, volley.Gun, volley.User, volley.GunComp.Target);
        DrewHitscan = true;
    }

    /// <summary>
    /// Whether the server will fire this entity as a projectile, which is what takes up a slot.
    /// </summary>
    private bool IsProjectileShot(EntityUid uid)
    {
        return !HasComp<HitscanAmmoComponent>(uid) && HasComp<ProjectileComponent>(uid);
    }

    /// <summary>
    /// Only predicts projectiles that vanish on impact and whose hit logic the client can mirror, so hiding the
    /// server's copy never hides something that stays in the world.
    /// </summary>
    private bool CanPredictProjectile(EntityUid uid, ProjectileComponent projectile)
    {
        return projectile.DeleteOnCollide
               && projectile.NoDamageDelete // a kinetic bolt outlives hits the client can't see coming
               && projectile.PenetrationThreshold == FixedPoint2.Zero
               && !HasComp<EmbeddableProjectileComponent>(uid)
               && !HasComp<HomingProjectileComponent>(uid)
               && !HasComp<IgnorePredictionHideComponent>(uid);
    }
}
