using Content.Shared._WF.Photography;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Photography;

/// <summary>Gives photographers a camera. <see cref="PhotoAlbumSystem"/> gives their personal album.</summary>
public sealed partial class PhotographerSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;

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
        _hands.PickupOrDrop(mob, Spawn(Camera, Transform(mob).Coordinates), checkActionBlocker: false);
    }
}
