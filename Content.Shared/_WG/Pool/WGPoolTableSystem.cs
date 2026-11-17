using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Map.Components;

namespace Content.Shared._WG.Pool;

/// <summary>Finds the pieces of a pool table, and closes its window on anyone who isn't standing at one of them.</summary>
public sealed partial class WGPoolTableSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const int MaxPieces = 12;

    private static readonly Vector2i[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    private readonly List<(EntityUid Uid, Vector2i Tile)> _pieces = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGPoolTableComponent, BoundUserInterfaceCheckRangeEvent>(OnRangeCheck);
    }

    /// <summary>Whether the player has a pool cue in a hand.</summary>
    public bool HoldsCue(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<WGPoolCueComponent>(held))
                return true;
        }

        return false;
    }

    /// <summary>The piece that holds the game: the lowest, then leftmost, of the table's pieces.</summary>
    public EntityUid GetRoot(Entity<WGPoolTableComponent> ent)
    {
        CollectPieces(ent);
        var root = _pieces[0];
        foreach (var piece in _pieces)
        {
            if (piece.Tile.Y < root.Tile.Y || piece.Tile.Y == root.Tile.Y && piece.Tile.X < root.Tile.X)
                root = piece;
        }

        return root.Uid;
    }

    /// <summary>Fills the piece list with this piece and every table piece joined to it.</summary>
    private void CollectPieces(Entity<WGPoolTableComponent> ent)
    {
        _pieces.Clear();
        var xform = Transform(ent);
        if (!ent.Comp.MultiTile || !xform.Anchored || !TryComp(xform.GridUid, out MapGridComponent? grid))
        {
            _pieces.Add((ent, Vector2i.Zero));
            return;
        }

        var gridUid = xform.GridUid.Value;
        _pieces.Add((ent, _map.TileIndicesFor(gridUid, grid, xform.Coordinates)));
        for (var i = 0; i < _pieces.Count && _pieces.Count < MaxPieces; i++)
        {
            foreach (var offset in Neighbours)
            {
                var tile = _pieces[i].Tile + offset;
                foreach (var other in _map.GetAnchoredEntities(gridUid, grid, tile))
                {
                    if (!TryComp(other, out WGPoolTableComponent? table) || !table.MultiTile || Known(other))
                        continue;

                    _pieces.Add((other, tile));
                }
            }
        }
    }

    private bool Known(EntityUid uid)
    {
        foreach (var piece in _pieces)
        {
            if (piece.Uid == uid)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The interface's own range is measured from the root piece and set wide enough to span the table; this
    /// narrows it to players standing at one of the pieces.
    /// </summary>
    private void OnRangeCheck(Entity<WGPoolTableComponent> ent, ref BoundUserInterfaceCheckRangeEvent args)
    {
        if (args.Result == BoundUserInterfaceRangeResult.Fail)
            return;

        var actor = _transform.GetMapCoordinates(args.Actor);
        CollectPieces(ent);
        foreach (var piece in _pieces)
        {
            if (_transform.GetMapCoordinates(piece.Uid).InRange(actor, ent.Comp.Range))
                return;
        }

        args.Result = BoundUserInterfaceRangeResult.Fail;
    }
}
