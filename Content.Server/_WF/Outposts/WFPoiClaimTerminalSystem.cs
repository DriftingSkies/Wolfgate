using System.Numerics;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Parallax;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Puts a claim terminal in every claimable site once its world is ready, and handles it: examining shows what the
/// claim still needs, a multitool hacks it, and using it claims the site (<see cref="WFOutpostSystem.TryClaimSite"/>).
/// </summary>
public sealed class WFPoiClaimTerminalSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;

    public const string TerminalProto = "WFPoiClaimTerminal";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetPreloadedEvent>(OnPreloaded);
        SubscribeLocalEvent<WFPoiClaimTerminalComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFPoiClaimTerminalComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFPoiClaimTerminalComponent, WFPoiHackDoAfterEvent>(OnHacked);
        SubscribeLocalEvent<WFPoiClaimTerminalComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnPreloaded(ref WFPlanetPreloadedEvent args)
    {
        if (!_cfg.GetCVar(OutpostCVars.Enabled) || !TryComp<WFPlanetPoiComponent>(args.Ground, out var pois))
            return;

        foreach (var site in pois.Sites)
        {
            SpawnTerminal(args.Ground, site);
        }
    }

    /// <summary>
    /// Puts a claim terminal on a free floor tile of a claimable site, nearest its middle, broken for a site that is
    /// repaired to claim. Null for a site that can't be claimed or has no free floor.
    /// </summary>
    public EntityUid? SpawnTerminal(EntityUid ground, WFPlanetPoiSite site)
    {
        if (site.Terminal != null || site.ClaimedBy != null || site.Type is not { } typeId
            || !_proto.TryIndex(typeId, out var type) || type.Claim == WFPoiClaim.None
            || !TryComp<MapGridComponent>(ground, out var groundGrid))
            return null;

        var grid = _outposts.SiteGrid(ground, site) ?? ground;
        var gridComp = Comp<MapGridComponent>(grid);
        var middle = new Vector2((site.Bounds.Left + site.Bounds.Right) / 2f, (site.Bounds.Bottom + site.Bounds.Top) / 2f);
        var candidates = grid == ground
            ? _outposts.SiteTiles((ground, groundGrid), site)
            : AllTiles((grid, gridComp));
        if (grid != ground)
            middle = Vector2.Transform(middle, _transform.GetWorldMatrix(ground) * _transform.GetInvWorldMatrix(grid));

        Vector2i? best = null;
        var bestDistance = float.MaxValue;
        foreach (var index in candidates)
        {
            if (!IsFree((grid, gridComp), index, ground))
                continue;

            var distance = Vector2.DistanceSquared(index + new Vector2(0.5f), middle);
            if (distance >= bestDistance)
                continue;

            best = index;
            bestDistance = distance;
        }

        if (best is not { } tile)
            return null;

        var terminal = Spawn(TerminalProto, _maps.GridTileToLocal(grid, gridComp, tile));
        var comp = EnsureComp<WFPoiClaimTerminalComponent>(terminal);
        comp.Ground = ground;
        comp.Site = site;
        site.Terminal = terminal;
        if (type.Claim == WFPoiClaim.Repair)
            _damage.TryChangeDamage(terminal, new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(comp.BrokenDamage) } }, true);
        return terminal;
    }

    private List<Vector2i> AllTiles(Entity<MapGridComponent> grid)
    {
        var result = new List<Vector2i>();
        var tiles = _maps.GetAllTilesEnumerator(grid, grid.Comp);
        while (tiles.MoveNext(out var tile))
        {
            result.Add(tile.Value.GridIndices);
        }

        return result;
    }

    private bool IsFree(Entity<MapGridComponent> grid, Vector2i index, EntityUid ground)
    {
        if (!_maps.TryGetTileRef(grid, grid.Comp, index, out var tile) || tile.Tile.IsEmpty)
            return false;

        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, grid.Comp, index);
        while (anchored.MoveNext(out var ent))
        {
            if (grid.Owner != ground || !_biome.WfIsBiomeAnchored(ground, ent.Value, index))
                return false;
        }

        return true;
    }

    private void OnExamined(Entity<WFPoiClaimTerminalComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Site is not { } site || site.Type is not { } typeId || !_proto.TryIndex(typeId, out var type))
            return;

        args.PushMarkup(Loc.GetString("wf-poi-claim-examine-site", ("site", Loc.GetString(site.Name)), ("type", Loc.GetString(type.Name))));
        var needs = type.Claim switch
        {
            WFPoiClaim.Repair when CompOrNull<DamageableComponent>(ent)?.TotalDamage > 0 => "wf-poi-claim-examine-repair",
            WFPoiClaim.Hack when !ent.Comp.Hacked => "wf-poi-claim-examine-hack",
            _ => "wf-poi-claim-examine-ready",
        };
        args.PushMarkup(Loc.GetString(needs));
    }

    private void OnInteractUsing(Entity<WFPoiClaimTerminalComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Hacked || !_tool.HasQuality(args.Used, ent.Comp.HackQuality)
            || ent.Comp.Site?.Type is not { } typeId || !_proto.TryIndex(typeId, out var type) || type.Claim != WFPoiClaim.Hack)
            return;

        args.Handled = _tool.UseTool(args.Used, args.User, ent, (float) ent.Comp.HackDelay.TotalSeconds, ent.Comp.HackQuality,
            new WFPoiHackDoAfterEvent());
    }

    private void OnHacked(Entity<WFPoiClaimTerminalComponent> ent, ref WFPoiHackDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        ent.Comp.Hacked = true;
        _popup.PopupEntity(Loc.GetString("wf-poi-claim-hacked"), ent, args.User);
    }

    private void OnActivate(Entity<WFPoiClaimTerminalComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        var user = args.User;
        _popup.PopupEntity(_outposts.TryClaimSite(user, ent, out var outpost, out var reason)
            ? Loc.GetString("wf-poi-claim-done", ("outpost", Name(outpost.Value)))
            : Loc.GetString(reason), user, user);
    }
}
