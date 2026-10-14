using Content.Shared._WF.Vehicles.Core;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.GameObjects;

namespace Content.Client._WF.Vehicles.Visuals;

/// <summary>Hides the roof pieces of the interior the local player is standing in.</summary>
public sealed partial class VehicleInteriorRoofSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void FrameUpdate(float frameTime)
    {
        EntityUid? playerMap = _player.LocalEntity is { } player ? Transform(player).MapUid : null;

        var query = AllEntityQuery<VehicleInteriorRoofComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            var visible = playerMap == null || xform.MapUid != playerMap;
            if (sprite.Visible != visible)
                _sprite.SetVisible((uid, sprite), visible);
        }
    }
}
