using System.Numerics;
using Content.Server._WG.Vehicles.Core;
using Content.Shared._WG.GroundCover;

namespace Content.Server._WG.GroundCover;

public sealed partial class WGGroundCoverSystem
{
    /// <summary>Snow depth units a vehicle's tread presses out of the tile it rolls over.</summary>
    public const int TrackCompression = WGSnowCover.UnitsPerLevel;

    private readonly Dictionary<EntityUid, (Vector2i Left, Vector2i Right)> _treads = new();

    private void InitializeTracks()
    {
        SubscribeLocalEvent<WGVehicleTreadsEvent>(OnTreads);
    }

    /// <summary>Presses a level of snow flat under each tread, once for each tile a tread enters.</summary>
    private void OnTreads(ref WGVehicleTreadsEvent args)
    {
        if (!HasComp<WGSnowCoverComponent>(args.Grid))
            return;

        var left = TileOf(args.Left);
        var right = TileOf(args.Right);
        var seen = _treads.TryGetValue(args.Vehicle, out var last);
        if (!seen || last.Left != left)
            Compress(args.Grid, left);

        if (!seen || last.Right != right)
            Compress(args.Grid, right);

        _treads[args.Vehicle] = (left, right);
        if (_treads.Count <= 64)
            return;

        foreach (var vehicle in new List<EntityUid>(_treads.Keys))
        {
            if (TerminatingOrDeleted(vehicle))
                _treads.Remove(vehicle);
        }
    }

    private void Compress(EntityUid grid, Vector2i tile)
    {
        var depth = GetDepth(grid, tile);
        if (depth > 0)
            SetDepth(grid, tile, depth - TrackCompression);
    }

    private static Vector2i TileOf(Vector2 position)
    {
        return new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
    }
}
