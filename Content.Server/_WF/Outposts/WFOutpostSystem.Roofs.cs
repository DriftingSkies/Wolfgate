using Content.Server._WF.Outposts.Components;
using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Roofs on outposts. An outpost is open to the sky until roofed: tiles go by their roof bits and roofing entities such as
/// walls, the way the ground does, and roof panels set those bits.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private SharedRoofSystem _roof = default!;

    private void InitializeRoofs()
    {
        SubscribeLocalEvent<WFOutpostRoofPanelComponent, AfterInteractEvent>(OnRoofPanelAfterInteract);
    }

    private void OnRoofPanelAfterInteract(Entity<WFOutpostRoofPanelComponent> panel, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        args.Handled = true;
        if (!TryRoof(args.User, args.ClickLocation, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
            return;
        }

        UseOne(panel);
    }

    /// <summary>
    /// Roofs over the outpost tile at <paramref name="target"/> for its owner. <paramref name="reason"/> is a loc id
    /// when it fails.
    /// </summary>
    public bool TryRoof(EntityUid user, Robust.Shared.Map.EntityCoordinates target, out string reason)
    {
        if (_transform.GetGrid(target) is not { } grid
            || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || !TryComp<MapGridComponent>(grid, out var gridComp)
            || !TryComp<RoofComponent>(grid, out var roof))
        {
            reason = "wf-outpost-roof-not-outpost";
            return false;
        }

        if (!CanBuild(user, (grid, outpost)))
        {
            reason = "wf-outpost-roof-not-owner";
            return false;
        }

        var index = _maps.TileIndicesFor(grid, gridComp, target);
        if (!_maps.TryGetTileRef(grid, gridComp, index, out var tile) || tile.Tile.IsEmpty)
        {
            reason = "wf-outpost-roof-no-floor";
            return false;
        }

        if (_roof.IsRooved((grid, gridComp, roof), index))
        {
            reason = "wf-outpost-roof-already";
            return false;
        }

        _roof.SetRoof((grid, gridComp, roof), index, true);
        reason = string.Empty;
        return true;
    }
}
