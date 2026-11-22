using System.Linq;
using Content.Shared._WG.CustomMarkings;
using Content.Shared.Preferences;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;

namespace Content.Client._WG.CustomMarkings.UI;

/// <summary>Opens the marking editor, one at a time, for the library and the creator's tiles alike.</summary>
public sealed class CustomMarkingUIController : UIController
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private CustomMarkingEditorWindow? _editor;
    /// <summary>An editor asked to close for a character switch and left open by its unsaved edits.</summary>
    private CustomMarkingEditorWindow? _released;

    /// <summary>
    /// Opens the editor on a marking, over the character wearing everything but that marking. An editor already open
    /// with unsaved edits asks about them first and stays; false then.
    /// </summary>
    /// <param name="profile">The character being edited, as it is when the marking is saved.</param>
    /// <param name="wornChanged">Takes the character's new worn list when saving puts the marking on or swaps it.</param>
    /// <param name="replacing">
    /// A worn marking with no library entry that the art comes from: it is left off the body shown, and the saved
    /// marking takes its place.
    /// </param>
    /// <param name="slot">The character slot a new marking is offered to, or null for every character.</param>
    public bool OpenEditor(
        CustomMarkingEntry? entry,
        CustomMarkingArt art,
        string name,
        Func<HumanoidCharacterProfile?> profile,
        Action<List<CustomMarking>> wornChanged,
        CustomMarking? replacing = null,
        int? slot = null)
    {
        _editor?.Close();
        if (_editor is { IsOpen: true })
            return false;

        var edited = replacing ?? (entry is { } editing ? new CustomMarking(editing.Hash, editing.Placement) : null);
        var body = profile();
        if (body != null && edited is { } left)
            body = body.WithCustomMarkings(body.CustomMarkings.Where(m => m != left));

        var editor = new CustomMarkingEditorWindow(entry, art, name, body) { Slot = slot };
        editor.OnSaved += (before, saved) =>
        {
            // Asked to close for a character switch, it saves to the library but wears nothing on the new character.
            if (editor == _released || profile() is not { } current)
                return;

            var worn = current.CustomMarkings.ToList();
            var max = Math.Min(_cfg.GetCVar(CustomMarkingCVars.MaxWorn), CustomMarkingRules.MaxWornCap);
            if (replacing is { } stray && Replace(worn, stray, new CustomMarking(saved.Hash, saved.Placement))
                || replacing == null && CustomMarkingRules.ApplySaved(worn, before, saved, max))
                wornChanged(worn);
        };
        editor.OnClose += () =>
        {
            if (_editor == editor)
                _editor = null;
            if (_released == editor)
                _released = null;
        };

        _editor = editor;
        editor.OpenCentered();
        return true;
    }

    /// <summary>
    /// Closes the editor, asking first about unsaved edits. One left open by that question no longer belongs to the
    /// character it was opened for.
    /// </summary>
    public void CloseEditor()
    {
        _editor?.Close();
        if (_editor is { IsOpen: true })
            _released = _editor;
    }

    /// <summary>Swaps a worn marking for another in place. Returns whether the list changed.</summary>
    private static bool Replace(List<CustomMarking> worn, CustomMarking old, CustomMarking saved)
    {
        var at = worn.IndexOf(old);
        if (at < 0 || old == saved)
            return false;

        worn.RemoveAt(at);
        if (!worn.Contains(saved))
            worn.Insert(at, saved);

        return true;
    }
}
