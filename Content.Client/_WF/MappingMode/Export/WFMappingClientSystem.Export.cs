using Content.Client._WF.MappingMode.Export;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Export;
using Content.Shared._WF.MappingMode.Issues;
using Robust.Shared.Timing;

namespace Content.Client._WF.MappingMode;

public sealed partial class WFMappingClientSystem
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly WFMappingIssuesScheduler _issuesScheduler = new();

    /// <summary>The last issues reply, for the export wizard.</summary>
    public WFMappingIssuesEvent? Issues { get; private set; }

    public event Action<WFMappingIssuesEvent>? IssuesReceived;

    /// <summary>Raised when the server refused a check as too soon; the scheduler sends it again after the gap.</summary>
    public event Action<WFMappingIssueScope>? IssuesRefused;

    private void InitializeExport()
    {
        SubscribeNetworkEvent<WFMappingIssuesEvent>(OnIssues);
        SubscribeNetworkEvent<WFMappingIssuesRefusedEvent>(OnIssuesRefused);
    }

    private void OnIssues(WFMappingIssuesEvent ev)
    {
        Issues = ev;
        IssuesReceived?.Invoke(ev);
    }

    private void OnIssuesRefused(WFMappingIssuesRefusedEvent ev)
    {
        _issuesScheduler.Refused(ev.Scope, _timing.RealTime);
        IssuesRefused?.Invoke(ev.Scope);
    }

    /// <summary>
    /// Asks the server to check the session for a scope; the reply comes as <see cref="IssuesReceived"/>. A request
    /// inside the server's three-second gap is held and sent from <see cref="UpdateIssues"/> once it has passed.
    /// </summary>
    public void RequestIssues(WFMappingIssueScope scope)
    {
        if (_issuesScheduler.TryRequest(scope, _timing.RealTime))
            RaiseNetworkEvent(new WFMappingIssuesRequest(scope));
    }

    /// <summary>Drops a held check, after the wizard gave up on it or left the session.</summary>
    public void CancelIssuesRequest()
    {
        _issuesScheduler.Clear();
    }

    /// <summary>Sends the held check once its gap has passed.</summary>
    private void UpdateIssues()
    {
        if (_issuesScheduler.TryTakeDue(_timing.RealTime, out var scope))
            RaiseNetworkEvent(new WFMappingIssuesRequest(scope));
    }

    /// <summary>Moves the observer to an issue's entity, or its coordinates.</summary>
    public void GoToIssue(WFMappingIssue issue)
    {
        if (issue.Entity == null && issue.Where == null)
            return;

        RaiseNetworkEvent(new WFMappingGoToIssueRequest(issue.Entity, issue.Where));
    }

    /// <summary>
    /// Asks for a library version packaged as one zip, as a ship with the given details or as a map; null is the newest
    /// version. The zip arrives through the download path as <c>&lt;Map&gt;.zip</c>.
    /// </summary>
    public void RequestExport(string mapName, string? version, WFMappingExportKind kind, WFMappingShipDetails? details)
    {
        if (RefuseWhileSaving())
            return;

        _requestedShip = false;
        _requestedZip = true;
        RaiseNetworkEvent(new WFMappingExportRequest(mapName, version, kind, details));
    }

    /// <summary>The ship details the library last listed for a map, if any.</summary>
    public WFMappingShipDetails? ShipDetailsOf(string mapName)
    {
        if (Library == null)
            return null;

        foreach (var map in Library.Maps)
        {
            if (string.Equals(map.Name, mapName, StringComparison.OrdinalIgnoreCase))
                return map.Ship;
        }

        return null;
    }

    /// <summary>Whether the library lists a version of a map as a ship; null for the newest version.</summary>
    public bool? IsShipVersion(string mapName, string? version)
    {
        if (Library == null)
            return null;

        foreach (var map in Library.Maps)
        {
            if (!string.Equals(map.Name, mapName, StringComparison.OrdinalIgnoreCase) || map.Versions.Count == 0)
                continue;

            if (version == null)
                return map.Versions[0].Ship;

            foreach (var found in map.Versions)
            {
                if (found.Name.Folder == version)
                    return found.Ship;
            }
        }

        return null;
    }
}
