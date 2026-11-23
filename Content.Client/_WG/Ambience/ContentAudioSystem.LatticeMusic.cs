using Content.Client._WG.Lattice;
using Content.Client.Gameplay;
using Content.Shared._WG.Lattice;
using Robust.Shared.Audio;

namespace Content.Client.Audio;

/// <summary>
/// A Lattice band with music plays it once on entry and holds other ambient music off while the player stays in it.
/// The piece re-arms once the player is back inside the band's edge.
/// </summary>
public sealed partial class ContentAudioSystem
{
    [Dependency] private WGLatticeHallucinationSystem _wgLatticeHallucinations = default!;

    private readonly HashSet<int> _wgBandMusicPlayed = new();
    private bool _wgBandHolding;
    private bool _wgStartingBandTrack;

    /// <summary>Whether a Lattice band is holding ordinary ambient music off.</summary>
    private bool WgBandHoldsMusic()
    {
        return _wgBandHolding && !_wgStartingBandTrack;
    }

    private void WgUpdateLatticeMusic()
    {
        var band = WgLocalBand(out var profile);
        _wgBandMusicPlayed.RemoveWhere(played => played > band);

        if (profile == null || band < 0 || band >= profile.Bands.Count || profile.Bands[band].Music is not { } music)
        {
            if (!_wgBandHolding)
                return;

            // Out of the band's hold: pick the ordinary music back up.
            _wgBandHolding = false;
            if (!_isCombatMusicPlaying && _state.CurrentState is GameplayState)
            {
                _currentlyPlaying = MusicType.None;
                SetMusic(_lastGrid, _lastBiome, _lastCombatState);
            }

            return;
        }

        _wgBandHolding = true;
        if (_wgBandMusicPlayed.Contains(band) ||
            _isCombatMusicPlaying ||
            _state.CurrentState is not GameplayState ||
            WgOnPlanet() ||
            !_proto.TryIndex(music, out var proto) ||
            !_proto.TryIndex<SoundCollectionPrototype>(proto.ID, out var collection) ||
            collection.PickFiles.Count == 0)
            return;

        _wgBandMusicPlayed.Add(band);
        _musicProto = proto;
        _wgStartingBandTrack = true;
        PlayMusicTrack(WgPickAmbientPath(collection), proto.Sound.Params.Volume, _ambientMusicFadeInTime, false);
        _wgStartingBandTrack = false;

        // Once only: no playlist follows it.
        _wgAmbientStream = null;
    }

    /// <summary>The local player's Lattice band and its profile, or -1 off any Lattice.</summary>
    private int WgLocalBand(out WGLatticeProfilePrototype? profile)
    {
        profile = null;
        if (_player.LocalEntity is not { } mob ||
            Transform(mob).MapUid is not { } map ||
            !TryComp<WGLatticeFieldComponent>(map, out var field) ||
            !_proto.TryIndex(field.Profile, out profile) ||
            _wgLatticeHallucinations.LocalDrift() is not { } drift)
            return -1;

        return drift.Band;
    }
}
