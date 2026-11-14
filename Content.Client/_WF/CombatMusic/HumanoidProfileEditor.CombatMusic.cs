using Content.Client._WF.CombatMusic;
using Content.Shared._WF.CombatMusic;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>The Combat music tab: the character's combat playlist.</summary>
public sealed partial class HumanoidProfileEditor
{
    private WFCombatMusicPicker _combatMusicPicker = default!;

    private void InitializeCombatMusic()
    {
        _combatMusicPicker = new WFCombatMusicPicker(_prototypeManager);
        _combatMusicPicker.OnChanged += SetCombatMusic;
        _combatMusicPicker.OnPreview += PreviewCombatMusic;
        CombatMusicContainer.AddChild(_combatMusicPicker);
        TabContainer.SetTabTitle(CombatMusicTab.GetPositionInParent(), Loc.GetString("wf-combat-music-tab"));

        var preview = _entManager.System<WFCombatMusicPreviewSystem>();
        preview.PreviewChanged += _combatMusicPicker.SetPreviewing;
        _combatMusicPicker.SetPreviewing(preview.Playing);
    }

    private void UpdateCombatMusicControls()
    {
        _combatMusicPicker.SetPlaylist(Profile?.CombatMusic ?? new List<ProtoId<WFCombatTrackPrototype>>());
    }

    private void SetCombatMusic(List<ProtoId<WFCombatTrackPrototype>> tracks)
    {
        if (Profile == null)
            return;

        Profile = Profile.WithCombatMusic(tracks);
        SetDirty();
    }

    private void PreviewCombatMusic(WFCombatTrackPrototype? track)
    {
        var preview = _entManager.System<WFCombatMusicPreviewSystem>();
        if (track == null)
            preview.Stop();
        else
            preview.Play(track);
    }

    /// <summary>Closing the editor only hides it, so a preview has to stop here too.</summary>
    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (!newVisible && _entManager.TrySystem<WFCombatMusicPreviewSystem>(out var preview))
            preview.Stop();
    }

    /// <summary>Stops any preview and lets go of the preview system when the editor goes away.</summary>
    private void DisposeCombatMusic()
    {
        if (!_entManager.TrySystem<WFCombatMusicPreviewSystem>(out var preview))
            return;

        preview.PreviewChanged -= _combatMusicPicker.SetPreviewing;
        preview.Stop();
    }
}
