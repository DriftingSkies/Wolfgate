using System.Numerics;
using Content.Client.Gameplay;
using Content.Shared._WG.Lattice;
using Robust.Shared.Audio;

namespace Content.Client.Audio;

/// <summary>
/// A Lattice band with music plays it once on entry and holds other ambient music off while the player stays in it.
/// The piece re-arms once the player is back inside the band's edge. The Coda's song outranks it.
/// </summary>
public sealed partial class ContentAudioSystem
{
    [Dependency] private SharedTransformSystem _wgTransform = default!;

    private readonly List<(Vector2 Position, float Strength)> _wgLatticeAnchors = new();
    private readonly HashSet<int> _wgBandMusicPlayed = new();
    private int _wgBand = -1;
    private float _wgBandInwardTimer;
    private bool _wgBandHolding;
    private bool _wgStartingBandTrack;
    private EntityUid? _wgBandStream;

    /// <summary>
    /// Whether a Lattice band is holding ordinary ambient music off. A combat track still playing when combat ends
    /// fades out here, so the band's piece can take over.
    /// </summary>
    private bool WgBandHoldsMusic()
    {
        if (!_wgBandHolding || _wgStartingBandTrack)
            return false;

        if (_isCombatMusicPlaying)
        {
            DisableAmbientMusic();
            _isCombatMusicPlaying = false;
        }

        return true;
    }

    private void WgUpdateLatticeMusic(float frameTime)
    {
        var profile = WgUpdateBand(frameTime);
        _wgBandMusicPlayed.RemoveWhere(played => played > _wgBand);

        if (profile == null || _wgBand < 0 || _wgBand >= profile.Bands.Count || profile.Bands[_wgBand].Music is not { } music)
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
        // The Coda's song comes first: the band's piece waits, unplayed, until the song lets the listener go. One the
        // song cut short plays again then.
        if (WgCodaSongHolding && _wgBandStream is { } cut && _ambientMusicStream == cut)
            _wgBandMusicPlayed.Remove(_wgBand);
        if (_isCombatMusicPlaying || WgCodaSongHolding || _state.CurrentState is not GameplayState)
            return;

        if (!_wgBandMusicPlayed.Contains(_wgBand) &&
            !WgOnPlanet() &&
            _proto.TryIndex(music, out var proto) &&
            proto.Sound is SoundCollectionSpecifier { Collection: { } collectionId } &&
            _proto.TryIndex<SoundCollectionPrototype>(collectionId, out var collection) &&
            collection.PickFiles.Count > 0)
        {
            _wgBandMusicPlayed.Add(_wgBand);
            _musicProto = proto;
            _wgStartingBandTrack = true;
            PlayMusicTrack(WgPickAmbientPath(collection), proto.Sound.Params.Volume, _ambientMusicFadeInTime, false);
            _wgStartingBandTrack = false;
            _wgBandStream = _ambientMusicStream;

            // Once only: no playlist follows it.
            _wgAmbientStream = null;
            return;
        }

        // Anything ordinary that started before the hold took over fades out; the band's own piece plays on.
        if (_ambientMusicStream is { } stream && stream != _wgBandStream)
            DisableAmbientMusic();
    }

    /// <summary>
    /// Works out the local player's band from the field, as the sky overlay does, so a ship change can't read a stale
    /// one. Moving out takes effect at once; moving in waits out the switch delay. Returns the profile, or null off any Lattice.
    /// </summary>
    private WGLatticeProfilePrototype? WgUpdateBand(float frameTime)
    {
        if (_player.LocalEntity is not { } mob ||
            Transform(mob).MapUid is not { } map ||
            !TryComp<WGLatticeFieldComponent>(map, out var field) ||
            !_proto.TryIndex(field.Profile, out var profile) ||
            field.Thresholds.Count == 0)
        {
            _wgBand = -1;
            _wgBandInwardTimer = 0f;
            return null;
        }

        _wgLatticeAnchors.Clear();
        for (var i = 0; i < field.Anchors.Count && i < field.Strengths.Count; i++)
        {
            _wgLatticeAnchors.Add((field.Anchors[i], field.Strengths[i]));
        }

        var position = _wgTransform.GetWorldPosition(mob);
        var strength = WGLatticeMath.Field(position, _wgLatticeAnchors, profile.Reach);
        var band = Math.Min(WGLatticeMath.Band(strength, field.Thresholds), profile.Bands.Count - 1);

        if (band >= _wgBand)
        {
            _wgBand = band;
            _wgBandInwardTimer = 0f;
        }
        else
        {
            _wgBandInwardTimer += frameTime;
            if (_wgBandInwardTimer >= WgAmbientSwitchDelay)
            {
                _wgBand = band;
                _wgBandInwardTimer = 0f;
            }
        }

        return profile;
    }
}
