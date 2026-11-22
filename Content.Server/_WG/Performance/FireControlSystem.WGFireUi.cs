using Content.Server._WG.Performance;
using Content.Shared._Mono.FireControl;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    /// <summary>Least time between two console states rebuilt for fire messages. The cursor sends them 10 times a second.</summary>
    private static readonly TimeSpan WGFireUiInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Least time between two server refreshes a console's message runs; each walks every weapon in the world.</summary>
    private static readonly TimeSpan WGRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<EntityUid, TimeSpan> _wgNextFireUi = new();
    private readonly HashSet<EntityUid> _wgFireUiPending = new();
    private readonly List<EntityUid> _wgFireUiDue = new();
    private readonly Dictionary<EntityUid, TimeSpan?> _wgNextRefresh = new();

    /// <summary>
    /// Rebuilds a console's state after a fire message, at most every <see cref="WGFireUiInterval"/>. One held back is
    /// sent by <see cref="WGFlushFireUi"/> once the interval is up, so the last shot's ammo still shows.
    /// </summary>
    private void WGFireUi(EntityUid uid, FireControlConsoleComponent component)
    {
        var now = _timing.CurTime;
        if (_wgNextFireUi.TryGetValue(uid, out var next) && now < next)
        {
            _wgFireUiPending.Add(uid);
            return;
        }

        _wgNextFireUi[uid] = now + WGFireUiInterval;
        _wgFireUiPending.Remove(uid);
        UpdateUi(uid, component);
    }

    /// <summary>Sends the states fire messages held back, once each console's interval is up.</summary>
    private void WGFlushFireUi()
    {
        if (_wgFireUiPending.Count == 0)
            return;

        var now = _timing.CurTime;
        _wgFireUiDue.Clear();
        foreach (var uid in _wgFireUiPending)
        {
            if (!_wgNextFireUi.TryGetValue(uid, out var next) || now >= next)
                _wgFireUiDue.Add(uid);
        }

        foreach (var uid in _wgFireUiDue)
        {
            _wgFireUiPending.Remove(uid);
            if (TryComp(uid, out FireControlConsoleComponent? console))
                WGFireUi(uid, console);
        }
    }

    /// <summary>Whether a console's refresh message is run now; at most one every <see cref="WGRefreshInterval"/>.</summary>
    private bool WGTakeRefresh(EntityUid uid)
    {
        var due = _wgNextRefresh.GetValueOrDefault(uid);
        var taken = WGRequestCap.Take(ref due, _timing.CurTime, WGRefreshInterval, TimeSpan.Zero);
        _wgNextRefresh[uid] = due;
        return taken;
    }

    /// <summary>Forgets a console that is going away.</summary>
    private void WGForgetConsole(EntityUid uid)
    {
        _wgNextFireUi.Remove(uid);
        _wgFireUiPending.Remove(uid);
        _wgNextRefresh.Remove(uid);
    }
}
