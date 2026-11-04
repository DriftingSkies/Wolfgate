using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Detection;
using Content.Shared._Mono.Ships;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Detection;

/// <summary>
///     Handles the logic for thermal signatures.
/// </summary>
public sealed partial class ThermalSignatureSystem : EntitySystem
{
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float UpdateIntervalSeconds = 1f;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(UpdateIntervalSeconds);
    private TimeSpan _nextUpdateTime = TimeSpan.FromSeconds(0.8); // WOLFGATE(Performance): off the tick other one-second systems run on

    private const float HeatChangeThreshold = 1.02f;

    // WOLFGATE(Performance) START: each pass refreshes one quarter of the signatures, by uid
    private const int SliceCount = 4;
    private int _slice;
    // WOLFGATE END

    private List<Entity<ThermalSignatureComponent>> _gridQueue = new();

    private EntityQuery<ThermalSignatureComponent> _sigQuery;
    private EntityQuery<GunComponent> _gunQuery;
    private EntityQuery<MapGridComponent> _mapGridQuery;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridInitializeEvent>(OnGridInitialized);

        // some of this could also be handled in shared but there's no point since PVS is a thing
        SubscribeLocalEvent<MachineThermalSignatureComponent, GetThermalSignatureEvent>(OnMachineGetSignature);
        SubscribeLocalEvent<PassiveThermalSignatureComponent, GetThermalSignatureEvent>(OnPassiveGetSignature);

        SubscribeLocalEvent<ThermalSignatureComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<PowerSupplierComponent, GetThermalSignatureEvent>(OnPowerGetSignature);
        SubscribeLocalEvent<ThrusterComponent, GetThermalSignatureEvent>(OnThrusterGetSignature);
        SubscribeLocalEvent<FTLDriveComponent, GetThermalSignatureEvent>(OnFTLGetSignature);

        _sigQuery = GetEntityQuery<ThermalSignatureComponent>();
        _gunQuery = GetEntityQuery<GunComponent>();
        _mapGridQuery = GetEntityQuery<MapGridComponent>();
    }

    private void OnGridInitialized(GridInitializeEvent args)
    {
        EnsureComp<ThermalSignatureComponent>(args.EntityUid);
    }

    private void OnGunShot(Entity<ThermalSignatureComponent> ent, ref GunShotEvent args)
    {
        if (_gunQuery.TryComp(ent, out var gun))
            ent.Comp.StoredHeat += gun.ShootThermalSignature;
    }

    private void OnMachineGetSignature(Entity<MachineThermalSignatureComponent> ent, ref GetThermalSignatureEvent args)
    {
        if (_power.IsPowered(ent.Owner))
            args.Signature += ent.Comp.Signature;
    }

    private void OnPassiveGetSignature(Entity<PassiveThermalSignatureComponent> ent, ref GetThermalSignatureEvent args)
    {
        args.Signature += ent.Comp.Signature;
    }

    private void OnPowerGetSignature(Entity<PowerSupplierComponent> ent, ref GetThermalSignatureEvent args)
    {
        args.Signature += ent.Comp.CurrentSupply * ent.Comp.HeatSignatureRatio;
    }

    private void OnThrusterGetSignature(Entity<ThrusterComponent> ent, ref GetThermalSignatureEvent args)
    {
        if (ent.Comp.Firing)
            args.Signature += ent.Comp.Thrust * ent.Comp.HeatSignatureRatio;
    }

    private void OnFTLGetSignature(Entity<FTLDriveComponent> ent, ref GetThermalSignatureEvent args)
    {
        var xform = Transform(ent);
        if (!TryComp<FTLComponent>(xform.GridUid, out var ftl))
            return;

        if (ftl.State == FTLState.Starting || ftl.State == FTLState.Cooldown)
            args.Signature += ent.Comp.ThermalSignature;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdateTime)
            return;

        _nextUpdateTime = _timing.CurTime + UpdateInterval;

        var gridQuery = EntityQueryEnumerator<MapGridComponent, ThermalSignatureComponent>();
        while (gridQuery.MoveNext(out _, out _, out var gridSigComp))
        {
            gridSigComp.TotalHeat = 0f;
        }

        _gridQueue.Clear();
        // WOLFGATE(Performance) START: only this pass's slice is refreshed, over the time since it was last
        _slice = (_slice + 1) % SliceCount;
        var sliceSeconds = UpdateIntervalSeconds * SliceCount;
        // WOLFGATE END
        var query = EntityQueryEnumerator<ThermalSignatureComponent>();
        while (query.MoveNext(out var uid, out var sigComp))
        {
            // WOLFGATE(Performance) START: the other slices keep their stored heat and only add to the totals
            if (uid.Id % SliceCount == _slice)
            {
                var ev = new GetThermalSignatureEvent();
                RaiseLocalEvent(uid, ref ev);

                sigComp.StoredHeat += ev.Signature * sliceSeconds;
                sigComp.StoredHeat *= MathF.Pow(sigComp.HeatDissipation, sliceSeconds);
            }
            // WOLFGATE END

            if (_mapGridQuery.HasComp(uid))
            {
                _gridQueue.Add((uid, sigComp));
                continue;
            }
            else
            {
                var xform = Transform(uid);
                sigComp.TotalHeat = sigComp.StoredHeat;
                if (xform.GridUid != null && _sigQuery.TryGetComponent(xform.GridUid.Value, out var gridSig))
                    gridSig.TotalHeat += sigComp.StoredHeat;
            }
        }

        foreach (var ent in _gridQueue) {
            ent.Comp.TotalHeat += ent.Comp.StoredHeat;

            // don't sync it if it didn't change heat much since last time, we don't need to sync 500 cold asteroids every system update
            if (ent.Comp.TotalHeat <= ent.Comp.LastUpdateHeat * HeatChangeThreshold
                && ent.Comp.TotalHeat >= ent.Comp.LastUpdateHeat / HeatChangeThreshold)
                continue;

            ent.Comp.LastUpdateHeat = ent.Comp.TotalHeat;
            Dirty(ent);
        }
    }
}
