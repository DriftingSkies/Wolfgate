#nullable enable
using System;
using Content.Client._WF.MappingMode.Export;
using Content.Shared._WF.MappingMode.Issues;
using NUnit.Framework;

namespace Content.Tests._WF.MappingMode;

/// <summary>
/// The client's issues scheduler sends the first request at once, holds one inside the server's gap and sends the
/// latest held scope once the gap has passed; a refusal pushes the retry a whole gap out.
/// </summary>
[TestFixture]
public sealed class WFMappingIssuesSchedulerTest
{
    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void FirstRequestGoesOut()
    {
        var scheduler = new WFMappingIssuesScheduler();
        Assert.That(scheduler.TryRequest(WFMappingIssueScope.Ship, At(10)), Is.True);
        Assert.That(scheduler.Pending, Is.Null);
        Assert.That(scheduler.TryTakeDue(At(20), out _), Is.False, "Nothing is held.");
    }

    [Test]
    public void EarlyRequestIsHeldAndSentWhenDue()
    {
        var scheduler = new WFMappingIssuesScheduler();
        Assert.That(scheduler.TryRequest(WFMappingIssueScope.Ship, At(10)), Is.True);
        Assert.That(scheduler.TryRequest(WFMappingIssueScope.Map, At(11)), Is.False, "Inside the gap.");
        Assert.That(scheduler.Pending, Is.EqualTo(WFMappingIssueScope.Map));
        Assert.That(scheduler.TryTakeDue(At(12.9), out _), Is.False, "Not yet.");
        Assert.That(scheduler.TryTakeDue(At(13), out var scope), Is.True);
        Assert.That(scope, Is.EqualTo(WFMappingIssueScope.Map));
        Assert.That(scheduler.Pending, Is.Null);
        Assert.That(scheduler.TryRequest(WFMappingIssueScope.Ship, At(14)), Is.False, "The sent retry starts a new gap.");
    }

    [Test]
    public void OnlyTheLatestHeldScopeIsKept()
    {
        var scheduler = new WFMappingIssuesScheduler();
        scheduler.TryRequest(WFMappingIssueScope.Ship, At(0));
        Assert.That(scheduler.TryRequest(WFMappingIssueScope.Map, At(1)), Is.False);
        Assert.That(scheduler.TryRequest(WFMappingIssueScope.Ship, At(2)), Is.False);
        Assert.That(scheduler.TryTakeDue(At(3), out var scope), Is.True);
        Assert.That(scope, Is.EqualTo(WFMappingIssueScope.Ship));
        Assert.That(scheduler.TryTakeDue(At(10), out _), Is.False, "One retry, not two.");
    }

    [Test]
    public void RefusalHoldsTheScopeForAWholeGap()
    {
        var scheduler = new WFMappingIssuesScheduler();
        scheduler.TryRequest(WFMappingIssueScope.Ship, At(0));
        scheduler.Refused(WFMappingIssueScope.Ship, At(0.1));
        Assert.That(scheduler.Pending, Is.EqualTo(WFMappingIssueScope.Ship));
        Assert.That(scheduler.TryTakeDue(At(3), out _), Is.False, "The gap counts from the refusal.");
        Assert.That(scheduler.TryTakeDue(At(3.1), out var scope), Is.True);
        Assert.That(scope, Is.EqualTo(WFMappingIssueScope.Ship));
    }

    [Test]
    public void RefusalKeepsANewerHeldScope()
    {
        var scheduler = new WFMappingIssuesScheduler();
        scheduler.TryRequest(WFMappingIssueScope.Ship, At(0));
        scheduler.TryRequest(WFMappingIssueScope.Map, At(0.05));
        scheduler.Refused(WFMappingIssueScope.Ship, At(0.1));
        Assert.That(scheduler.Pending, Is.EqualTo(WFMappingIssueScope.Map));
    }

    [Test]
    public void ClearDropsTheHeldScope()
    {
        var scheduler = new WFMappingIssuesScheduler();
        scheduler.TryRequest(WFMappingIssueScope.Ship, At(0));
        scheduler.TryRequest(WFMappingIssueScope.Map, At(1));
        scheduler.Clear();
        Assert.That(scheduler.Pending, Is.Null);
        Assert.That(scheduler.TryTakeDue(At(10), out _), Is.False);
    }
}
