using Content.Shared._WG.MappingTools;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Database;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.MappingTools;

public sealed partial class MappingToolsSystem
{
    [Dependency] private AnchorableSystem _anchorable = default!;

    private void InitializeWrench()
    {
        SubscribeNetworkEvent<MappingToolsWrenchEvent>(OnWrench);
    }

    private void OnWrench(MappingToolsWrenchEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            Wrench(args.SenderSession, ev.Target);
    }

    /// <summary>
    /// Anchors or unanchors an entity a wrench could, as one undoable edit. Anchoring keeps the game's tile checks and
    /// snaps it to the tile and a right angle. No user is named, so the game's tool attempts don't apply.
    /// </summary>
    public void Wrench(ICommonSession session, NetEntity target)
    {
        if (!TryGetOnMap<AnchorableComponent>(target, out var uid, out var map, out var anchorable) ||
            !_protection.CanTouch(uid))
        {
            RefuseLink(session, "wg-mapping-wrench-bad");
            return;
        }

        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid ||
            xform.ParentUid != gridUid ||
            !TryComp(gridUid, out MapGridComponent? grid))
        {
            RefuseLink(session, "wg-mapping-wrench-no-grid");
            return;
        }

        var anchoring = !xform.Anchored;
        var needed = anchoring ? AnchorableFlags.Anchorable : AnchorableFlags.Unanchorable;
        if ((anchorable.Flags & needed) == 0)
        {
            RefuseLink(session, "wg-mapping-wrench-fixed");
            return;
        }

        var old = PoseOf(uid);
        var pose = old with { Anchored = anchoring };
        if (anchoring)
        {
            var cell = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            if (!_map.TryGetTileRef(gridUid, grid, cell, out var tile) || tile.Tile.IsEmpty)
            {
                RefuseLink(session, "wg-mapping-wrench-no-grid");
                return;
            }

            if (TryComp(uid, out PhysicsComponent? body) &&
                !_anchorable.TileFree((gridUid, grid), cell, body.CollisionLayer, body.CollisionMask))
            {
                RefuseLink(session, "anchorable-occupied");
                return;
            }

            if (_anchorable.AnyUnstackable(uid, xform.Coordinates))
            {
                RefuseLink(session, "construction-step-condition-no-unstackable-in-tile");
                return;
            }

            var quarter = Math.PI / 2;
            pose = pose with
            {
                Position = _map.GridTileToLocal(gridUid, grid, cell).Position,
                Rotation = new Angle(Math.Round(old.Rotation.Theta / quarter) * quarter),
            };
        }

        if (!CanEditMaps(session, [map], 1))
            return;

        var edit = new MappingEdit("wg-mapping-tools-edit-wrench");
        edit.Moves.Add(new MappingEntityMove(GetRef(uid), old, pose));
        Commit(session, edit);

        _popup.PopupCursor(Loc.GetString(anchoring ? "wg-mapping-wrench-anchored" : "wg-mapping-wrench-unanchored",
            ("name", Name(uid))), session);
        _adminLog.Add(anchoring ? LogType.Anchor : LogType.Unanchor, LogImpact.Low,
            $"{session:player} used mapping tools to {(anchoring ? "anchor" : "unanchor")} {ToPrettyString(uid):entity}");
    }
}
