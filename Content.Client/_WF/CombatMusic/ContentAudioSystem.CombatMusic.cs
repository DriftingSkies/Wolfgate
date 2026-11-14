using Content.Shared._WF.CombatMusic;
using Robust.Shared.Audio;

namespace Content.Client.Audio;

/// <summary>
/// Combat music from the character's own playlist, shuffled, and kept going track after track for as long as combat
/// mode stays on. A body with no playlist shuffles its faction's tracks the same way.
/// </summary>
public sealed partial class ContentAudioSystem
{
    private readonly List<string> _wfCombatBag = new();
    private EntityUid? _wfCombatStream;
    private float _wfCombatElapsed;
    private string? _wfCombatLast;
    private int _wfCombatPlaylistHash;

    /// <summary>Starts the next combat track. False when there is nothing to play.</summary>
    private bool WfPlayCombatTrack()
    {
        if (WfNextCombatPath() is not { } path)
            return false;

        PlayMusicTrack(path, _musicProto?.Sound.Params.Volume ?? 0f, _combatMusicFadeInTime, true);
        _wfCombatStream = _ambientMusicStream;
        _wfCombatElapsed = 0f;
        return true;
    }

    /// <summary>Moves on to another track when the current one ends, while still in combat.</summary>
    private void WfUpdateCombatPlaylist(float frameTime)
    {
        if (_currentlyPlaying != MusicType.Combat || !_isCombatMusicPlaying)
        {
            _wfCombatStream = null;
            return;
        }

        if (_wfMusicPauseReasons != WfMusicPauseReason.None)
            return;

        if (_ambientMusicStream != _wfCombatStream)
        {
            _wfCombatStream = _ambientMusicStream;
            _wfCombatElapsed = 0f;
        }

        _wfCombatElapsed += frameTime;
        if (_wfCombatElapsed >= _timeUntilNextAmbientTrack)
            WfPlayCombatTrack();
    }

    /// <summary>Draws from a shuffled bag so every track plays before any repeats, and never the same one twice running.</summary>
    private string? WfNextCombatPath()
    {
        var pool = WfCombatPool();
        if (pool.Count == 0)
            return null;

        var hash = string.Join('|', pool).GetHashCode();
        if (hash != _wfCombatPlaylistHash)
        {
            _wfCombatPlaylistHash = hash;
            _wfCombatBag.Clear();
        }

        if (_wfCombatBag.Count == 0)
        {
            _wfCombatBag.AddRange(pool);
            _random.Shuffle(_wfCombatBag);
            if (_wfCombatBag.Count > 1 && _wfCombatBag[^1] == _wfCombatLast)
                (_wfCombatBag[0], _wfCombatBag[^1]) = (_wfCombatBag[^1], _wfCombatBag[0]);
        }

        var next = _wfCombatBag[^1];
        _wfCombatBag.RemoveAt(_wfCombatBag.Count - 1);
        _wfCombatLast = next;
        return next;
    }

    /// <summary>The character's playlist, or the tracks of the faction combat music already chosen.</summary>
    private List<string> WfCombatPool()
    {
        var pool = new List<string>();
        if (TryComp<WFCombatMusicComponent>(_player.LocalEntity, out var music))
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
