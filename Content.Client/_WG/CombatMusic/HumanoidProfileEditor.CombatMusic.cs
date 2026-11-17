using Content.Client._WG.CombatMusic;
using Content.Shared._WG.CombatMusic;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>The Combat music tab: the character's combat playlist.</summary>
public sealed partial class HumanoidProfileEditor
{
    private WGCombatMusicPicker _combatMusicPicker = default!;

    private void InitializeCombatMusic()
    {
        _combatMusicPicker = new WGCombatMusicPicker(_prototypeManager);
        _combatMusicPicker.OnChanged += SetCombatMusic;
        _combatMusicPicker.OnPreview += PreviewCombatMusic;
        CombatMusicContainer.AddChild(_combatMusicPicker);
        TabContainer.SetTabTitle(CombatMusicTab.GetPositionInParent(), Loc.GetString("wg-combat-music-tab"));

        var preview = _entManager.System<WGCombatMusicPreviewSystem>();
        preview.PreviewChanged += _combatMusicPicker.SetPreviewing;
        _combatMusicPicker.SetPreviewing(preview.Playing);
    }

    private void UpdateCombatMusicControls()
    {
        _combatMusicPicker.SetPlaylist(Profile?.CombatMusic ?? new List<ProtoId<WGCombatTrackPrototype>>());
    }

    private void SetCombatMusic(List<ProtoId<WGCombatTrackPrototype>> tracks)
    {
        if (Profile == null)
            return;

        Profile = Profile.WithCombatMusic(tracks);
        SetDirty();
    }

    private void PreviewCombatMusic(WGCombatTrackPrototype? track)
    {
        var preview = _entManager.System<WGCombatMusicPreviewSystem>();
        if (track == null)
            preview.Stop();
        else
            preview.Play(track);
    }

    /// <summary>Closing the editor only hides it, so a preview has to stop here too.</summary>
    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (!newVisible && _entManager.TrySystem<WGCombatMusicPreviewSystem>(out var preview))
            preview.Stop();
    }

    /// <summary>Stops any preview and lets go of the preview system when the editor goes away.</summary>
    private void DisposeCombatMusic()
    {
        if (!_entManager.TrySystem<WGCombatMusicPreviewSystem>(out var preview))
            return;

        preview.PreviewChanged -= _combatMusicPicker.SetPreviewing;
        preview.Stop();
    }
}
