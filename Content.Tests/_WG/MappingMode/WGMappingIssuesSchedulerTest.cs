#nullable enable
using System;
using Content.Client._WG.MappingMode.Export;
using Content.Shared._WG.MappingMode.Issues;
using NUnit.Framework;

namespace Content.Tests._WG.MappingMode;

/// <summary>
/// The client's issues scheduler sends the first request at once, holds one inside the server's gap and sends the
/// latest held scope once the gap has passed; a refusal pushes the retry a whole gap out.
/// </summary>
[TestFixture]
public sealed class WGMappingIssuesSchedulerTest
{
    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void FirstRequestGoesOut()
    {
        var scheduler = new WGMappingIssuesScheduler();
        Assert.That(scheduler.TryRequest(WGMappingIssueScope.Ship, At(10)), Is.True);
        Assert.That(scheduler.Pending, Is.Null);
        Assert.That(scheduler.TryTakeDue(At(20), out _), Is.False, "Nothing is held.");
    }

    [Test]
    public void EarlyRequestIsHeldAndSentWhenDue()
    {
        var scheduler = new WGMappingIssuesScheduler();
        Assert.That(scheduler.TryRequest(WGMappingIssueScope.Ship, At(10)), Is.True);
        Assert.That(scheduler.TryRequest(WGMappingIssueScope.Map, At(11)), Is.False, "Inside the gap.");
        Assert.That(scheduler.Pending, Is.EqualTo(WGMappingIssueScope.Map));
        Assert.That(scheduler.TryTakeDue(At(12.9), out _), Is.False, "Not yet.");
        Assert.That(scheduler.TryTakeDue(At(13), out var scope), Is.True);
        Assert.That(scope, Is.EqualTo(WGMappingIssueScope.Map));
        Assert.That(scheduler.Pending, Is.Null);
        Assert.That(scheduler.TryRequest(WGMappingIssueScope.Ship, At(14)), Is.False, "The sent retry starts a new gap.");
    }

    [Test]
    public void OnlyTheLatestHeldScopeIsKept()
    {
        var scheduler = new WGMappingIssuesScheduler();
        scheduler.TryRequest(WGMappingIssueScope.Ship, At(0));
        Assert.That(scheduler.TryRequest(WGMappingIssueScope.Map, At(1)), Is.False);
        Assert.That(scheduler.TryRequest(WGMappingIssueScope.Ship, At(2)), Is.False);
        Assert.That(scheduler.TryTakeDue(At(3), out var scope), Is.True);
        Assert.That(scope, Is.EqualTo(WGMappingIssueScope.Ship));
        Assert.That(scheduler.TryTakeDue(At(10), out _), Is.False, "One retry, not two.");
    }

    [Test]
    public void RefusalHoldsTheScopeForAWholeGap()
    {
        var scheduler = new WGMappingIssuesScheduler();
        scheduler.TryRequest(WGMappingIssueScope.Ship, At(0));
        scheduler.Refused(WGMappingIssueScope.Ship, At(0.1));
        Assert.That(scheduler.Pending, Is.EqualTo(WGMappingIssueScope.Ship));
        Assert.That(scheduler.TryTakeDue(At(3), out _), Is.False, "The gap counts from the refusal.");
        Assert.That(scheduler.TryTakeDue(At(3.1), out var scope), Is.True);
        Assert.That(scope, Is.EqualTo(WGMappingIssueScope.Ship));
    }

    [Test]
    public void RefusalKeepsANewerHeldScope()
    {
        var scheduler = new WGMappingIssuesScheduler();
        scheduler.TryRequest(WGMappingIssueScope.Ship, At(0));
        scheduler.TryRequest(WGMappingIssueScope.Map, At(0.05));
        scheduler.Refused(WGMappingIssueScope.Ship, At(0.1));
        Assert.That(scheduler.Pending, Is.EqualTo(WGMappingIssueScope.Map));
    }

    [Test]
    public void ClearDropsTheHeldScope()
    {
        var scheduler = new WGMappingIssuesScheduler();
        scheduler.TryRequest(WGMappingIssueScope.Ship, At(0));
        scheduler.TryRequest(WGMappingIssueScope.Map, At(1));
        scheduler.Clear();
        Assert.That(scheduler.Pending, Is.Null);
        Assert.That(scheduler.TryTakeDue(At(10), out _), Is.False);
    }
}
