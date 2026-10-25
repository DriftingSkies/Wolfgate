using Content.Shared._WF.MappingMode.Issues;

namespace Content.Client._WF.MappingMode.Export;

/// <summary>
/// Spaces issues requests to the server's one check per three seconds per player: a request inside the gap is held
/// and goes out once it has passed, and only the latest held scope is kept. Pure, so it is unit-tested on its own.
/// </summary>
public sealed class WFMappingIssuesScheduler
{
    /// <summary>The server's gap between two checks from one player.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private TimeSpan? _lastSent;
    private WFMappingIssueScope? _pending;

    /// <summary>The scope waiting to be sent, if any.</summary>
    public WFMappingIssueScope? Pending => _pending;

    /// <summary>Whether a request for <paramref name="scope"/> may go out now; otherwise it is held for <see cref="TryTakeDue"/>.</summary>
    public bool TryRequest(WFMappingIssueScope scope, TimeSpan now)
    {
        if (_lastSent is { } last && now - last < Interval)
        {
            _pending = scope;
            return false;
        }

        _lastSent = now;
        _pending = null;
        return true;
    }

    /// <summary>Takes the held scope once the gap has passed, so the caller sends it now.</summary>
    public bool TryTakeDue(TimeSpan now, out WFMappingIssueScope scope)
    {
        scope = default;
        if (_pending is not { } pending || _lastSent is { } last && now - last < Interval)
            return false;

        scope = pending;
        _pending = null;
        _lastSent = now;
        return true;
    }

    /// <summary>
    /// The server refused a request as too soon, which its clock may do when ours thought the gap had passed: the
    /// scope is held again, behind any newer one, and waits a whole gap from now.
    /// </summary>
    public void Refused(WFMappingIssueScope scope, TimeSpan now)
    {
        _pending ??= scope;
        _lastSent = now;
    }

    /// <summary>Drops the held scope.</summary>
    public void Clear()
    {
        _pending = null;
    }
}
