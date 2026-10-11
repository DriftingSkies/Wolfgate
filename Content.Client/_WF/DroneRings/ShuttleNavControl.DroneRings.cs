using System.Numerics;
using Content.Client._WF.DroneRings;
using Content.Shared._WF.DroneRings;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>Rings at the distances from the map's centre where drones start to spawn.</summary>
    private void DrawDroneRings(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (!EntManager.TryGetComponent<WFDroneRingsComponent>(map, out var rings))
            return;

        WFDroneRingDrawing.Draw(handle, Font, rings.Radii, Vector2.Transform(Vector2.Zero, worldToView), MinimapScale, PixelSize, UIScale);
    }
}
