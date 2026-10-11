using System.Numerics;
using Content.Client._WF.DroneRings;
using Content.Shared._WF.DroneRings;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    /// <summary>Rings at the distances from the viewed map's centre where drones start to spawn.</summary>
    private void DrawDroneRings(DrawingHandleScreen handle, Matrix3x2 matty, EntityUid map)
    {
        if (!EntManager.TryGetComponent<WFDroneRingsComponent>(map, out var rings))
            return;

        var centre = Vector2.Transform(Vector2.Zero, matty);
        centre = ScalePosition(centre with { Y = -centre.Y });
        WFDroneRingDrawing.Draw(handle, Font, rings.Radii, centre, MinimapScale, PixelSize, UIScale);
    }
}
