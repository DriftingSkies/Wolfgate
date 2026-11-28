using System.Numerics;

namespace Content.Shared._WG.Settlers;

/// <summary>
/// The fixed shape of the board: nineteen pointy-top hexes in a hexagon of radius two, their 54 corners and
/// 72 sides, and the nine coast sides that hold docks. Positions are in hex radii with the board's centre at the
/// origin and y pointing down. Built the same way on server and client, so indices agree.
/// </summary>
public static class SettlersBoard
{
    public const int TileCount = 19;
    public const int VertexCount = 54;
    public const int EdgeCount = 72;
    public const int DockCount = 9;

    /// <summary>Which of the 30 coast sides, counted round the board, hold the docks.</summary>
    private static readonly int[] DockSlots = { 0, 3, 7, 10, 13, 17, 20, 23, 27 };

    public static readonly Vector2[] TileCenter = new Vector2[TileCount];

    /// <summary>Each tile's six corners, clockwise from the top.</summary>
    public static readonly int[][] TileVertices = new int[TileCount][];
    public static readonly int[][] TileNeighbours = new int[TileCount][];

    public static readonly Vector2[] VertexPos;
    public static readonly int[][] VertexTiles;
    public static readonly int[][] VertexEdges;
    public static readonly int[][] VertexNeighbours;

    public static readonly int[] EdgeA;
    public static readonly int[] EdgeB;

    /// <summary>The coast side each dock sits on.</summary>
    public static readonly int[] DockEdges = new int[DockCount];

    static SettlersBoard()
    {
        var sqrt3 = MathF.Sqrt(3f);
        var tile = 0;
        for (var r = -2; r <= 2; r++)
        {
            for (var q = Math.Max(-2, -r - 2); q <= Math.Min(2, -r + 2); q++)
            {
                TileCenter[tile++] = new Vector2(sqrt3 * (q + r * 0.5f), 1.5f * r);
            }
        }

        var vertexIds = new Dictionary<(int, int), int>();
        var positions = new List<Vector2>();
        for (var t = 0; t < TileCount; t++)
        {
            TileVertices[t] = new int[6];
            for (var i = 0; i < 6; i++)
            {
                var angle = MathF.PI / 3f * i - MathF.PI / 2f;
                var pos = TileCenter[t] + new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var key = ((int) MathF.Round(pos.X * 100f), (int) MathF.Round(pos.Y * 100f));
                if (!vertexIds.TryGetValue(key, out var id))
                {
                    id = positions.Count;
                    vertexIds[key] = id;
                    positions.Add(pos);
                }

                TileVertices[t][i] = id;
            }
        }

        VertexPos = positions.ToArray();

        var edgeIds = new Dictionary<(int, int), int>();
        var edgeA = new List<int>();
        var edgeB = new List<int>();
        var edgeTiles = new List<int>();
        for (var t = 0; t < TileCount; t++)
        {
            for (var i = 0; i < 6; i++)
            {
                var a = TileVertices[t][i];
                var b = TileVertices[t][(i + 1) % 6];
                var key = (Math.Min(a, b), Math.Max(a, b));
                if (edgeIds.TryGetValue(key, out var id))
                {
                    edgeTiles[id]++;
                    continue;
                }

                edgeIds[key] = edgeA.Count;
                edgeA.Add(key.Item1);
                edgeB.Add(key.Item2);
                edgeTiles.Add(1);
            }
        }

        EdgeA = edgeA.ToArray();
        EdgeB = edgeB.ToArray();

        var vertexTiles = NewLists(VertexPos.Length);
        for (var t = 0; t < TileCount; t++)
        {
            foreach (var v in TileVertices[t])
            {
                vertexTiles[v].Add(t);
            }
        }

        var vertexEdges = NewLists(VertexPos.Length);
        var vertexNeighbours = NewLists(VertexPos.Length);
        for (var e = 0; e < EdgeA.Length; e++)
        {
            vertexEdges[EdgeA[e]].Add(e);
            vertexEdges[EdgeB[e]].Add(e);
            vertexNeighbours[EdgeA[e]].Add(EdgeB[e]);
            vertexNeighbours[EdgeB[e]].Add(EdgeA[e]);
        }

        VertexTiles = ToArrays(vertexTiles);
        VertexEdges = ToArrays(vertexEdges);
        VertexNeighbours = ToArrays(vertexNeighbours);

        for (var t = 0; t < TileCount; t++)
        {
            var neighbours = new List<int>();
            for (var o = 0; o < TileCount; o++)
            {
                if (o != t && Vector2.Distance(TileCenter[t], TileCenter[o]) < sqrt3 + 0.01f)
                    neighbours.Add(o);
            }

            TileNeighbours[t] = neighbours.ToArray();
        }

        var coast = new List<int>();
        for (var e = 0; e < EdgeA.Length; e++)
        {
            if (edgeTiles[e] == 1)
                coast.Add(e);
        }

        coast.Sort((x, y) => Angle(EdgeMid(x)).CompareTo(Angle(EdgeMid(y))));
        for (var i = 0; i < DockCount; i++)
        {
            DockEdges[i] = coast[DockSlots[i]];
        }
    }

    public static Vector2 EdgeMid(int edge)
    {
        return (VertexPos[EdgeA[edge]] + VertexPos[EdgeB[edge]]) * 0.5f;
    }

    /// <summary>The far end of an edge from one of its corners.</summary>
    public static int OtherEnd(int edge, int vertex)
    {
        return EdgeA[edge] == vertex ? EdgeB[edge] : EdgeA[edge];
    }

    public static bool Touches(int edge, int vertex)
    {
        return EdgeA[edge] == vertex || EdgeB[edge] == vertex;
    }

    private static float Angle(Vector2 at)
    {
        return MathF.Atan2(at.Y, at.X);
    }

    private static List<int>[] NewLists(int count)
    {
        var lists = new List<int>[count];
        for (var i = 0; i < count; i++)
        {
            lists[i] = new List<int>();
        }

        return lists;
    }

    private static int[][] ToArrays(List<int>[] lists)
    {
        var arrays = new int[lists.Length][];
        for (var i = 0; i < lists.Length; i++)
        {
            arrays[i] = lists[i].ToArray();
        }

        return arrays;
    }
}
