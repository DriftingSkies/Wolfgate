using System.Numerics;
using Content.Shared._WG.Vehicles.Scanner;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private static readonly Color WgVehicleScannerColor = Color.FromHex("#3fe05a");

    /// <summary>On a vehicle's scanner: a ring round the vehicle, and its position along the bottom.</summary>
    private void DrawWgVehicleScanner(DrawingHandleScreen handle, Matrix3x2 worldToView)
    {
        if (!EntManager.HasComponent<WGVehicleScannerComponent>(_consoleEntity)
            || _wgRadarOrigin is not { } origin
            || !EntManager.EntityExists(origin.EntityId))
            return;

        var position = _transform.GetWorldPosition(origin.EntityId);
        var point = Vector2.Transform(position, worldToView);
        handle.DrawCircle(point, 6f * UIScale, WgVehicleScannerColor, false);

        var text = Loc.GetString("wg-vehicle-scanner-position", ("x", position.X.ToString("0")), ("y", position.Y.ToString("0")));
        handle.DrawString(Font, new Vector2(10f * UIScale, PixelSize.Y - 24f * UIScale), text, UIScale, WgVehicleScannerColor);
    }
}
