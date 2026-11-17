using System.Numerics;
using Content.Client._WG.DroneRings;
using Content.Client._WG.Lattice;
using Content.Shared._WG.DroneRings;
using Content.Shared._WG.Lattice;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private readonly List<float> _wgLatticeRadii = new();
    private readonly List<string> _wgLatticeLabels = new();
    private readonly List<Color> _wgLatticeColors = new();
    private string? _wgLatticeBuilt;

    /// <summary>Rings at the Lattice's band edges, or where drones start to spawn on a map without one.</summary>
    private void DrawDroneRings(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        var centre = Vector2.Transform(Vector2.Zero, worldToView);
        if (EntManager.TryGetComponent<WGLatticeFieldComponent>(map, out var field)
            && (WGLatticeRings.IsCurrent(field, _wgLatticeBuilt, _wgLatticeRadii)
                || WGLatticeRings.TryBuild(IoCManager.Resolve<IPrototypeManager>(), field, _wgLatticeRadii, _wgLatticeLabels, _wgLatticeColors)))
        {
            _wgLatticeBuilt = field.Profile.Id;
            WGDroneRingDrawing.Draw(handle, Font, _wgLatticeRadii, centre, MinimapScale, PixelSize, UIScale, _wgLatticeLabels, _wgLatticeColors);
            return;
        }

        if (!EntManager.TryGetComponent<WGDroneRingsComponent>(map, out var rings))
            return;

        WGDroneRingDrawing.Draw(handle, Font, rings.Radii, centre, MinimapScale, PixelSize, UIScale);
    }
}
