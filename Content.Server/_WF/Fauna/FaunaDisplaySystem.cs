using Content.Server.Anomaly.Components;
using Content.Server.NPC.HTN;
using Content.Server.Popups;
using Content.Server.Singularity.Components;
using Content.Server.Spawners.Components;
using Content.Shared.Interaction;
using Content.Shared.Radiation.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Spawns the display mobs of a demo map and lets a button turn their AI on or reset them.
/// </summary>
public sealed partial class FaunaDisplaySystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private readonly HashSet<Entity<FaunaDisplaySpawnerComponent>> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaDisplaySpawnerComponent, ComponentStartup>(OnSpawnerStartup);
        SubscribeLocalEvent<FaunaDisplaySpawnerComponent, MapInitEvent>(OnSpawnerMapInit);
        SubscribeLocalEvent<FaunaDisplaySpawnerComponent, ComponentShutdown>(OnSpawnerShutdown);
        SubscribeLocalEvent<FaunaDisplayButtonComponent, ActivateInWorldEvent>(OnButtonActivate);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        _pending.RemoveWhere(ent =>
        {
            if (TerminatingOrDeleted(ent) || ent.Comp.Spawned != null)
                return true;

            if (LifeStage(ent) < EntityLifeStage.MapInitialized)
                return false;

            Respawn(ent, false);
            return true;
        });
    }

    // A map saved after map init loads without a MapInitEvent, so its spawners fire from Update.
    private void OnSpawnerStartup(Entity<FaunaDisplaySpawnerComponent> ent, ref ComponentStartup args)
    {
        _pending.Add(ent);
    }

    private void OnSpawnerMapInit(Entity<FaunaDisplaySpawnerComponent> ent, ref MapInitEvent args)
    {
        Respawn(ent, false);
    }

    private void OnSpawnerShutdown(Entity<FaunaDisplaySpawnerComponent> ent, ref ComponentShutdown args)
    {
        QueueDel(ent.Comp.Spawned);
    }

    private void OnButtonActivate(Entity<FaunaDisplayButtonComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        if (_timing.CurTime < ent.Comp.NextPress)
            return;

        args.Handled = true;
        ent.Comp.NextPress = _timing.CurTime + ent.Comp.Delay;
        ent.Comp.Live = !ent.Comp.Live;

        var map = _transform.GetMap(ent.Owner);
        var query = EntityQueryEnumerator<FaunaDisplaySpawnerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawner, out var xform))
        {
            if (xform.MapUid == map)
                Respawn((uid, spawner), ent.Comp.Live);
        }

        var message = ent.Comp.Live ? "fauna-display-button-live" : "fauna-display-button-still";
        _popup.PopupEntity(Loc.GetString(message), ent, args.User);
    }

    /// <summary>
    /// Replaces the spawner's mob with a fresh one, still unless <paramref name="live"/>.
    /// </summary>
    public void Respawn(Entity<FaunaDisplaySpawnerComponent> ent, bool live)
    {
        QueueDel(ent.Comp.Spawned);
        ent.Comp.Spawned = null;

        ent.Comp.Spawned = SpawnDisplayMob(ent.Comp.Prototype, Transform(ent).Coordinates, live);
    }

    /// <summary>
    /// Spawns a display mob, stripped of AI, auras and minions unless <paramref name="live"/>.
    /// </summary>
    public EntityUid? SpawnDisplayMob(EntProtoId prototype, EntityCoordinates coords, bool live)
    {
        if (!_proto.TryIndex(prototype, out var proto) || proto.Abstract)
        {
            Log.Error($"No spawnable display prototype {prototype}");
            return null;
        }

        var mob = Spawn(prototype, coords);
        if (!live)
        {
            RemComp<HTNComponent>(mob);
            RemComp<FaunaAirTemperatureComponent>(mob);
            RemComp<RadiationSourceComponent>(mob);
            RemComp<GasProducerAnomalyComponent>(mob);
            RemComp<GravityWellComponent>(mob);
            RemComp<FaunaBurrowComponent>(mob);
            RemComp<TimedSpawnerComponent>(mob);
            RemComp<FaunaVineComponent>(mob);
        }

        return mob;
    }
}
