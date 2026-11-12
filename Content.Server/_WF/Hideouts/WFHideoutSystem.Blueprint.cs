using System.Linq;
using System.Numerics;
using Content.Server._WF.Outposts.Components;
using Content.Server.Decals;
using Content.Shared._WF.Hideouts.Blueprint;
using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Content.Shared.Decals;
using Content.Shared.Doors.Components;
using Content.Shared.SubFloor;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Hideouts;

/// <summary>
/// The blueprint: a builder paints floor, walls, doors, furniture, decorations, decals and roof onto a plan of an
/// outpost (the console's Blueprint tab) or of a ship in a bay (the shipwright's Interior), and the plan is built at
/// once for its price. On an outpost, floor follows the rules foundation plates do; on a ship it only re-lays the hull's own.
/// </summary>
public sealed partial class WFHideoutSystem
{
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    /// <summary>The most decals the blueprint lays on one tile.</summary>
    private const int DecalsPerTile = 8;

    private readonly HashSet<EntityUid> _onTile = new();

    /// <summary>Lays floor for a plan and returns the tiles that changed, with the loc id of the first refusal.</summary>
    public delegate List<Vector2i> BlueprintFloor(IReadOnlyList<(Vector2i Index, string Tile)> cells, out string? refused);

    /// <summary>Roofs or unroofs tiles for a plan and returns the tiles that changed.</summary>
    public delegate List<Vector2i> BlueprintRoof(IReadOnlyList<Vector2i> cells, bool roofed);

    /// <summary>What a tile of a brush costs.</summary>
    public int BrushPrice(WFBlueprintEntry brush)
    {
        if (brush.Entity is { } entity && _proto.TryIndex<EntityPrototype>(entity, out var proto))
            return Math.Max(brush.Price, (int) Math.Ceiling(_pricing.GetEstimatedPrice(proto) * 1.5));

        return brush.Price;
    }

    /// <summary>What a tile of the brush with an id costs; 0 for an id that is no brush.</summary>
    public int BrushPrice(string id)
    {
        return WFBlueprintCatalog.TryGet(_proto, _tileDefs, id, out var brush) ? BrushPrice(brush) : 0;
    }

    private void OnCanvasRequest(Entity<WFOutpostConsoleComponent> console, ref WFBlueprintCanvasRequestMessage args)
    {
        if (TryOutpost(console, out var outpost))
            SendCanvas(console, WFOutpostConsoleUiKey.Key, outpost, args.Actor);
    }

    /// <summary>Sends a player at a console what stands on a grid, the brushes' prices and their balance.</summary>
    public void SendCanvas(EntityUid console, Enum key, EntityUid gridUid, EntityUid actor)
    {
        var prices = WFBlueprintCatalog.All(_proto, _tileDefs).ToDictionary(brush => brush.Id, BrushPrice);
        _bank.TryGetBalance(actor, out var balance);
        _ui.ServerSendUiMessage(console, key, new WFBlueprintCanvasMessage(Cells(gridUid), prices, balance), actor);
    }

    /// <summary>What stands on a grid, as a plan draws it: everything anchored, and what the brushes lay that isn't.</summary>
    private List<WFBlueprintCell> Cells(EntityUid gridUid)
    {
        var cells = new List<WFBlueprintCell>();
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return cells;

        var laid = LaidEntities();
        var children = Transform(gridUid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            var xform = Transform(child);
            if (HasComp<SubFloorHideComponent>(child)
                || !xform.Anchored && !(MetaData(child).EntityPrototype?.ID is { } id && laid.Contains(id)))
                continue;

            cells.Add(new WFBlueprintCell(_maps.LocalToTile(gridUid, grid, xform.Coordinates), CellKind(child)));
        }

        return cells;
    }

    /// <summary>The entity prototypes the brushes lay.</summary>
    private HashSet<string> LaidEntities(bool underOnly = false)
    {
        return WFBlueprintCatalog.All(_proto, _tileDefs).Where(brush => brush.Entity != null && (!underOnly || brush.Under))
            .Select(brush => brush.Entity!).ToHashSet();
    }

    private WFBlueprintCellKind CellKind(EntityUid uid)
    {
        if (_tags.HasTag(uid, WallTag))
            return WFBlueprintCellKind.Wall;
        if (_tags.HasTag(uid, WindowTag))
            return WFBlueprintCellKind.Window;
        return HasComp<DoorComponent>(uid) ? WFBlueprintCellKind.Door : WFBlueprintCellKind.Thing;
    }

    private void OnBlueprintApply(Entity<WFOutpostConsoleComponent> console, ref WFBlueprintApplyMessage args)
    {
        if (!TryOutpost(console, out var outpost))
            return;

        var built = TryApplyBlueprint(args.Actor, outpost, args.Strokes, out var spent, out var reason);
        ReportBlueprint(args.Actor, built, spent, reason);
        SendCanvas(console, WFOutpostConsoleUiKey.Key, outpost, args.Actor);
        _outposts.RefreshConsole(outpost);
    }

    /// <summary>Tells a builder what a plan came to: what was built and paid, and the first thing refused.</summary>
    public void ReportBlueprint(EntityUid user, int built, int spent, string? reason)
    {
        var text = reason != null && built == 0
            ? Loc.GetString(reason)
            : Loc.GetString("wf-blueprint-built", ("count", built), ("price", spent))
              + (reason != null ? " " + Loc.GetString("wf-blueprint-some-refused", ("reason", Loc.GetString(reason))) : string.Empty);
        _popup.PopupEntity(text, user, user);
    }

    /// <summary>
    /// Builds a plan on an outpost for one of its builders. Returns the strokes built; <paramref name="reason"/> is the
    /// loc id of the first refusal, if any.
    /// </summary>
    public int TryApplyBlueprint(EntityUid user, Entity<WFOutpostComponent> outpost, IReadOnlyList<WFBlueprintStroke> strokes,
        out int spent, out string? reason)
    {
        spent = 0;
        if (!_cfg.GetCVar(Content.Shared._WF.Hideouts.HideoutCVars.Enabled) || !_outposts.CanBuild(user, outpost)
            || !TryComp<MapGridComponent>(outpost, out var grid))
        {
            reason = "wf-blueprint-not-builder";
            return 0;
        }

        var built = ApplyPlan(user, outpost, grid, strokes,
            (IReadOnlyList<(Vector2i Index, string Tile)> cells, out string? refused) => _outposts.PaintFloor(user, outpost, cells, out refused),
            (cells, roofed) => _outposts.PaintRoof(user, outpost, cells, roofed),
            _outposts.EntityCap(outpost) - _outposts.CountAllEntities(outpost), out spent, out reason);
        if (built > 0)
        {
            _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
                $"{ToPrettyString(user):user} built a blueprint of {built} strokes on outpost {ToPrettyString(outpost):grid} for {spent}");
        }

        return built;
    }

    /// <summary>
    /// Builds a plan on a grid. The whole plan's price is taken first and what a refused stroke cost is paid back.
    /// <paramref name="roofs"/> is null where roofs can't be painted, and <paramref name="room"/> is how many more
    /// objects the grid may take. Returns the strokes built.
    /// </summary>
    public int ApplyPlan(EntityUid user, EntityUid gridUid, MapGridComponent grid, IReadOnlyList<WFBlueprintStroke> strokes,
        BlueprintFloor floors, BlueprintRoof? roofs, int room, out int spent, out string? reason)
    {
        spent = 0;
        reason = null;
        var plan = new List<(WFBlueprintStroke Stroke, WFBlueprintEntry Brush, int Price)>();
        foreach (var stroke in strokes.Take(WFBlueprintLimits.Strokes))
        {
            if (WFBlueprintCatalog.TryGet(_proto, _tileDefs, stroke.Brush, out var brush))
                plan.Add((stroke, brush, BrushPrice(brush)));
        }

        if (plan.Count == 0)
            return 0;

        var total = plan.Sum(entry => entry.Price);
        if (total > 0 && !_bank.TryBankWithdraw(user, total))
        {
            reason = "wf-hideout-cannot-afford";
            return 0;
        }

        var built = 0;
        var owed = 0;

        // Clearing comes first, so a wall can be swapped for a door in one plan.
        var laid = LaidEntities();
        foreach (var (stroke, _, price) in plan.Where(entry => entry.Brush.Kind == WFBlueprintKind.Erase))
        {
            if (Erase(gridUid, grid, stroke.Index, laid))
            {
                built++;
                owed += price;
            }
        }

        foreach (var (stroke, _, price) in plan.Where(entry => entry.Brush.Kind == WFBlueprintKind.CleanDecals))
        {
            if (CleanDecals(gridUid, stroke.Index))
            {
                built++;
                owed += price;
            }
        }

        var floorStrokes = plan.Where(entry => entry.Brush.Kind == WFBlueprintKind.Floor && entry.Brush.Tile != null).ToList();
        if (floorStrokes.Count > 0)
        {
            var done = floors(floorStrokes.Select(entry => (entry.Stroke.Index, entry.Brush.Tile!)).ToList(), out var refused).ToHashSet();
            reason ??= refused;
            foreach (var (stroke, _, price) in floorStrokes)
            {
                if (!done.Contains(stroke.Index))
                    continue;

                built++;
                owed += price;
            }
        }

        // Rugs and wall hangings go down before what stands over them.
        var under = LaidEntities(underOnly: true);
        foreach (var (stroke, brush, price) in plan.Where(entry => entry.Brush.Kind == WFBlueprintKind.Entity && entry.Brush.Entity != null)
                     .OrderByDescending(entry => entry.Brush.Under))
        {
            if (room <= 0)
            {
                reason ??= "wf-outpost-prefab-too-many";
                break;
            }

            if (!_maps.TryGetTileRef(gridUid, grid, stroke.Index, out var tile) || tile.Tile.IsEmpty)
            {
                reason ??= "wf-blueprint-no-floor";
                continue;
            }

            if (brush.Under ? Stands(gridUid, grid, stroke.Index, brush.Entity!) : IsTaken(gridUid, grid, stroke.Index, under))
            {
                reason ??= "wf-blueprint-taken";
                continue;
            }

            var ent = SpawnAtPosition(brush.Entity!, new EntityCoordinates(gridUid, stroke.Index + new Vector2(0.5f)));
            _transform.SetLocalRotation(ent, stroke.Facing.ToAngle());
            room--;
            built++;
            owed += price;
        }

        foreach (var (stroke, brush, price) in plan.Where(entry => entry.Brush.Kind == WFBlueprintKind.Decal && entry.Brush.Decal != null))
        {
            if (!_maps.TryGetTileRef(gridUid, grid, stroke.Index, out var tile) || tile.Tile.IsEmpty)
            {
                reason ??= "wf-blueprint-no-floor";
                continue;
            }

            EnsureComp<DecalGridComponent>(gridUid);
            if (DecalsOn(gridUid, stroke.Index).Count >= DecalsPerTile
                || !_decals.TryAddDecal(brush.Decal!, new EntityCoordinates(gridUid, stroke.Index), out _, rotation: stroke.Facing.ToAngle()))
            {
                reason ??= "wf-blueprint-decals-full";
                continue;
            }

            built++;
            owed += price;
        }

        foreach (var roofed in new[] { true, false })
        {
            var kind = roofed ? WFBlueprintKind.Roof : WFBlueprintKind.Sky;
            var cells = plan.Where(entry => entry.Brush.Kind == kind).ToList();
            if (cells.Count == 0)
                continue;

            if (roofs == null)
            {
                reason ??= "wf-blueprint-no-roof";
                continue;
            }

            var changed = roofs(cells.Select(entry => entry.Stroke.Index).ToList(), roofed).ToHashSet();
            foreach (var (stroke, _, price) in cells)
            {
                if (!changed.Contains(stroke.Index))
                    continue;

                built++;
                owed += price;
            }
        }

        spent = owed;
        if (total > owed)
            _bank.TryBankDeposit(user, total - owed, tax: false);

        return built;
    }

    /// <summary>
    /// Re-lays floor on a grid's own tiles and nowhere else: a ship's hull keeps its shape. Returns the tiles that changed.
    /// </summary>
    public List<Vector2i> RelayFloor(EntityUid gridUid, MapGridComponent grid, IReadOnlyList<(Vector2i Index, string Tile)> cells,
        out string? refused)
    {
        refused = null;
        var done = new List<Vector2i>();
        foreach (var (index, tileId) in cells)
        {
            if (!_tileDefs.TryGetDefinition(tileId, out var def))
                continue;

            if (!_maps.TryGetTileRef(gridUid, grid, index, out var existing) || existing.Tile.IsEmpty)
            {
                refused ??= "wf-blueprint-ship-no-hull";
                continue;
            }

            if (existing.Tile.TypeId == def.TileId)
                continue;

            _maps.SetTile(gridUid, grid, index, new Tile(def.TileId));
            done.Add(index);
        }

        return done;
    }

    /// <summary>The decals lying on a tile: those whose corner, which is where a decal is kept, falls in it.</summary>
    private HashSet<(uint Index, Decal Decal)> DecalsOn(EntityUid gridUid, Vector2i index)
    {
        var corner = (Vector2) index;
        return _decals.GetDecalsIntersecting(gridUid, new Box2(corner - new Vector2(0.01f), corner + new Vector2(0.99f)));
    }

    private bool CleanDecals(EntityUid gridUid, Vector2i index)
    {
        if (!HasComp<DecalGridComponent>(gridUid))
            return false;

        var found = DecalsOn(gridUid, index);
        foreach (var (id, _) in found)
        {
            _decals.RemoveDecal(gridUid, id);
        }

        return found.Count > 0;
    }

    /// <summary>Whether something anchored stands on a tile; what runs under the floor, rugs and wall hangings don't count.</summary>
    private bool IsTaken(EntityUid gridUid, MapGridComponent grid, Vector2i index, HashSet<string> under)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(gridUid, grid, index);
        while (anchored.MoveNext(out var ent))
        {
            if (!HasComp<SubFloorHideComponent>(ent.Value) && !(MetaData(ent.Value).EntityPrototype?.ID is { } id && under.Contains(id)))
                return true;
        }

        return false;
    }

    /// <summary>Whether an entity of a prototype already stands on a tile.</summary>
    private bool Stands(EntityUid gridUid, MapGridComponent grid, Vector2i index, string prototype)
    {
        _onTile.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, index, _onTile, flags: LookupFlags.Uncontained);
        foreach (var ent in _onTile)
        {
            if (MetaData(ent).EntityPrototype?.ID == prototype)
                return true;
        }

        return false;
    }

    /// <summary>Takes away what the brushes lay on a tile, anchored or not; nothing else, and nothing anyone carries.</summary>
    private bool Erase(EntityUid gridUid, MapGridComponent grid, Vector2i index, HashSet<string> laid)
    {
        _onTile.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, index, _onTile, flags: LookupFlags.Uncontained);
        var gone = new List<EntityUid>();
        foreach (var ent in _onTile)
        {
            if (MetaData(ent).EntityPrototype?.ID is { } id && laid.Contains(id) && Transform(ent).ParentUid == gridUid
                && _maps.LocalToTile(gridUid, grid, Transform(ent).Coordinates) == index)
                gone.Add(ent);
        }

        foreach (var ent in gone)
        {
            Del(ent);
        }

        return gone.Count > 0;
    }
}
