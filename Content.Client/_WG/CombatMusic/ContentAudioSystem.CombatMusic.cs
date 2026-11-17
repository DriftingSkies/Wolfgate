using System.Linq;
using Content.Client.Gameplay;
using Content.Shared._WG.CombatMusic;
using Robust.Shared.Audio;

namespace Content.Client.Audio;

/// <summary>
/// Combat music from the character's own playlist, shuffled, and kept going track after track for as long as combat
/// mode stays on. A body with no playlist shuffles its faction's tracks the same way.
/// </summary>
public sealed partial class ContentAudioSystem
{
    private readonly List<string> _wgCombatBag = new();
    private readonly List<string> _wgCombatPool = new();
    private EntityUid? _wgCombatStream;
    private string? _wgCombatLast;

    /// <summary>Starts the next combat track. False when there is nothing to play.</summary>
    private bool WgPlayCombatTrack()
    {
        if (WgNextCombatPath() is not { } path)
        {
            _wgCombatStream = null;
            return false;
        }

        PlayMusicTrack(path, _musicProto?.Sound.Params.Volume ?? 0f, _combatMusicFadeInTime, true);
        _wgCombatStream = _ambientMusicStream;
        return true;
    }

    /// <summary>
    /// Moves on to another track once the one this playlist started has played out. Anything else that stops or
    /// replaces it (round end, the lobby, an expedition countdown, leaving combat) ends the playlist instead.
    /// </summary>
    private void WgUpdateCombatPlaylist()
    {
        if (_wgCombatStream is not { } stream)
            return;

        if (_ambientMusicStream != stream ||
            _currentlyPlaying != MusicType.Combat ||
            !_isCombatMusicPlaying ||
            _state.CurrentState is not GameplayState)
        {
            _wgCombatStream = null;
            return;
        }

        // A finished track despawns itself; a paused one doesn't.
        if (Deleted(stream))
            WgPlayCombatTrack();
    }

    /// <summary>Draws from a shuffled bag so every track plays before any repeats, and never the same one twice running.</summary>
    private string? WgNextCombatPath()
    {
        var pool = WgCombatPool();
        if (pool.Count == 0)
            return null;

        if (!pool.SequenceEqual(_wgCombatPool))
        {
            _wgCombatPool.Clear();
            _wgCombatPool.AddRange(pool);
            _wgCombatBag.Clear();
        }

        if (_wgCombatBag.Count == 0)
        {
            _wgCombatBag.AddRange(pool);
            _random.Shuffle(_wgCombatBag);
            if (_wgCombatBag.Count > 1 && _wgCombatBag[^1] == _wgCombatLast)
                (_wgCombatBag[0], _wgCombatBag[^1]) = (_wgCombatBag[^1], _wgCombatBag[0]);
        }

        var next = _wgCombatBag[^1];
        _wgCombatBag.RemoveAt(_wgCombatBag.Count - 1);
        _wgCombatLast = next;
        return next;
    }

    /// <summary>The character's playlist, or the tracks of the faction combat music already chosen.</summary>
    private List<string> WgCombatPool()
    {
        var pool = new List<string>();
        if (TryComp<WGCombatMusicComponent>(_player.LocalEntity, out var music))
        {
            foreach (var id in music.Tracks)
            {
                if (_proto.TryIndex(id, out var track))
                    pool.Add(track.Path.ToString());
            }

            if (pool.Count > 0)
                return pool;
        }

        if (_musicProto != null && _proto.TryIndex<SoundCollectionPrototype>(_musicProto.ID, out var collection))
        {
            foreach (var file in collection.PickFiles)
            {
                pool.Add(file.ToString());
            }
        }

        return pool;
    }
}
