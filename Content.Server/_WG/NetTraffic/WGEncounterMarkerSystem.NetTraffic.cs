using System.Numerics;
using Content.Shared._WG.Encounters;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._WG.Encounters.Systems;

public sealed partial class WGEncounterMarkerSystem
{
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>How far a ship may stray from where clients extrapolate it before the list is sent again, in metres.</summary>
    private const float WgPositionTolerance = 1f;

    /// <summary>How much a ship's velocity may change before the list is sent again, in metres per second.</summary>
    private const float WgVelocityTolerance = 0.1f;

    /// <summary>Matches the client's cap on how long it moves a marker on by its velocity.</summary>
    private const float WgMaxExtrapolation = 10f;

    private List<WGEncounterMarker> _wgLastSent = new();
    private TimeSpan _wgLastSentAt;
    private readonly HashSet<ICommonSession> _wgJoined = new();

    /// <summary>How many times the markers went to every player.</summary>
    public int WgBroadcasts { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        _players.PlayerStatusChanged += WgOnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= WgOnPlayerStatusChanged;
        _wgJoined.Clear();
    }

    private void WgOnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame)
            _wgJoined.Add(args.Session);
        else
            _wgJoined.Remove(args.Session);
    }

    /// <summary>
    /// Sends the markers to everyone when they differ from what clients already show, else only to players who joined
    /// since the last update.
    /// </summary>
    private void WgSendMarkers(WGEncounterMarkersEvent ev)
    {
        var now = _timing.CurTime;
        if (WgChanged(ev.Markers, now))
        {
            _wgLastSent = ev.Markers;
            _wgLastSentAt = now;
            _wgJoined.Clear();
            WgBroadcasts++;
            RaiseNetworkEvent(ev, Filter.Broadcast());
            return;
        }

        if (_wgJoined.Count == 0)
            return;

        // An empty list is what a fresh client already holds.
        if (ev.Markers.Count > 0)
        {
            var filter = Filter.Empty();
            foreach (var session in _wgJoined)
            {
                if (session.Status == SessionStatus.InGame)
                    filter.AddPlayer(session);
            }

            RaiseNetworkEvent(ev, filter);
        }

        _wgJoined.Clear();
    }

    /// <summary>Whether the markers differ from the last ones sent by more than client extrapolation covers.</summary>
    private bool WgChanged(List<WGEncounterMarker> markers, TimeSpan now)
    {
        if (markers.Count != _wgLastSent.Count)
            return true;

        var elapsed = MathF.Min((float) (now - _wgLastSentAt).TotalSeconds, WgMaxExtrapolation);
        for (var i = 0; i < markers.Count; i++)
        {
            var a = markers[i];
            var b = _wgLastSent[i];
            if (a.Encounter != b.Encounter
                || a.Grid != b.Grid
                || a.Map != b.Map
                || a.Name != b.Name
                || a.Ship != b.Ship
                || a.Side != b.Side
                || a.Category != b.Category
                || a.Icon != b.Icon
                || a.Color != b.Color
                || a.WarnRange != b.WarnRange
                || a.AttackRange != b.AttackRange
                || a.Threat != b.Threat
                || a.Retrofitted != b.Retrofitted)
                return true;

            if (Vector2.Distance(a.Velocity, b.Velocity) > WgVelocityTolerance)
                return true;

            // The client stops extrapolating here, so a moving marker would freeze until it drifts out of tolerance.
            if (elapsed >= WgMaxExtrapolation && b.Velocity != Vector2.Zero)
                return true;

            var predicted = b.Position + b.Velocity * elapsed;
            if (Vector2.Distance(a.Position, predicted) > WgPositionTolerance)
                return true;
        }

        return false;
    }
}
