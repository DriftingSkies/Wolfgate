using Content.Server._WG.MappingTools;
using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingMode.Issues;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.MappingMode.Issues;

/// <summary>
/// Checks a mapping session's levels for what the repo would refuse or a shipyard vessel would miss, and answers
/// members' <see cref="WGMappingIssuesRequest"/>. The rules are small methods in the partials; the catalog in
/// shared names them. <see cref="Check"/> and <see cref="HasErrors"/> serve the export stage.
/// </summary>
public sealed partial class WGMappingIssuesSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private MappingZLevelsSystem _zLevels = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGMappingModeSystem _mapping = default!;
    [Dependency] private WGMappingVesselSystem _vessel = default!;

    /// <summary>One check per three seconds per player.</summary>
    private readonly WGMappingThrottle<NetUserId> _throttle = new(1 / 3.0, 1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMappingIssuesRequest>(OnRequest);
    }

    private void OnRequest(WGMappingIssuesRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_mapping.TryGetPresent(player, out var session) ||
            !Enum.IsDefined(ev.Scope))
            return;

        if (!_throttle.TryTake(player.UserId, _timing.CurTime))
        {
            Log.Debug($"Refused an issues request from {player}: too many");
            RaiseNetworkEvent(new WGMappingIssuesRefusedEvent(ev.Scope), player);
            return;
        }

        // The check reads the live levels, which a tester can neither see nor fix right now.
        if (_mapping.IsTesting(player))
        {
            _mapping.Notify(player, false, "wg-mapping-export-while-testing");
            return;
        }

        var issues = Check(session, ev.Scope, out var summary);
        RaiseNetworkEvent(new WGMappingIssuesEvent(ev.Scope, issues, summary), player);
    }

    /// <summary>The issues of a session's levels, in reported order and capped at the catalog's limit.</summary>
    public List<WGMappingIssue> Check(WGMappingSession session, WGMappingIssueScope scope, int? price = null)
    {
        return Check(session, scope, out _, price);
    }

    /// <summary>
    /// The issues of a session's levels and what was scanned. <paramref name="price"/> is the ship price to hold
    /// against the appraisal; the generated one when null.
    /// </summary>
    public List<WGMappingIssue> Check(WGMappingSession session, WGMappingIssueScope scope,
        out WGMappingIssuesSummary summary, int? price = null)
    {
        var report = new Report();
        var levels = _zLevels.GetLevels(session.RootMap, out var ground);
        var grids = new List<(EntityUid Grid, int Depth)>();
        for (var i = 0; i < levels.Count; i++)
        {
            CheckLevel(report, levels[i], i - ground, scope, grids);
        }

        if (scope == WGMappingIssueScope.Ship)
            CheckShip(report, levels.Count, grids, price);

        summary = new WGMappingIssuesSummary(levels.Count, report.Grids, report.Tiles, report.Entities, report.Appraisal);
        return report.Finish();
    }

    /// <summary>Whether an <see cref="WGMappingIssueSeverity.Error"/> stands, which refuses a ship export.</summary>
    public bool HasErrors(WGMappingSession session, WGMappingIssueScope scope, int? price = null)
    {
        return Check(session, scope, price).Exists(issue => issue.Severity == WGMappingIssueSeverity.Error);
    }

    /// <summary>Adds an issue for a rule, placed at an entity, a tile, or nowhere.</summary>
    private void Add(Report report, string ruleId, EntityUid? entity, EntityCoordinates? where, int? depth,
        params (string, object)[] args)
    {
        var rule = WGMappingIssueCatalog.Get(ruleId);
        var locArgs = new List<WGMappingLocArg>(args.Length);
        foreach (var (name, value) in args)
        {
            locArgs.Add(value switch
            {
                int i => new WGMappingLocArg(name, null, i),
                long l => new WGMappingLocArg(name, null, l),
                _ => new WGMappingLocArg(name, value.ToString() ?? string.Empty, 0),
            });
        }

        report.Issues.Add(new WGMappingIssue(rule.Severity, rule.TitleKey, locArgs,
            entity is { } uid ? GetNetEntity(uid) : null,
            where is { } coords ? GetNetCoordinates(coords) : null,
            depth, ruleId));
    }

    /// <summary>The prototype's name, else the entity's, else its prototype id.</summary>
    private string NameOf(EntityUid uid)
    {
        var meta = MetaData(uid);
        if (!string.IsNullOrEmpty(meta.EntityName))
            return meta.EntityName;

        return meta.EntityPrototype?.Name is { Length: > 0 } name ? name : meta.EntityPrototype?.ID ?? string.Empty;
    }

    /// <summary>The issues found so far and the scan counts.</summary>
    private sealed class Report
    {
        public readonly List<WGMappingIssue> Issues = new();
        public int Grids;
        public int Tiles;
        public int Entities;
        public double Appraisal;

        /// <summary>Sorts the list, cuts it at the cap and closes a cut list with the count left out.</summary>
        public List<WGMappingIssue> Finish()
        {
            Issues.Sort(WGMappingIssueCatalog.Compare);
            if (Issues.Count <= WGMappingIssueCatalog.MaxIssues)
                return Issues;

            var left = Issues.Count - WGMappingIssueCatalog.MaxIssues;
            Issues.RemoveRange(WGMappingIssueCatalog.MaxIssues, left);
            var rule = WGMappingIssueCatalog.Get(WGMappingIssueCatalog.More);
            Issues.Add(new WGMappingIssue(rule.Severity, rule.TitleKey,
                new List<WGMappingLocArg> { new("count", null, left) }, null, null, null, rule.Id));
            return Issues;
        }
    }
}
