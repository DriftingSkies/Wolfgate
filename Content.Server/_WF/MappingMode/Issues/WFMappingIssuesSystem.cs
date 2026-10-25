using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Issues;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.MappingMode.Issues;

/// <summary>
/// Checks a mapping session's levels for what the repo would refuse or a shipyard vessel would miss, and answers
/// members' <see cref="WFMappingIssuesRequest"/>. The rules are small methods in the partials; the catalog in
/// shared names them. <see cref="Check"/> and <see cref="HasErrors"/> serve the export stage.
/// </summary>
public sealed partial class WFMappingIssuesSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private MappingZLevelsSystem _zLevels = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFMappingModeSystem _mapping = default!;
    [Dependency] private WFMappingVesselSystem _vessel = default!;

    /// <summary>One check per three seconds per player.</summary>
    private readonly WFMappingThrottle<NetUserId> _throttle = new(1 / 3.0, 1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFMappingIssuesRequest>(OnRequest);
    }

    private void OnRequest(WFMappingIssuesRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_mapping.TryGetPresent(player, out var session) ||
            !Enum.IsDefined(ev.Scope))
            return;

        if (!_throttle.TryTake(player.UserId, _timing.CurTime))
        {
            Log.Debug($"Refused an issues request from {player}: too many");
            RaiseNetworkEvent(new WFMappingIssuesRefusedEvent(ev.Scope), player);
            return;
        }

        // The check reads the live levels, which a tester can neither see nor fix right now.
        if (_mapping.IsTesting(player))
        {
            _mapping.Notify(player, false, "wf-mapping-export-while-testing");
            return;
        }

        var issues = Check(session, ev.Scope, out var summary);
        RaiseNetworkEvent(new WFMappingIssuesEvent(ev.Scope, issues, summary), player);
    }

    /// <summary>The issues of a session's levels, in reported order and capped at the catalog's limit.</summary>
    public List<WFMappingIssue> Check(WFMappingSession session, WFMappingIssueScope scope, int? price = null)
    {
        return Check(session, scope, out _, price);
    }

    /// <summary>
    /// The issues of a session's levels and what was scanned. <paramref name="price"/> is the ship price to hold
    /// against the appraisal; the generated one when null.
    /// </summary>
    public List<WFMappingIssue> Check(WFMappingSession session, WFMappingIssueScope scope,
        out WFMappingIssuesSummary summary, int? price = null)
    {
        var report = new Report();
        var levels = _zLevels.GetLevels(session.RootMap, out var ground);
        var grids = new List<(EntityUid Grid, int Depth)>();
        for (var i = 0; i < levels.Count; i++)
        {
            CheckLevel(report, levels[i], i - ground, scope, grids);
        }

        if (scope == WFMappingIssueScope.Ship)
            CheckShip(report, levels.Count, grids, price);

        summary = new WFMappingIssuesSummary(levels.Count, report.Grids, report.Tiles, report.Entities, report.Appraisal);
        return report.Finish();
    }

    /// <summary>Whether an <see cref="WFMappingIssueSeverity.Error"/> stands, which refuses a ship export.</summary>
    public bool HasErrors(WFMappingSession session, WFMappingIssueScope scope, int? price = null)
    {
        return Check(session, scope, price).Exists(issue => issue.Severity == WFMappingIssueSeverity.Error);
    }

    /// <summary>Adds an issue for a rule, placed at an entity, a tile, or nowhere.</summary>
    private void Add(Report report, string ruleId, EntityUid? entity, EntityCoordinates? where, int? depth,
        params (string, object)[] args)
    {
        var rule = WFMappingIssueCatalog.Get(ruleId);
        var locArgs = new List<WFMappingLocArg>(args.Length);
        foreach (var (name, value) in args)
        {
            locArgs.Add(value switch
            {
                int i => new WFMappingLocArg(name, null, i),
                long l => new WFMappingLocArg(name, null, l),
                _ => new WFMappingLocArg(name, value.ToString() ?? string.Empty, 0),
            });
        }

        report.Issues.Add(new WFMappingIssue(rule.Severity, rule.TitleKey, locArgs,
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
        public readonly List<WFMappingIssue> Issues = new();
        public int Grids;
        public int Tiles;
        public int Entities;
        public double Appraisal;

        /// <summary>Sorts the list, cuts it at the cap and closes a cut list with the count left out.</summary>
        public List<WFMappingIssue> Finish()
        {
            Issues.Sort(WFMappingIssueCatalog.Compare);
            if (Issues.Count <= WFMappingIssueCatalog.MaxIssues)
                return Issues;

            var left = Issues.Count - WFMappingIssueCatalog.MaxIssues;
            Issues.RemoveRange(WFMappingIssueCatalog.MaxIssues, left);
            var rule = WFMappingIssueCatalog.Get(WFMappingIssueCatalog.More);
            Issues.Add(new WFMappingIssue(rule.Severity, rule.TitleKey,
                new List<WFMappingLocArg> { new("count", null, left) }, null, null, null, rule.Id));
            return Issues;
        }
    }
}
