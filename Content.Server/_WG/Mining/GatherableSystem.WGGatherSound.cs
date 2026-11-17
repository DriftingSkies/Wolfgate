using System.Numerics;
using Content.Server.Gatherable.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Gatherable;

/// <summary>
/// Rate-limits gather sounds. Mass mining breaks many rocks at once, and each break sound is a networked audio
/// entity that distant clients must apply.
/// </summary>
public sealed partial class GatherableSystem
{
    [Dependency] private IGameTiming _timing = default!;

    // Judgement call: close enough that one sound covers the cluster, short enough to keep the rhythm of mining.
    private static readonly TimeSpan WgGatherSoundWindow = TimeSpan.FromSeconds(0.2);
    private const float WgGatherSoundRadius = 3f;

    private readonly List<(MapId Map, Vector2 Position, TimeSpan Time)> _wgRecentGatherSounds = new();

    /// <summary>Plays the break sound unless one just played nearby.</summary>
    private void WgPlayGatherSound(EntityUid gathered, SoundOnGatherComponent sound)
    {
        var now = _timing.CurTime;
        _wgRecentGatherSounds.RemoveAll(e => now - e.Time > WgGatherSoundWindow);

        var map = _transform.GetMapCoordinates(gathered);
        foreach (var recent in _wgRecentGatherSounds)
        {
            if (recent.Map == map.MapId && (recent.Position - map.Position).LengthSquared() <= WgGatherSoundRadius * WgGatherSoundRadius)
                return;
        }

        _wgRecentGatherSounds.Add((map.MapId, map.Position, now));
        _audio.PlayPvs(sound.Sound, Transform(gathered).Coordinates);
    }
}
