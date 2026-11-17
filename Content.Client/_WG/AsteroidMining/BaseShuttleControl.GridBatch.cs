using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// Queues radar grid fills and outlines so consecutive grids go out in one draw call. Every DrawPrimitives call is its
/// own GL draw, so an asteroid field drawn grid by grid costs hundreds of them a frame.
/// </summary>
public partial class BaseShuttleControl
{
    /// <summary>
    /// Most vertices per flush. The buffer grows in doubles from a multiple of 6, so line and triangle lists never split
    /// mid-primitive.
    /// </summary>
    private const int WgGridBatchSize = 6 * 4096;

    private DrawVertexUV2DColor[] _wgGridBatch = new DrawVertexUV2DColor[6 * 128];
    private int _wgGridBatchCount;
    private DrawPrimitiveTopology _wgGridBatchTopology;

    /// <summary>Queues a grid's fill or outline. Anything else drawn before <see cref="WgFlushGrids"/> must flush first.</summary>
    private void WgQueueGrid(DrawingHandleScreen handle, Matrix3x2 gridToView, GridDrawData data, Color color,
        float alpha, bool drawFill)
    {
        if (drawFill)
        {
            var fillColor = Color.ToSrgb(Color.InterpolateBetween(BackingColor, color, alpha)).WithAlpha(1f);
            WgQueue(handle, DrawPrimitiveTopology.TriangleList, data.Vertices, 0, data.EdgeIndex, gridToView, fillColor);
        }
        else
        {
            WgQueue(handle, DrawPrimitiveTopology.LineList, data.Vertices, data.EdgeIndex, data.Vertices.Count,
                gridToView, color);
        }
    }

    protected void WgQueue(DrawingHandleScreen handle, DrawPrimitiveTopology topology, List<Vector2> vertices, int start,
        int end, Matrix3x2 gridToView, Color color)
    {
        if (_wgGridBatchCount > 0 && topology != _wgGridBatchTopology)
            WgFlushGrids(handle);

        _wgGridBatchTopology = topology;
        // As DrawPrimitives with a flat colour does it.
        var linear = Color.FromSrgb(color * handle.Modulate);
        var stride = topology == DrawPrimitiveTopology.TriangleList ? 3 : 2;
        for (var i = start; i + stride <= end; i += stride)
        {
            if (_wgGridBatchCount + stride > _wgGridBatch.Length)
            {
                if (_wgGridBatch.Length < WgGridBatchSize)
                    Array.Resize(ref _wgGridBatch, _wgGridBatch.Length * 2);
                else
                    WgFlushGrids(handle);
            }

            for (var j = 0; j < stride; j++)
            {
                _wgGridBatch[_wgGridBatchCount++] = new DrawVertexUV2DColor(Vector2.Transform(vertices[i + j], gridToView),
                    new Vector2(0.5f, 0.5f), linear);
            }
        }
    }

    /// <summary>Draws every queued grid.</summary>
    protected void WgFlushGrids(DrawingHandleScreen handle)
    {
        if (_wgGridBatchCount == 0)
            return;

        handle.DrawPrimitives(_wgGridBatchTopology, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(_wgGridBatch, 0, _wgGridBatchCount));
        _wgGridBatchCount = 0;
    }
}
