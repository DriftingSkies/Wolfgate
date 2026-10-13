using System.Linq;
using System.Numerics;
using Content.Client.Animations;
using Content.Client.Gameplay;
using Content.Client.Items;
using Content.Client.Projectiles;
using Content.Client.Weapons.Ranged.Components;
using Content.Shared.Camera;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Hands.Components;
using Content.Shared._Mono.FireControl;
using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using GunUseGunOriginComponent = Content.Shared._WF.Vehicles.Weapons.GunUseGunOriginComponent; // WOLFGATE(Vehicles)
using Content.Shared.Mech.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Animations;
using Robust.Shared.Audio;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using SharedGunSystem = Content.Shared.Weapons.Ranged.Systems.SharedGunSystem;
using TimedDespawnComponent = Robust.Shared.Spawners.TimedDespawnComponent;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem : SharedGunSystem
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private AnimationPlayerSystem _animPlayer = default!;
    [Dependency] private InputSystem _inputSystem = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private Robust.Client.Physics.PhysicsSystem _physics = default!;

    public static readonly EntProtoId HitscanProto = "HitscanEffect";

    public bool SpreadOverlay
    {
        get => _spreadOverlay;
        set
        {
            if (_spreadOverlay == value)
                return;

            _spreadOverlay = value;
            var overlayManager = IoCManager.Resolve<IOverlayManager>();

            if (_spreadOverlay)
            {
                overlayManager.AddOverlay(new GunSpreadOverlay(
                    EntityManager,
                    _eyeManager,
                    Timing,
                    _inputManager,
                    _player,
                    this,
                    TransformSystem));
            }
            else
            {
                overlayManager.RemoveOverlay<GunSpreadOverlay>();
            }
        }
    }

    private bool _spreadOverlay;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        UpdatesBefore.Add(typeof(Robust.Shared.Physics.Systems.SharedPhysicsSystem)); // WOLFGATE(Weapons): predicted copies move on the tick they're fired, like the server's
        SubscribeLocalEvent<AmmoCounterComponent, ItemStatusCollectMessage>(OnAmmoCounterCollect);
        SubscribeLocalEvent<AmmoCounterComponent, UpdateClientAmmoEvent>(OnUpdateClientAmmo);
        SubscribeAllEvent<MuzzleFlashEvent>(OnMuzzleFlash);

        // Plays animated effects on the client.
        SubscribeAllEvent<HitscanEvent>(OnHitscan); // WOLFGATE(Weapons): also draws the beams this client predicts locally

        InitializeMagazineVisuals();
        InitializeSpentAmmo();
    }

    private void OnUpdateClientAmmo(EntityUid uid, AmmoCounterComponent ammoComp, ref UpdateClientAmmoEvent args)
    {
        UpdateAmmoCount(uid, ammoComp);
    }

    private void OnMuzzleFlash(MuzzleFlashEvent args)
    {
        var gunUid = GetEntity(args.Uid);

        // Find the gun's holder by checking its parent entity
        // If the gun is being held, its parent will be a hand container, and the hand container's parent will be the holder
        EntityUid? holder = null;

        if (EntityManager.TryGetComponent(gunUid, out TransformComponent? gunXform) && gunXform.ParentUid != EntityUid.Invalid)
        {
            var parent = gunXform.ParentUid;
            // Check if the parent has HandsComponent (it's the holder)
            if (HasComp<HandsComponent>(parent))
            {
                holder = parent;
            }
            else
            {
                // The parent might be a hand container, check its parent
                var parentXform = Transform(parent);
                if (parentXform.ParentUid != EntityUid.Invalid && HasComp<HandsComponent>(parentXform.ParentUid))
                {
                    holder = parentXform.ParentUid;
                }
            }
        }

        CreateEffect(gunUid, args, holder);
    }

    private void OnHitscan(HitscanEvent ev)
    {
        // ALL I WANT IS AN ANIMATED EFFECT

        // TODO EFFECTS
        // This is very jank
        // because the effect consists of three unrelatd entities, the hitscan beam can be split appart.
        // E.g., if a grid rotates while part of the beam is parented to the grid, and part of it is parented to the map.
        // Ideally, there should only be one entity, with one sprite that has multiple layers
        // Or at the very least, have the other entities parented to the same entity to make sure they stick together.
        foreach (var a in ev.Sprites)
        {
            if (a.Sprite is not SpriteSpecifier.Rsi rsi)
                continue;

            var coords = GetCoordinates(a.coordinates);

            if (!TryComp(coords.EntityId, out TransformComponent? relativeXform))
                continue;

            var ent = Spawn(HitscanProto, coords);
            var sprite = Comp<SpriteComponent>(ent);

            var xform = Transform(ent);
            var targetWorldRot = a.angle + _xform.GetWorldRotation(relativeXform);
            var delta = targetWorldRot - _xform.GetWorldRotation(xform);
            _xform.SetLocalRotationNoLerp(ent, xform.LocalRotation + delta, xform);

            sprite[EffectLayers.Unshaded].AutoAnimated = false;
            sprite.LayerSetSprite(EffectLayers.Unshaded, rsi);
            sprite.LayerSetState(EffectLayers.Unshaded, rsi.RsiState);
            sprite.Scale = new Vector2(a.Distance, 1f);
            sprite[EffectLayers.Unshaded].Visible = true;

            var anim = new Animation()
            {
                Length = TimeSpan.FromSeconds(0.48f),
                AnimationTracks =
                {
                    new AnimationTrackSpriteFlick()
                    {
                        LayerKey = EffectLayers.Unshaded,
                        KeyFrames =
                        {
                            new AnimationTrackSpriteFlick.KeyFrame(rsi.RsiState, 0f),
                        }
                    }
                }
            };

            _animPlayer.Play(ent, anim, "hitscan-effect");
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime); // WOLFGATE(Weapons): keeps the Mono multi-shot offset in step with the server

        if (!Timing.IsFirstTimePredicted)
            return;

        var entityNull = _player.LocalEntity;

        if (entityNull == null || !TryComp<CombatModeComponent>(entityNull, out var combat) || !combat.IsInCombatMode)
        {
            return;
        }

        var entity = entityNull.Value;

        if (TryComp<MechPilotComponent>(entity, out var mechPilot)) // Goobstation
            entity = mechPilot.Mech;

        if (!TryGetGun(entity, out var gunUid))
        {
            return;
        }

        var useKey = gunUid.Comp.UseKey ? EngineKeyFunctions.Use : EngineKeyFunctions.UseSecondary;

        if (_inputSystem.CmdStates.GetState(useKey) != BoundKeyState.Down && !gunUid.Comp.BurstActivated)
        {
            if (gunUid.Comp.ShotCounter != 0)
                EntityManager.RaisePredictiveEvent(new RequestStopShootEvent { Gun = GetNetEntity(gunUid) });
            return;
        }

        if (gunUid.Comp.NextFire > Timing.CurTime)
            return;

        var mousePos = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);

        if (mousePos.MapId == MapId.Nullspace)
        {
            if (gunUid.Comp.ShotCounter != 0)
                EntityManager.RaisePredictiveEvent(new RequestStopShootEvent { Gun = GetNetEntity(gunUid) });

            return;
        }

        // Define target coordinates relative to gun entity, so that network latency on moving grids doesn't fuck up the target location.
        // WOLFGATE(Vehicles) START: an operator on another map aims relative to the grid under the gun, which is on the map the cursor is over
        // upstream original, kept for merges:
        // var coordinates = TransformSystem.ToCoordinates(entity, mousePos);
        var coordinates = GetAimCoordinates(gunUid, entity, mousePos);
        // WOLFGATE END

        NetEntity? target = null;
        if (_state.CurrentState is GameplayStateBase screen)
            target = GetNetEntity(screen.GetDamageableClickedEntity(mousePos)); // Goob edit

        Log.Debug($"Sending shoot request tick {Timing.CurTick} / {Timing.CurTime}");

        if (_player.LocalSession is not { } session)
            return;

        var predicting = GunPrediction && _gameState.IsPredictionEnabled; // WOLFGATE(Weapons)
        DrewHitscan = false; // WOLFGATE(Weapons)
        var projectiles = ShootRequested(GetNetEntity(gunUid), GetNetCoordinates(coordinates), target, null, (Robust.Shared.Player.ICommonSession)session, predicting); // WOLFGATE(Weapons)

        EntityManager.RaisePredictiveEvent(new RequestShootEvent()
        {
            Target = target,
            Coordinates = GetNetCoordinates(coordinates),
            Gun = GetNetEntity(gunUid),
            Shot = projectiles, // WOLFGATE(Weapons): one slot per fired projectile, holding its predicted copy's id
            Predicted = DrewHitscan, // WOLFGATE(Weapons): this client already drew this shot's beams
        });
    }

    public override void Shoot(EntityUid gunUid, GunComponent gun, List<(EntityUid? Entity, IShootable Shootable)> ammo,
        EntityCoordinates fromCoordinates, EntityCoordinates toCoordinates, out bool userImpulse, EntityUid? user = null, bool throwItems = false)
    {
        userImpulse = true;

        // Rather than splitting client / server for every ammo provider it's easier
        // to just delete the spawned entities. This is for programmer sanity despite the wasted perf.
        // This also means any ammo specific stuff can be grabbed as necessary.
        var direction = TransformSystem.ToMapCoordinates(fromCoordinates).Position - TransformSystem.ToMapCoordinates(toCoordinates).Position;
        var worldAngle = direction.ToAngle().Opposite();
        var volley = BeginVolley(gunUid, gun, fromCoordinates, toCoordinates, user, ammo.Count); // WOLFGATE(Weapons): recoil and predicted copies

        foreach (var (ent, shootable) in ammo)
        {
            NextRound(volley); // WOLFGATE(Weapons)

            if (throwItems)
            {
                ReserveSlot(volley, ent); // WOLFGATE(Weapons)
                Recoil(user, direction, gun.CameraRecoilScalarModified);
                if (IsClientSide(ent!.Value))
                    Del(ent.Value);
                else
                    RemoveShootable(ent.Value);
                continue;
            }

            switch (shootable)
            {
                case CartridgeAmmoComponent cartridge:
                    if (!cartridge.Spent)
                    {
                        PredictCartridge(volley, cartridge); // WOLFGATE(Weapons)
                        SetCartridgeSpent(ent!.Value, cartridge, true);
                        MuzzleFlash(gunUid, cartridge, worldAngle, user);
                        Audio.PlayPredicted(cartridge.SoundGunshot ?? gun.SoundGunshotModified, gunUid, user);
                        Recoil(user, direction, gun.CameraRecoilScalarModified);
                        // TODO: Can't predict entity deletions.
                        //if (cartridge.DeleteOnSpawn)
                        //    Del(cartridge.Owner);
                    }
                    else
                    {
                        userImpulse = false;
                        Audio.PlayPredicted(gun.SoundEmpty, gunUid, user);
                    }

                    if (IsClientSide(ent!.Value))
                        Del(ent.Value);

                    break;
                case AmmoComponent newAmmo:
                    MuzzleFlash(gunUid, newAmmo, worldAngle, user);
                    Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                    Recoil(user, direction, gun.CameraRecoilScalarModified);
                    // WOLFGATE(Weapons) START: fired as its own predicted copy
                    if (PredictAmmo(volley, ent!.Value))
                        break;
                    // WOLFGATE END
                    if (IsClientSide(ent!.Value))
                        Del(ent.Value);
                    else
                        RemoveShootable(ent.Value);
                    break;
                case HitscanAmmoComponent:
                    Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                    Recoil(user, direction, gun.CameraRecoilScalarModified);
                    PredictHitscan(volley, ent, volley.Direction); // WOLFGATE(Weapons): draw the beam now rather than waiting for the server's
                    // WOLFGATE(Weapons) START: the server fires the hitscan, so don't leak the client-side ammo
                    if (ent != null && IsClientSide(ent.Value))
                        QueueDel(ent.Value);
                    // WOLFGATE END
                    break;
            }
        }
    }

    private void Recoil(EntityUid? user, Vector2 recoil, float recoilScalar)
    {
        if (!Timing.IsFirstTimePredicted || user == null || recoil == Vector2.Zero || recoilScalar == 0)
            return;

        _recoil.KickCamera(user.Value, recoil.Normalized() * 0.5f * recoilScalar);
    }

    protected override void Popup(string message, EntityUid? uid, EntityUid? user)
    {
        if (uid == null || user == null || !Timing.IsFirstTimePredicted)
            return;

        PopupSystem.PopupEntity(message, uid.Value, user.Value);
    }

    protected override void CreateEffect(EntityUid gunUid, MuzzleFlashEvent message, EntityUid? user = null)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        // EntityUid check added to stop throwing exceptions due to https://github.com/space-wizards/space-station-14/issues/28252
        // TODO: Check to see why invalid entities are firing effects.
        if (gunUid == EntityUid.Invalid)
        {
            Log.Debug($"Invalid Entity sent MuzzleFlashEvent (proto: {message.Prototype}, gun: {ToPrettyString(gunUid)})");
            return;
        }

        var gunXform = Transform(gunUid);
        var gridUid = gunXform.GridUid;
        EntityCoordinates coordinates;

        if (TryComp(gridUid, out MapGridComponent? mapGrid))
        {
            coordinates = new EntityCoordinates(gridUid.Value, _maps.LocalToGrid(gridUid.Value, mapGrid, gunXform.Coordinates));
        }
        else if (gunXform.MapUid != null)
        {
            coordinates = new EntityCoordinates(gunXform.MapUid.Value, TransformSystem.GetWorldPosition(gunXform));
        }
        else
        {
            return;
        }

        var ent = Spawn(message.Prototype, coordinates);
        TransformSystem.SetWorldRotationNoLerp(ent, message.Angle);

        // WOLFGATE(Vehicles) START: a mounted gun's flash follows the gun at its muzzle, not an operator on another map
        Vector2? mountedFlashOffset = null;
        if (HasComp<GunUseGunOriginComponent>(gunUid))
        {
            user = gunUid;
            mountedFlashOffset = GetMountedFlashOffset(gunUid, gunXform, message.Angle);
        }
        // WOLFGATE END

        // Don't track muzzle flash to user if this is a fire control console weapon (ship artillery)
        // Note: FireControllableComponent is server-only, so we can't check it on client
        // The server will handle this check
        if (user != null)
        {
            var track = EnsureComp<TrackUserComponent>(ent);
            track.User = user;
            track.Offset = mountedFlashOffset ?? Vector2.UnitX / 2f; // WOLFGATE(Vehicles): at the muzzle of a mounted gun
        }

        var lifetime = 0.4f;

        if (TryComp<TimedDespawnComponent>(gunUid, out var despawn))
        {
            lifetime = despawn.Lifetime;
        }

        var anim = new Animation()
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(1f), 0),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(0f), lifetime)
                    }
                }
            }
        };

        _animPlayer.Play(ent, anim, "muzzle-flash");
        if (!TryComp(gunUid, out PointLightComponent? light))
        {
            light = Factory.GetComponent<PointLightComponent>();
            light.NetSyncEnabled = false;
            AddComp(gunUid, light);
        }

        Lights.SetEnabled(gunUid, true, light);
        Lights.SetRadius(gunUid, 2f, light);
        Lights.SetColor(gunUid, Color.FromHex("#cc8e2b"), light);
        Lights.SetEnergy(gunUid, 5f, light);

        var animTwo = new Animation()
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.Energy),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(5f, 0),
                        new AnimationTrackProperty.KeyFrame(0f, lifetime)
                    }
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.AnimatedEnable),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(true, 0),
                        new AnimationTrackProperty.KeyFrame(false, lifetime)
                    }
                }
            }
        };

        var uidPlayer = EnsureComp<AnimationPlayerComponent>(gunUid);

        _animPlayer.Stop(gunUid, uidPlayer, "muzzle-flash-light");
        _animPlayer.Play((gunUid, uidPlayer), animTwo, "muzzle-flash-light");
    }

    // TODO: Move RangedDamageSoundComponent to shared so this can be predicted.
    public override void PlayImpactSound(
        EntityUid otherEntity,
        DamageSpecifier? modifiedDamage,
        SoundSpecifier? weaponSound,
        bool forceWeaponSound,
        Robust.Shared.Player.Filter? filter = null,
        Entity<ProjectileComponent, PhysicsComponent>? projectile = null)
    {
    }

    public override void ShootProjectile(EntityUid uid,
        Vector2 direction,
        Vector2 gunVelocity,
        EntityUid? gunUid, // WOLFGATE(Performance): wizden#36641, was EntityUid
        EntityUid? user = null,
        float speed = 20f,
        float offset = 0f) // Mono
    {
        EnsureComp<PredictedProjectileClientComponent>(uid);
        _physics.UpdateIsPredicted(uid);
        base.ShootProjectile(uid, direction, gunVelocity, gunUid, user, speed, offset);
    }
}
