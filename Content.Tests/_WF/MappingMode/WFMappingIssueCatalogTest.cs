using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Issues;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Tests._WF.MappingMode;

/// <summary>Every rule has a unique id and both of its Fluent strings, and issues sort errors first.</summary>
[TestFixture]
public sealed class WFMappingIssueCatalogTest
{
    private const string LocFile = "Resources/Locale/en-US/_WF/MappingMode/mapping-issues.ftl";

    private static readonly Regex MessageId = new(@"^([a-z0-9-]+)\s*=", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>The message ids of the issues locale file, found by walking up from the test binaries.</summary>
    private static HashSet<string> LocIds()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, LocFile)))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, $"{LocFile} not found above {TestContext.CurrentContext.TestDirectory}");
        var text = File.ReadAllText(Path.Combine(dir!.FullName, LocFile));
        return MessageId.Matches(text).Select(match => match.Groups[1].Value).ToHashSet();
    }

    [Test]
    public void RuleIdsAreUniqueAndLookedUp()
    {
        var rules = WFMappingIssueCatalog.Rules;
        Assert.That(rules.Select(rule => rule.Id), Is.Unique);
        Assert.That(rules, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var rule in rules)
            {
                Assert.That(rule.Id, Does.Match("^[a-z0-9-]+$"), rule.Id);
                Assert.That(WFMappingIssueCatalog.TryGet(rule.Id, out var found), Is.True, rule.Id);
                Assert.That(found, Is.EqualTo(rule));
            }

            Assert.That(WFMappingIssueCatalog.TryGet("no-such-rule", out _), Is.False);
            Assert.That(WFMappingIssueCatalog.Get(WFMappingIssueCatalog.More).Severity,
                Is.EqualTo(WFMappingIssueSeverity.Note));
        });
    }

    [Test]
    public void EveryRuleHasTitleAndHint()
    {
        var ids = LocIds();
        Assert.Multiple(() =>
        {
            foreach (var rule in WFMappingIssueCatalog.Rules)
            {
                Assert.That(ids, Does.Contain(rule.TitleKey), $"{rule.Id} has no title in {LocFile}");
                Assert.That(ids, Does.Contain(rule.HintKey), $"{rule.Id} has no hint in {LocFile}");
            }

            // No stray strings: every issue message belongs to a rule.
            var known = WFMappingIssueCatalog.Rules.SelectMany(rule => new[] { rule.TitleKey, rule.HintKey }).ToHashSet();
            foreach (var id in ids.Where(id => id.StartsWith(WFMappingIssueCatalog.KeyPrefix)))
            {
                Assert.That(known, Does.Contain(id), $"{id} names no rule");
            }
        });
    }

    [Test]
    public void CompareOrdersBySeverityRuleAndPlace()
    {
        static WFMappingIssue Make(string id, float x = 0, float y = 0, bool placed = true, int? depth = 0)
        {
            var rule = WFMappingIssueCatalog.Get(id);
            return new WFMappingIssue(rule.Severity, rule.TitleKey, new List<WFMappingLocArg>(), null,
                placed ? new NetCoordinates(NetEntity.First, new System.Numerics.Vector2(x, y)) : null, depth, id);
        }

        var issues = new List<WFMappingIssue>
        {
            Make(WFMappingIssueCatalog.ShipNoAtmos),
            Make(WFMappingIssueCatalog.MapDoubled, 3, 1),
            Make(WFMappingIssueCatalog.MapLooseEntity),
            Make(WFMappingIssueCatalog.MapDenied, 2, 2),
            Make(WFMappingIssueCatalog.MapDoubled, 1, 1),
            Make(WFMappingIssueCatalog.MapDoubled, placed: false),
            Make(WFMappingIssueCatalog.MapDenied, 0, 0, depth: 1),
            Make(WFMappingIssueCatalog.MapDenied, 5, 0),
        };
        issues.Sort(WFMappingIssueCatalog.Compare);

        Assert.That(issues.Select(issue => (issue.RuleId, issue.Depth, issue.Where?.Position)), Is.EqualTo(new[]
        {
            (WFMappingIssueCatalog.MapDenied, (int?) 0, (System.Numerics.Vector2?) new System.Numerics.Vector2(5, 0)),
            (WFMappingIssueCatalog.MapDenied, 0, new System.Numerics.Vector2(2, 2)),
            (WFMappingIssueCatalog.MapDenied, 1, new System.Numerics.Vector2(0, 0)),
            (WFMappingIssueCatalog.MapDoubled, 0, null),
            (WFMappingIssueCatalog.MapDoubled, 0, new System.Numerics.Vector2(1, 1)),
            (WFMappingIssueCatalog.MapDoubled, 0, new System.Numerics.Vector2(3, 1)),
            (WFMappingIssueCatalog.MapLooseEntity, 0, new System.Numerics.Vector2(0, 0)),
            (WFMappingIssueCatalog.ShipNoAtmos, 0, new System.Numerics.Vector2(0, 0)),
        }));
    }
}
