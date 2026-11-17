using System.Numerics;
using Content.Shared._WG.Lattice;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private static readonly Color WgBeamColor = Color.FromHex("#c060ff");
    private static readonly Color WgBrokenBeamColor = Color.FromHex("#d03040");
    private static readonly Color WgSealedTint = Color.FromHex("#a040e0").WithAlpha(0.07f);
    private static readonly Color WgBrokenTint = Color.FromHex("#d03040").WithAlpha(0.09f);
    private static readonly Color WgWardColor = Color.FromHex("#b080e0").WithAlpha(0.45f);

    /// <summary>Length in pixels of each dash and gap on a broken beam.</summary>
    private const float WgDash = 10f;

    private readonly Vector2[] _wgSlice = new Vector2[4];

    /// <summary>A slice from a beam in to Central's circle.</summary>
    private void DrawWgSlice(DrawingHandleScreen handle, Matrix3x2 worldToView, WGLatticeFieldComponent field, Vector2 a, Vector2 b, Color color)
    {
        var c = field.CentralCentre;
        var r = field.CentralRadius;
        var innerA = c + Vector2.Normalize(a - c) * r;
        var innerB = c + Vector2.Normalize(b - c) * r;
        _wgSlice[0] = Vector2.Transform(innerA, worldToView);
        _wgSlice[1] = Vector2.Transform(a, worldToView);
        _wgSlice[2] = Vector2.Transform(b, worldToView);
        _wgSlice[3] = Vector2.Transform(innerB, worldToView);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _wgSlice, color);
    }

    /// <summary>
    /// Shows where the Lattice still shelters: the hex field's beams (dashed red where broken, so a gap shows), a faint tint
    /// inside it while it is sealed, and a ring round each anchor that keeps corruption off.
    /// </summary>
    private void DrawWgLatticeHex(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (map is not { } mapUid || !EntManager.TryGetComponent<WGLatticeFieldComponent>(mapUid, out var field))
            return;

        var scale = (Vector2.Transform(Vector2.UnitX, worldToView) - Vector2.Transform(Vector2.Zero, worldToView)).Length();
        for (var i = 0; i < field.Wards.Count && i < field.WardRadii.Count; i++)
        {
            handle.DrawCircle(Vector2.Transform(field.Wards[i], worldToView), field.WardRadii[i] * scale, WgWardColor, false);
        }

        var corners = field.Hex;
        if (field.Unstrung || corners.Count < 3 || field.HexUp.Count != corners.Count)
            return;

        // Each beam's slice: tinted while its beam stands, cut out in red from the beam in to Central's circle once it falls.
        var sealedRing = WGLatticeHex.Sealed(corners, field.HexUp);
        var centre = WGLatticeHex.Centre(corners);
        for (var i = 0; i < corners.Count; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Count];
            if (sealedRing || field.HexUp[i])
            {
                // While anything is broken, Central's circle is open too, so a standing slice stops at it.
                if (sealedRing || field.CentralRadius <= 0f)
                {
                    _wgSlice[0] = Vector2.Transform(centre, worldToView);
                    _wgSlice[1] = Vector2.Transform(a, worldToView);
                    _wgSlice[2] = Vector2.Transform(b, worldToView);
                    handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _wgSlice.AsSpan(0, 3), WgSealedTint);
                    continue;
                }

                DrawWgSlice(handle, worldToView, field, a, b, WgSealedTint);
                continue;
            }

            DrawWgSlice(handle, worldToView, field, a, b, WgBrokenTint);
        }

        for (var i = 0; i < corners.Count; i++)
        {
            var a = Vector2.Transform(corners[i], worldToView);
            var b = Vector2.Transform(corners[(i + 1) % corners.Count], worldToView);
            if (field.HexUp[i])
            {
                handle.DrawLine(a, b, WgBeamColor);
                continue;
            }

            var length = (b - a).Length();
            if (length < 1f)
                continue;

            var step = (b - a) / length;
            var dash = WgDash * UIScale;
            for (var along = 0f; along < length; along += dash * 2f)
            {
                handle.DrawLine(a + step * along, a + step * MathF.Min(along + dash, length), WgBrokenBeamColor);
            }
        }
    }
}
