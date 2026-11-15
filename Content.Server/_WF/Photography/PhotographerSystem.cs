using Content.Shared._WF.Photography;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Photography;

/// <summary>Gives photographers a camera. <see cref="PhotoAlbumSystem"/> gives their personal album.</summary>
public sealed partial class PhotographerSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedStorageSystem _storage = default!;

    private static readonly EntProtoId Camera = "WFCameraPersonal";

    /// <summary>Spawned mobs to equip next tick, once traits are in place.</summary>
    private readonly List<EntityUid> _queued = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_queued.Count == 0)
            return;

        foreach (var mob in _queued)
        {
            if (!TerminatingOrDeleted(mob) && HasComp<PhotographerComponent>(mob))
                Equip(mob);
        }

        _queued.Clear();
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        _queued.Add(args.Mob);
    }

    private void Equip(EntityUid mob)
    {
        // Spawned next to the mob, not at its coordinates: a crew member who spawns in a cryo pod is inside it.
        var camera = SpawnNextToOrDrop(Camera, mob);
        if (_inventory.TryGetSlotEntity(mob, "back", out var back)
            && _storage.Insert(back.Value, camera, out _, playSound: false))
            return;

        _hands.PickupOrDrop(mob, camera, checkActionBlocker: false);
    }
}
