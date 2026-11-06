using System.Numerics;
using Content.Client._WF.Lattice;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private static readonly Color WfPhantomColor = Color.FromHex("#c8c8c8");

    /// <summary>A contact's radar label, garbled if it or the viewer is out past the Lattice's hold.</summary>
    private string? WfLatticeGarble(string? label, EntityUid grid)
    {
        if (label == null)
            return null;

        var map = EntManager.GetComponent<TransformComponent>(grid).MapUid;
        return EntManager.System<WFLatticeViewSystem>().Garble(label, map, _transform.GetWorldPosition(grid), grid.Id);
    }

    /// <summary>Radar contacts that only this player sees.</summary>
    private void DrawWfLatticePhantoms(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (map is not { } mapUid)
            return;

        var mapId = EntManager.GetComponent<TransformComponent>(mapUid).MapID;
        var view = EntManager.System<WFLatticeViewSystem>();
        foreach (var (position, _, seed) in EntManager.System<WFLatticeHallucinationSystem>().Blips)
        {
            if (position.MapId != mapId)
                continue;

            var at = Vector2.Transform(position.Position, worldToView);
            if (at.X < 0f || at.Y < 0f || at.X > PixelSize.X || at.Y > PixelSize.Y)
                continue;

            handle.DrawCircle(at, 3f * UIScale, WfPhantomColor);
            var label = view.Garble(Loc.GetString("wf-lattice-phantom-contact"), mapUid, position.Position, seed);
            handle.DrawString(Font, at + new Vector2(6f, -6f) * UIScale, label, UIScale * 0.8f, WfPhantomColor);
        }
    }
}
