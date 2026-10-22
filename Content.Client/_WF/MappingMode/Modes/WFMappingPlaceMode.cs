using Content.Client._WF.MappingMode.Palette;
using Content.Shared._WF.MappingTools;
using Content.Shared.Maps;
using Robust.Client.Player;
using Robust.Shared.Input.Binding;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// Place: the engine's placement as usual on the left button. Right click erases what is under the cursor for the
/// entry's kind, the middle button eyedrops and Ctrl+wheel turns the placement.
/// </summary>
public sealed partial class WFMappingPlaceMode(WFMappingBuildModeSystem modes) : WFMappingModeHandlerBase(modes)
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;

    public override WFMappingBuildMode Mode => WFMappingBuildMode.Place;

    public override void Enter()
    {
        if (Modes.Entry is { } entry)
            Modes.StartPlacement(entry);
    }

    public override void Exit()
    {
        Modes.StopPlacement();
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        // A decal's right click stays with the decal placement, which erases decals.
        if (Modes.Entry is not { } entry || entry.Kind == WFMappingPaletteKind.Decal)
            return false;

        if (!IsDown(args))
            return true;

        if (entry.Kind == WFMappingPaletteKind.Entity)
        {
            var target = args.EntityUid;
            if (target.IsValid() && _entities.EntityExists(target) && target != _player.LocalEntity)
            {
                _net.ClientSendMessage(new MsgPlacement
                {
                    PlaceType = PlacementManagerMessage.RequestEntRemove,
                    EntityUid = _entities.GetNetEntity(target),
                });
            }

            return true;
        }

        if (Modes.TryGetGridCell(Modes.ToMap(args.Coordinates), out var grid, out var cell) &&
            Modes.TileTypeAt(grid, cell) != 0)
        {
            Modes.SendDraw(new MappingToolsDrawEvent(_entities.GetNetEntity(grid), MappingDrawOp.SetTiles,
                new List<Vector2i> { cell }, ContentTileDefinition.SpaceID, 0, null, Angle.Zero));
        }

        return true;
    }

    public override bool Middle(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (IsDown(args))
            Modes.Pick(args);

        return true;
    }

    public override bool Wheel(float delta)
    {
        return Modes.CtrlDown && Modes.RotatePlacement(delta > 0 ? 1 : -1);
    }

    public override bool Cancel()
    {
        if (Modes.Entry == null)
            return false;

        Modes.ClearEntry();
        return true;
    }
}
