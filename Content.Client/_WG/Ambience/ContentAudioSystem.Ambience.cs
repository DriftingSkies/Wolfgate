using Content.Client.Gameplay;
using Content.Shared._Crescent.SpaceBiomes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Client.Audio;

/// <summary>
/// Ambient music playlist: the next track starts when the last one finishes, each pool plays through a shuffled bag,
/// and grid or biome changes wait a moment so crossing an airlock doesn't flip the music back and forth.
/// </summary>
public sealed partial class ContentAudioSystem
{
    /// <summary>Seconds a grid or biome change must hold before the music follows it.</summary>
    private const float WgAmbientSwitchDelay = 3f;

    /// <summary>Seconds an ambient track takes to fade out when another replaces it.</summary>
    private const float WgAmbientFadeOut = 4f;

    private readonly Dictionary<string, List<string>> _wgAmbientBags = new();
    private string? _wgAmbientLast;
    private EntityUid? _wgAmbientStream;

    private bool _wgSwitchPending;
    private float _wgSwitchTimer;
    private EntityUid? _wgPendingGrid;
    private ProtoId<SpaceBiomePrototype>? _wgPendingBiome;

    /// <summary>Queues a grid change; the music follows once it has held for the switch delay.</summary>
    private void WgQueueGridChange(EntityUid? grid)
    {
        WgSeedPending();
        _wgPendingGrid = grid;
        WgRestartSwitchTimer();
    }

    /// <summary>Queues a biome change; the music follows once it has held for the switch delay.</summary>
    private void WgQueueBiomeChange(ProtoId<SpaceBiomePrototype> biome)
    {
        WgSeedPending();
        _wgPendingBiome = biome;
        WgRestartSwitchTimer();
    }

    /// <summary>Starts a pending change from the current state, so a grid change keeps the biome and vice versa.</summary>
    private void WgSeedPending()
    {
        if (_wgSwitchPending)
            return;

        _wgPendingGrid = _lastGrid;
        _wgPendingBiome = _lastBiome;
    }

    private void WgRestartSwitchTimer()
    {
        _wgSwitchPending = true;
        _wgSwitchTimer = 0f;
    }

    /// <summary>Applies a queued grid or biome change once it has held, and starts the next track when one finishes.</summary>
    private void WgUpdateAmbientPlaylist(float frameTime)
    {
        WgUpdateLatticeMusic();

        if (_wgSwitchPending)
        {
            _wgSwitchTimer += frameTime;
            if (_wgSwitchTimer >= WgAmbientSwitchDelay)
            {
                _wgSwitchPending = false;
                SetMusic(_wgPendingGrid, _wgPendingBiome, _lastCombatState);
            }
        }

        if (_wgAmbientStream is not { } stream)
            return;

        // Anything that replaced or stopped the track (combat, the lobby, a planet, round end) ends the playlist.
        if (_ambientMusicStream != stream ||
            _isCombatMusicPlaying ||
            !_replayAmbientMusicBool ||
            _state.CurrentState is not GameplayState)
        {
            _wgAmbientStream = null;
            return;
        }

        // A finished track despawns itself; a paused one doesn't.
        if (Deleted(stream))
            ReplayAmbientMusic();
    }

    /// <summary>Remembers the ambient track just started so the playlist can follow it.</summary>
    private void WgTrackAmbientStream(bool combatMode)
    {
        _wgAmbientStream = combatMode ? null : _ambientMusicStream;
    }

    /// <summary>Draws from a shuffled bag per pool so every track plays before any repeats, never the same one twice running.</summary>
    private string WgPickAmbientPath(SoundCollectionPrototype collection)
    {
        if (!_wgAmbientBags.TryGetValue(collection.ID, out var bag))
        {
            bag = new List<string>();
            _wgAmbientBags[collection.ID] = bag;
        }

        if (bag.Count == 0)
        {
            foreach (var file in collection.PickFiles)
                bag.Add(file.ToString());
            _random.Shuffle(bag);

            // A fresh bag shouldn't open with the track that just played.
            if (bag.Count > 1 && bag[^1] == _wgAmbientLast)
                (bag[0], bag[^1]) = (bag[^1], bag[0]);
        }

        var path = bag[^1];
        bag.RemoveAt(bag.Count - 1);
        _wgAmbientLast = path;
        return path;
    }
}
