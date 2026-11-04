using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using System.Numerics;
using Content.Server._Crescent.ShipShields.Components;


namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem : EntitySystem
{
    private const string ShipShieldPrototype = "ShipShield";

    //private const float DeflectionSpread = 25f;
    private const float EmitterUpdateRate = 1.5f;

    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private FixtureSystem _fixtureSystem = default!;
    [Dependency] private PhysicsSystem _physicsSystem = default!;
    [Dependency] private PvsOverrideSystem _pvsSys = default!;

    private EntityQuery<ProjectileComponent> _projectileQuery;
    private EntityQuery<ShipWeaponProjectileComponent> _shipWeaponProjectileQuery;
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateWolfgateShields(frameTime); // WOLFGATE(ShipShields): refresh hull geometry and shield health

        var query = EntityQueryEnumerator<ShipShieldEmitterComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var emitter, out var power))
        {
            ReconcileWolfgateShieldEmitter(uid, emitter); // WOLFGATE(ShipShields): recover stale handles and keep one owner per hull
            emitter.Accumulator += frameTime;

            if (emitter.Accumulator < EmitterUpdateRate)
                continue;

            if (CalculateLoadDamage(emitter) >= emitter.MaxDraw)
                emitter.Recharging = true;
            if (!power.Powered)
                emitter.Recharging = true;

            emitter.Accumulator -= EmitterUpdateRate;
            if (emitter.OverloadAccumulator > 0)
            {
                emitter.OverloadAccumulator -= EmitterUpdateRate;
            }

            float healed = emitter.HealPerSecond * EmitterUpdateRate;

            if (emitter.Recharging)
                healed *= emitter.UnpoweredBonus;

            emitter.Damage -= healed;

            // WOLFGATE(ShipShields) START: complete recharge when healing lands exactly on zero damage
            // if (emitter.Damage < 0)
            if (emitter.Damage <= 0)
            // WOLFGATE END
            {
                emitter.Damage = 0;
                if (power.Powered)
                    emitter.Recharging = false;
            }

            AdjustEmitterLoad(uid, emitter, power);

            var parent = Transform(uid).GridUid;

            if (parent == null)
                continue;

            // var filter = _station.GetInOwningStation(uid); // WOLFGATE(Performance): unused, and the lookup is costly

            if (emitter.Damage > emitter.DamageLimit)
                emitter.OverloadAccumulator = emitter.DamageOverloadTimePunishment;

            // WOLFGATE(ShipShields) START: respect the ship's manual field switch
            // if (!emitter.Recharging && emitter.Shield is null && emitter.OverloadAccumulator < 1)
            if (!emitter.Recharging && emitter.Shield is null && emitter.OverloadAccumulator < 1 && IsWolfgateShieldEnabled(parent.Value))
            // WOLFGATE END
            {
                var shield = ShieldEntity(parent.Value, uid);
                if (shield != EntityUid.Invalid)
                {
                    emitter.Shield = shield;
                    emitter.Shielded = parent.Value;
                    LogWolfgateShieldTransition(uid, parent.Value, emitter, true, "ready"); // WOLFGATE(ShipShields): record actual field transitions for diagnosis
                }
                // WOLFGATE(ShipShields) START: only announce successful startup and rate-limit power transitions per hull
                // _audio.PlayGlobal(emitter.PowerUpSound, filter, true, emitter.PowerUpSound.Params);
                if (shield != EntityUid.Invalid)
                    PlayWolfgateShieldPowerSound(uid, parent.Value, true);
                // WOLFGATE END
            }
            else if ((emitter.Recharging || emitter.OverloadAccumulator > 0) && emitter.Shield is not null || HasComp<ShipShieldDisabledGridComponent>(Transform(uid).GridUid))
            {
                // WOLFGATE(ShipShields) START: a standby emitter cannot remove another generator's field
                // UnshieldEntity(parent.Value);
                var removed = RemoveWolfgateEmitterShield(uid, emitter,
                    !power.Powered ? "power lost" : emitter.OverloadAccumulator > 0 ? "overload" : "recharging or disabled grid");
                // WOLFGATE END
                emitter.Shield = null;
                emitter.Shielded = null;
                // WOLFGATE(ShipShields) START: share the startup cooldown and use shutdown audio parameters
                if (removed && !HasComp<ShipShieldDisabledGridComponent>(Transform(uid).GridUid)) // WOLFGATE(ShipShields): announce only actual shutdown
                {
                    // _audio.PlayGlobal(emitter.PowerDownSound, filter, true, emitter.PowerUpSound.Params);
                    PlayWolfgateShieldPowerSound(uid, parent.Value, false);
                }
                // WOLFGATE END
            }
        }
    }
    public override void Initialize()
    {
        base.Initialize();
        _projectileQuery = GetEntityQuery<ProjectileComponent>();
        _shipWeaponProjectileQuery = GetEntityQuery<ShipWeaponProjectileComponent>();

        SubscribeLocalEvent<ShipShieldComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentShutdown>(OnEmitterShutdown); // Mono

        InitializeCommands();
        InitializeEmitters();
        InitializeWolfgateShields(); // WOLFGATE(ShipShields): track hull tile changes
    }

    private void OnPreventCollide(EntityUid uid, ShipShieldComponent component, ref PreventCollideEvent args)
    {
        // only handle ship weapons for now. engine update introduced physics regressions. Let's polish everything else and circle back yeah?
        // Ensuring projectiles coming froms same grid don't hit shield is handled by ProjectileGridPhaseComponent
        // WOLFGATE(ShipShields): map-parented shield still phases its ship's outgoing shots
        if (IsWolfgateShieldFriendlyProjectile(uid, args.OtherEntity) || !_shipWeaponProjectileQuery.HasComponent(args.OtherEntity) ||
        !_projectileQuery.TryGetComponent(args.OtherEntity, out var projectile) ||
        projectile.ProjectileSpent || IsWolfgateShieldUnprotectedProjectile(uid, args.OtherEntity)) // WOLFGATE(ShipShields): unpowered sectors let shots pass without changing their shooter
        {
            args.Cancelled = true;
            return;
        }

        //if (TryComp<TimedDespawnComponent>(args.OtherEntity, out var despawn))
        //    despawn.Lifetime += despawn.Lifetime;

        // I originally tried reflection but the math is too hard with the fucked coordinate system in this game (WorldRotation can be negative. Vector to Angle conversion loses information. Etc etc.)
        // Might try again at some point using just vector math with this (https://math.stackexchange.com/questions/13261/how-to-get-a-reflection-vector)
        //var deflectionVector = Transform(args.OtherEntity).WorldPosition - Transform(uid).WorldPosition;
        //var angle = _random.NextFloat(DeflectionSpread);

        //if (_random.Prob(0.5f))
        //    angle = -angle;

        //deflectionVector = new Vector2((float) (Math.Cos(angle) * deflectionVector.X - Math.Sin(angle) * deflectionVector.Y), (float) (Math.Sin(angle) * deflectionVector.X - Math.Cos(angle) * deflectionVector.Y));

        // instead of reflecting the projectile, just delete it. this works better for gameplay and intuiting what is going on in a fight.
        // why shoot the projectile again when you can just 180 its physics, tho?
        //_gun.ShootProjectile(args.OtherEntity, deflectionVector, _physicsSystem.GetMapLinearVelocity(uid), uid, null, velocity.Length());

        // WOLFGATE(ShipShields) START: apply damage only after a projectile contacts the perimeter
        // if (component.Source is { } source)
        // {
        //     var ev = new ShieldDeflectedEvent(args.OtherEntity, projectile);
        //     RaiseLocalEvent(source, ref ev);
        // }
        // WOLFGATE END
    }

    private void OnEmitterShutdown(EntityUid uid, ShipShieldEmitterComponent emitter, ComponentShutdown args) // Mono
    {
        if (emitter.Shielded != null)
        {
            // WOLFGATE(ShipShields) START: remove only the field owned by this emitter
            // UnshieldEntity(emitter.Shielded.Value);
            RemoveWolfgateEmitterShield(uid, emitter);
            // WOLFGATE END
            emitter.Shield = null;
            emitter.Shielded = null;
        }
    }

    /// <summary>
    /// Produces a shield around a grid entity, if it doesn't already exist.
    /// </summary>
    /// <param name="entity">The entity being shielded.</param>
    /// <param name="mapGrid">The map grid component of the entity being shielded.</param>
    /// <param name="source">A shield generator or similar providing the shield for the entity</param>
    /// <returns>The shield entity.</returns>
    private EntityUid ShieldEntity(EntityUid entity, EntityUid? source = null, MapGridComponent? mapGrid = null)
    {
        // WOLFGATE(ShipShields) START: block normal and administrative shield deployment throughout FTL
        if (IsWolfgateShieldFtlLocked(entity))
            return EntityUid.Invalid;
        // WOLFGATE END
        // WOLFGATE(ShipShields) START: discard stale grid fields and reserve active fields for their owner
        // if (TryComp<ShipShieldedComponent>(entity, out var existingShielded))
        //     return existingShielded.Shield;
        if (TryComp<ShipShieldedComponent>(entity, out var existingShielded))
        {
            if (IsWolfgateShieldLive(existingShielded.Shield))
                return source == null || existingShielded.Source == source ? existingShielded.Shield : EntityUid.Invalid;
            TryQueueDel(existingShielded.Shield);
            RemComp<ShipShieldedComponent>(entity);
        }
        // WOLFGATE END

        if (!Resolve(entity, ref mapGrid, false) || HasComp<ShipShieldDisabledGridComponent>(Transform(entity).GridUid))
            return EntityUid.Invalid;

        var prototype = ShipShieldPrototype;

        var shield = Spawn(prototype, Transform(entity).Coordinates);
        var shieldPhysics = EnsureComp<PhysicsComponent>(shield);
        var shieldComp = EnsureComp<ShipShieldComponent>(shield);
        shieldComp.Shielded = entity;
        shieldComp.Source = source;

        // Copy shield color from the generator to the shield visuals
        var shieldVisuals = EnsureComp<ShipShieldVisualsComponent>(shield);
        if (source != null && TryComp<ShipShieldEmitterComponent>(source.Value, out var emitter))
        {
            shieldVisuals.ShieldColor = emitter.ShieldColor;
            Dirty(shield, shieldVisuals);
        }

        // WOLFGATE(ShipShields) START: replace oval and interior blocker with the padded hull perimeter
        // var gridCenter = new EntityCoordinates(entity, mapGrid.LocalAABB.Center);
        // _transformSystem.SetCoordinates(shield, gridCenter);
        // _transformSystem.SetWorldRotation(shield, _transformSystem.GetWorldRotation(entity));

        // var chain = GenerateOvalFixture(shield, "shield", shieldPhysics, mapGrid, shieldVisuals.Padding);

        // List<Vector2> roughPoly = new();

        // var interval = chain.Count / PhysicsConstants.MaxPolygonVertices;

        // int i = 0;

        // while (i < PhysicsConstants.MaxPolygonVertices)
        // {
        //     roughPoly.Add(chain.Vertices[i * interval]);
        //     i++;
        // }

        // var internalPoly = new PolygonShape();
        // internalPoly.Set(roughPoly);

        // _fixtureSystem.TryCreateFixture(shield, internalPoly, "internalShield",
        //     hard: true,
        //     collisionLayer: (int)CollisionGroup.BulletImpassable, // Mono - Only try to block bullets
        //     body: shieldPhysics);


        CreateWolfgateShieldHull(shield, entity, mapGrid, shieldPhysics);
        BeginWolfgateShieldFormation(shield, entity);
        // WOLFGATE END

        _physicsSystem.WakeBody(shield, body: shieldPhysics);
        _physicsSystem.SetSleepingAllowed(shield, shieldPhysics, false);

        // WOLFGATE(Performance) START: nearby sessions only, not a global override
        // _pvsSys.AddGlobalOverride(shield);
        AddWolfgateShieldPvs(shield, entity);
        // WOLFGATE END

        var shieldedComp = EnsureComp<ShipShieldedComponent>(entity);
        shieldedComp.Shield = shield;
        shieldedComp.Source = source;

        return shield;
    }

    private bool UnshieldEntity(EntityUid uid, ShipShieldedComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        // WOLFGATE(ShipShields) START: dissipate visually after protection stops immediately
        EndWolfgateShieldAppearance(component.Shield, uid);
        if (TryComp<PhysicsComponent>(component.Shield, out var physics))
            _physicsSystem.SetCanCollide(component.Shield, false, body: physics);
        // WOLFGATE END
        TryQueueDel(component.Shield);
        RemComp<ShipShieldedComponent>(uid);
        return true;
    }

    private ChainShape GenerateOvalFixture(EntityUid uid, string name, PhysicsComponent physics, MapGridComponent mapGrid, float padding)
    {
        float radius;
        float scale;
        var scaleX = true;

        var height = mapGrid.LocalAABB.Height + padding;
        var width = mapGrid.LocalAABB.Width + padding;

        if (width > height)
        {
            radius = 0.5f * height;
            scale = width / height;
        }
        else
        {
            radius = 0.5f * width;
            scale = height / width;
            scaleX = false;
        }

        var chain = new ChainShape();

        chain.CreateLoop(Vector2.Zero, radius);

        for (int i = 0; i < chain.Vertices.Length; i++)
        {
            if (scaleX)
            {
                chain.Vertices[i].X *= scale;
            }
            else
            {
                chain.Vertices[i].Y *= scale;
            }
        }

        _fixtureSystem.TryCreateFixture(uid, chain, name,
            hard: false,
            collisionLayer: (int)CollisionGroup.BulletImpassable, // Mono - Only blocks bullets
            body: physics);

        return chain;
    }

    [ByRefEvent]
    public record struct ShieldDeflectedEvent(EntityUid Deflected, ProjectileComponent Projectile)
    {

    }
}
