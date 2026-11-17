using Robust.Shared.Serialization;

namespace Content.Shared._WG.SectorControl;

/// <summary>One hex of the sector grid, in axial coordinates around the sector origin.</summary>
[Serializable, NetSerializable]
public readonly record struct WGSectorCell(int Q, int R)
{
    /// <summary>The cell at the sector origin.</summary>
    public static readonly WGSectorCell Origin = new(0, 0);

    /// <summary>The cell one step away in a direction, 0 to 5; see <see cref="WGSectorHex.Directions"/>.</summary>
    public WGSectorCell Neighbour(int direction)
    {
        var step = WGSectorHex.Directions[((direction % 6) + 6) % 6];
        return new WGSectorCell(Q + step.Q, R + step.R);
    }

    public override string ToString()
    {
        return $"({Q}, {R})";
    }
}
