using System.Numerics;
using Robust.Shared.Map;

namespace Content.Shared._WF.Photography;

/// <summary>The square of tiles a photo covers, numbered row by row from the top left.</summary>
public readonly record struct PhotoSquare(EntityUid Grid, Vector2 Centre, Angle Rotation, int Tiles,
    Vector2i CentreTile = default)
{
    /// <summary>Tile offset from the centre tile, with y up.</summary>
    public (int Column, int Row) Offset(int index)
    {
        var half = Tiles / 2;
        return (index % Tiles - half, half - index / Tiles);
    }

    /// <summary>Map position of the middle of a tile in the square.</summary>
    public MapCoordinates TileCentre(int index, MapId map)
    {
        var (x, y) = Offset(index);
        return new MapCoordinates(Centre + Rotation.RotateVec(new Vector2(x, y)), map);
    }

    /// <summary>Grid tile indices of a tile in the square.</summary>
    public Vector2i GridTile(int index)
    {
        var (x, y) = Offset(index);
        return CentreTile + new Vector2i(x, y);
    }

    /// <summary>The square's outline in world space.</summary>
    public Box2Rotated Bounds()
    {
        return new Box2Rotated(Box2.CenteredAround(Centre, new Vector2(Tiles, Tiles)), Rotation, Centre);
    }
}
