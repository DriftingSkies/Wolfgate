using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Moves <see cref="FaunaHitAndRunComponent"/> mobs off to the side after each melee hit.
/// </summary>
public sealed partial class FaunaHitAndRunSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaHitAndRunComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<FaunaHitAndRunComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.User != ent.Owner || args.HitEntities.Count == 0 || !_random.Prob(ent.Comp.Chance))
            return;

        var xform = Transform(ent);
        if (!TryComp<MapGridComponent>(xform.ParentUid, out var grid))
            return;

        var centre = _map.TileIndicesFor(xform.ParentUid, grid, xform.Coordinates);
        if (!_helper.TryPickFreeTile(xform.ParentUid, grid, centre, 1, FaunaTileShape.SquareNoCentre, out var tile))
            return;

        var tileCentre = _map.GridTileToLocal(xform.ParentUid, grid, tile);
        var to = _transform.ToMapCoordinates(tileCentre).Position;
        _throwing.TryThrow(ent, to - _transform.GetWorldPosition(xform), ent.Comp.Speed, animated: false,
            playSound: false, doSpin: false);
    }
}
