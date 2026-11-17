using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._WG.MappingMode.Issues;

/// <summary>A rule the issues checker applies: its id, severity, and the Fluent ids of its title and fix hint.</summary>
public readonly record struct WGMappingIssueRule(
    string Id,
    WGMappingIssueSeverity Severity,
    WGMappingIssueScope Scope)
{
    /// <summary>The Fluent id of the one-line title; it takes the issue's arguments.</summary>
    public string TitleKey => $"{WGMappingIssueCatalog.KeyPrefix}{Id}";

    /// <summary>The Fluent id of the hint on how to fix it.</summary>
    public string HintKey => $"{WGMappingIssueCatalog.KeyPrefix}{Id}-hint";
}

/// <summary>
/// Every rule the checker knows, so a client can render a report without knowing the rules. Pure: the server
/// decides when a rule fires. Rules with the <see cref="WGMappingIssueScope.Map"/> scope run for every scope;
/// <see cref="WGMappingIssueScope.Ship"/> ones only for a ship.
/// </summary>
public static class WGMappingIssueCatalog
{
    public const string KeyPrefix = "wg-mapping-issue-";

    /// <summary>Most issues one reply carries; a <see cref="More"/> note closes a longer list.</summary>
    public const int MaxIssues = 200;

    /// <summary>The closing note of a capped list; its <c>count</c> is how many were left out.</summary>
    public const string More = "more";

    public const string ShipGridCount = "ship-grid-count";
    public const string ShipNoName = "ship-no-name";
    public const string ShipNoFloor = "ship-no-floor";
    public const string ShipNoConsole = "ship-no-console";
    public const string ShipNoDock = "ship-no-dock";
    public const string ShipNoThruster = "ship-no-thruster";
    public const string ShipNoGyroscope = "ship-no-gyroscope";
    public const string ShipNoPowerSource = "ship-no-power-source";
    public const string ShipNoApc = "ship-no-apc";
    public const string ShipNoBattery = "ship-no-battery";
    public const string ShipNoAtmos = "ship-no-atmos";
    public const string ShipNoSpawnPoint = "ship-no-spawn-point";
    public const string ShipGunOffHardpoint = "ship-gun-off-hardpoint";
    public const string ShipAppraisalZero = "ship-appraisal-zero";
    public const string ShipPriceFloor = "ship-price-floor";

    public const string MapLevelEmpty = "map-level-empty";
    public const string MapDoubled = "map-doubled";
    public const string MapAnchoredNoTile = "map-anchored-no-tile";
    public const string MapLooseEntity = "map-loose-entity";
    public const string MapUnsaved = "map-unsaved";
    public const string MapDenied = "map-denied";
    public const string MapLint = "map-lint";

    private static readonly WGMappingIssueRule[] RuleList =
    {
        new(ShipGridCount, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipNoName, WGMappingIssueSeverity.Warning, WGMappingIssueScope.Ship),
        new(ShipNoFloor, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipNoConsole, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipNoDock, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipNoThruster, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipNoGyroscope, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipNoPowerSource, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipNoApc, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipNoBattery, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipNoAtmos, WGMappingIssueSeverity.Note, WGMappingIssueScope.Ship),
        new(ShipNoSpawnPoint, WGMappingIssueSeverity.Warning, WGMappingIssueScope.Ship),
        new(ShipGunOffHardpoint, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipAppraisalZero, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(ShipPriceFloor, WGMappingIssueSeverity.Error, WGMappingIssueScope.Ship),
        new(MapLevelEmpty, WGMappingIssueSeverity.Warning, WGMappingIssueScope.Map),
        new(MapDoubled, WGMappingIssueSeverity.Warning, WGMappingIssueScope.Map),
        new(MapAnchoredNoTile, WGMappingIssueSeverity.Error, WGMappingIssueScope.Map),
        new(MapLooseEntity, WGMappingIssueSeverity.Warning, WGMappingIssueScope.Map),
        new(MapUnsaved, WGMappingIssueSeverity.Error, WGMappingIssueScope.Map),
        new(MapDenied, WGMappingIssueSeverity.Error, WGMappingIssueScope.Map),
        new(MapLint, WGMappingIssueSeverity.Error, WGMappingIssueScope.Map),
        new(More, WGMappingIssueSeverity.Note, WGMappingIssueScope.Map),
    };

    private static readonly Dictionary<string, WGMappingIssueRule> ById = BuildIndex();

    /// <summary>Every rule, in the order they are checked.</summary>
    public static IReadOnlyList<WGMappingIssueRule> Rules => RuleList;

    private static Dictionary<string, WGMappingIssueRule> BuildIndex()
    {
        var index = new Dictionary<string, WGMappingIssueRule>();
        foreach (var rule in RuleList)
        {
            index[rule.Id] = rule;
        }

        return index;
    }

    /// <summary>The rule with an id, if the catalog has it.</summary>
    public static bool TryGet(string id, out WGMappingIssueRule rule)
    {
        return ById.TryGetValue(id, out rule);
    }

    /// <summary>The rule with an id; throws on an unknown one.</summary>
    public static WGMappingIssueRule Get(string id)
    {
        return ById[id];
    }

    /// <summary>
    /// Puts issues in their reported order: errors, warnings, notes; within one severity by rule id, then by depth
    /// and grid position, placeless issues first.
    /// </summary>
    public static int Compare(WGMappingIssue a, WGMappingIssue b)
    {
        var order = a.Severity.CompareTo(b.Severity);
        if (order != 0)
            return order;

        order = string.CompareOrdinal(a.RuleId, b.RuleId);
        if (order != 0)
            return order;

        order = (a.Depth ?? int.MinValue).CompareTo(b.Depth ?? int.MinValue);
        if (order != 0)
            return order;

        if (a.Where is not { } wa)
            return b.Where is null ? 0 : -1;

        if (b.Where is not { } wb)
            return 1;

        order = wa.Position.Y.CompareTo(wb.Position.Y);
        return order != 0 ? order : wa.Position.X.CompareTo(wb.Position.X);
    }
}
