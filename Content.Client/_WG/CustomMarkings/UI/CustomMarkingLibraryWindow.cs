using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client._WG.Stylesheets;
using Content.Client.Lobby;
using Content.Client.Stylesheets;
using Content.Shared._WG.CustomMarkings;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Utility;

namespace Content.Client._WG.CustomMarkings.UI;

/// <summary>
/// A player's library of custom markings, opened from the character creator: draw or import new ones, change or
/// remove old ones, and pick which the character being edited wears. It lists the character's own markings and the
/// shared ones; each can be switched between the two.
/// </summary>
public sealed partial class CustomMarkingLibraryWindow : CustomMarkingWindow
{
    private const int ThumbnailScale = 2;

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IFileDialogManager _dialog = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IResourceManager _res = default!;

    private readonly CustomMarkingSystem _system;
    private readonly Func<HumanoidCharacterProfile?> _profile;
    private readonly Func<int?> _slot;
    private readonly BoxContainer _rows;
    private readonly Label _counts;
    private readonly Label _status;
    private readonly Button _openFolder;
    private readonly Button _newButton;
    private readonly Button _importButton;

    /// <summary>The character, undressed and without custom markings, that each marking is shown on.</summary>
    private EntityUid? _doll;

    private List<CustomMarking> _worn = new();

    /// <summary>The entry whose Delete was pressed once and waits for a second press.</summary>
    private int _confirmingDelete;

    private bool _busy;

    /// <summary>Raised with the new list whenever the character's worn markings change here.</summary>
    public Action<List<CustomMarking>>? OnWornChanged;

    /// <param name="profile">The character being edited, as it is now.</param>
    /// <param name="slot">The character's slot; null, for a character not saved yet, lists only shared markings.</param>
    public CustomMarkingLibraryWindow(Func<HumanoidCharacterProfile?> profile, Func<int?>? slot = null)
    {
        IoCManager.InjectDependencies(this);
        _system = _entMan.System<CustomMarkingSystem>();
        _profile = profile;
        _slot = slot ?? (() => null);

        Title = Loc.GetString("wg-custom-marking-library-title");
        // A set size to open at: sizing itself, the window grows as wide as the screen to fit the hint on one line.
        MinSize = SetSize = new Vector2(760, 560);

        if (profile() is { } character)
        {
            var bare = character.WithCustomMarkings(new List<CustomMarking>());
            _doll = UserInterfaceManager.GetUIController<LobbyUIController>().LoadProfileEntity(bare, null, false);
        }

        var hint = new RichTextLabel { HorizontalExpand = true };
        hint.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("wg-custom-marking-library-hint")));

        _newButton = new Button { Text = Loc.GetString("wg-custom-marking-library-new"), StyleClasses = { StyleWolfgate.StyleClassCreatorPrimary } };
        _newButton.OnPressed += _ => OpenEditor(null, new CustomMarkingArt(), string.Empty);
        _importButton = new Button { Text = Loc.GetString("wg-custom-marking-library-import") };
        _importButton.OnPressed += _ => Import();
        _counts = new Label
        {
            HorizontalExpand = true,
            Align = Label.AlignMode.Right,
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
        };

        _rows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };

        _status = new Label { ClipText = true, HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        _openFolder = new Button { Text = Loc.GetString("wg-custom-marking-library-open-folder"), Visible = false };
        _openFolder.OnPressed += _ => _res.UserData.OpenOsWindow(ExportFolder);

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Children =
            {
                new PanelContainer
                {
                    StyleClasses = { StyleWolfgate.StyleClassCreatorCard },
                    Children =
                    {
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Vertical,
                            SeparationOverride = 6,
                            Children =
                            {
                                new Label { Text = Loc.GetString("wg-custom-marking-library-heading"), StyleClasses = { StyleWolfgate.StyleClassCreatorHeading } },
                                hint,
                                new BoxContainer
                                {
                                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                                    SeparationOverride = 6,
                                    Children = { _newButton, _importButton, _counts },
                                },
                            },
                        },
                    },
                },
                new ScrollContainer
                {
                    VerticalExpand = true,
                    HorizontalExpand = true,
                    HScrollEnabled = false,
                    Children = { _rows },
                },
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 6,
                    Children = { _status, _openFolder },
                },
            },
        });

        _system.LibraryUpdated += Rebuild;
        _system.ArtLoaded += OnArtLoaded;
        OnClose += () =>
        {
            _system.LibraryUpdated -= Rebuild;
            _system.ArtLoaded -= OnArtLoaded;
            if (_doll is { } doll)
                _entMan.DeleteEntity(doll);

            _doll = null;
        };

        _system.RequestLibrary();
        Rebuild();
    }

    private int MaxWorn => Math.Min(_cfg.GetCVar(CustomMarkingCVars.MaxWorn), CustomMarkingRules.MaxWornCap);

    /// <summary>Shows which markings the character wears. Call when the profile is loaded or changed elsewhere.</summary>
    public void SetWorn(IEnumerable<CustomMarking> worn)
    {
        _worn = worn.ToList();
        Rebuild();
    }

    private void OnArtLoaded(string hash)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        _rows.RemoveAllChildren();

        var library = _system.Library;
        var limit = _cfg.GetCVar(CustomMarkingCVars.LibraryLimit);
        var slot = _slot();
        var offered = library?.Where(entry => entry.OfferedTo(slot)).ToList() ?? new List<CustomMarkingEntry>();
        _counts.Text = Loc.GetString("wg-custom-marking-library-counts",
            ("saved", library?.Count ?? 0), ("limit", limit), ("worn", _worn.Count), ("max", MaxWorn),
            ("elsewhere", (library?.Count ?? 0) - offered.Count));
        var full = library == null
            ? Loc.GetString("wg-custom-marking-reason-loading")
            : library.Count >= limit
                ? Loc.GetString("wg-custom-marking-reason-library-full", ("limit", limit))
                : null;
        Allow(_newButton, Loc.GetString("wg-custom-marking-library-new-tooltip"), full);
        Allow(_importButton, Loc.GetString("wg-custom-marking-library-import-tooltip"), full);

        if (library == null)
        {
            _rows.AddChild(new Label { Text = Loc.GetString("wg-custom-marking-library-loading"), StyleClasses = { "LabelSubText" } });
            return;
        }

        if (offered.Count == 0 && _worn.Count == 0)
            _rows.AddChild(new Label { Text = Loc.GetString("wg-custom-marking-library-empty"), StyleClasses = { "LabelSubText" } });

        foreach (var entry in offered)
        {
            _rows.AddChild(EntryRow(entry, slot));
        }

        // Worn markings with no entry here: deleted from the library since, kept for another character, or from an
        // imported character.
        foreach (var marking in _worn)
        {
            if (!offered.Any(entry => entry.Hash == marking.Hash && entry.Placement == marking.Placement))
                _rows.AddChild(StrayRow(marking, full));
        }
    }

    /// <param name="slot">The slot of the character being edited, which a marking is narrowed to.</param>
    private Control EntryRow(CustomMarkingEntry entry, int? slot)
    {
        var marking = new CustomMarking(entry.Hash, entry.Placement);
        var worn = _worn.Contains(marking);
        var loaded = _system.TryGetArt(entry.Hash, out var rsi);

        var wear = new Button
        {
            Text = Loc.GetString(worn ? "wg-custom-marking-library-take-off" : "wg-custom-marking-library-wear"),
            ToggleMode = true,
            Pressed = worn,
            MinWidth = 90,
            StyleClasses = { StyleWolfgate.StyleClassCreatorToggle },
        };
        if (!worn && _worn.Count >= MaxWorn)
            Allow(wear, string.Empty, Loc.GetString("wg-custom-marking-reason-worn-full", ("max", MaxWorn)));
        wear.OnPressed += _ => SetWorn(marking, !worn);

        var loading = loaded ? null : Loc.GetString("wg-custom-marking-reason-loading");
        var edit = new CustomMarkingIconButton("pencil", string.Empty);
        Allow(edit, Loc.GetString("wg-custom-marking-library-edit"), loading);
        edit.OnPressed += _ =>
        {
            if (ReadArt(entry.Hash) is { } art)
                OpenEditor(entry, art, entry.Name);
        };

        var export = new CustomMarkingIconButton("export", string.Empty);
        Allow(export, Loc.GetString("wg-custom-marking-library-export"), loading);
        export.OnPressed += _ => Export(entry);

        // Deleting takes two presses: the icon turns into a question, which deletes.
        BaseButton delete;
        if (_confirmingDelete == entry.Id)
        {
            delete = new Button { Text = Loc.GetString("wg-custom-marking-library-delete-confirm"), StyleClasses = { StyleBase.ButtonCaution } };
            delete.OnPressed += _ =>
            {
                _system.Delete(entry.Id);
                _confirmingDelete = 0;
                Rebuild();
            };
        }
        else
        {
            delete = new CustomMarkingIconButton("trash", Loc.GetString("wg-custom-marking-library-delete"));
            delete.OnPressed += _ =>
            {
                _confirmingDelete = entry.Id;
                Rebuild();
            };
        }

        // Pressed, every character is offered the marking; up, only this one.
        var shared = new Button
        {
            Text = Loc.GetString("wg-custom-marking-library-all-characters"),
            ToggleMode = true,
            Pressed = entry.Slot == null,
            StyleClasses = { StyleWolfgate.StyleClassCreatorToggle },
        };
        Allow(shared, Loc.GetString("wg-custom-marking-library-all-characters-tooltip"),
            slot == null && entry.Slot == null ? Loc.GetString("wg-custom-marking-reason-unsaved-character") : null);
        shared.OnToggled += args => _system.SetScope(entry.Id, args.Pressed ? null : slot);

        var placement = Loc.GetString(CustomMarkingEditorWindow.PlacementName(entry.Placement));
        var subtitle = Loc.GetString(entry.Slot == null ? "wg-custom-marking-library-subtitle-all" : "wg-custom-marking-library-subtitle-character",
            ("placement", placement));
        return Row(rsi, entry.Hash, entry.Name, entry.Placement, subtitle, wear, shared, edit, export, delete);
    }

    /// <param name="full">Why the library can't take the marking, or null when it can.</param>
    private Control StrayRow(CustomMarking marking, string? full)
    {
        _system.TryGetArt(marking.Hash, out var rsi);

        var takeOff = new Button { Text = Loc.GetString("wg-custom-marking-library-take-off"), MinWidth = 90 };
        takeOff.OnPressed += _ => SetWorn(marking, false);

        var keep = new Button { Text = Loc.GetString("wg-custom-marking-library-keep") };
        Allow(keep, Loc.GetString("wg-custom-marking-library-keep-tooltip"),
            full ?? (rsi == null ? Loc.GetString("wg-custom-marking-reason-loading") : null));
        keep.OnPressed += _ =>
        {
            if (ReadArt(marking.Hash) is { } art)
                _system.Save(0, string.Empty, marking.Placement, art, _slot());
        };

        var subtitle = Loc.GetString("wg-custom-marking-library-stray-subtitle",
            ("placement", Loc.GetString(CustomMarkingEditorWindow.PlacementName(marking.Placement))));
        return Row(rsi, marking.Hash, Loc.GetString("wg-custom-marking-default-name"), marking.Placement, subtitle, takeOff, keep);
    }

    /// <summary>Enables a button with its tooltip, or disables it with the reason why under the tooltip.</summary>
    private static void Allow(BaseButton button, string tooltip, string? reason)
    {
        button.Disabled = reason != null;
        button.ToolTip = reason == null ? tooltip : tooltip.Length == 0 ? reason : $"{tooltip}\n{reason}";
    }

    /// <summary>
    /// A row: the marking on the character from each side, as it will look, its name, a line under it, and its
    /// actions.
    /// </summary>
    private Control Row(RSI? rsi, string hash, string name, CustomMarkingPlacement placement, string subtitle, params BaseButton[] actions)
    {
        RSI.State? state = null;
        rsi?.TryGetState(CustomMarkingResources.State, out state);

        byte[]? erase = null;
        if (rsi != null && _system.EraseEnabled)
            _system.TryGetErase(hash, out erase);

        var thumbnails = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
        for (var facing = 0; facing < CustomMarkingRules.Facings; facing++)
        {
            thumbnails.AddChild(new CustomMarkingCanvas(ThumbnailScale, false)
            {
                ArtState = state,
                Facing = facing,
                Body = _doll,
                Placement = placement,
                Erase = erase,
            });
        }

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            VerticalAlignment = VAlignment.Center,
        };
        foreach (var action in actions)
        {
            buttons.AddChild(action);
        }

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 12,
            Children =
            {
                thumbnails,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    VerticalAlignment = VAlignment.Center,
                    SeparationOverride = 2,
                    Children =
                    {
                        new Label { Text = name, ClipText = true, ToolTip = name, StyleClasses = { StyleWolfgate.StyleClassCreatorCardTitle } },
                        new Label
                        {
                            Text = subtitle,
                            ToolTip = subtitle,
                            ClipText = true,
                            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
                        },
                    },
                },
                buttons,
            },
        };

        return new PanelContainer
        {
            StyleClasses = { StyleWolfgate.StyleClassCreatorCard },
            HorizontalExpand = true,
            Children = { row },
        };
    }

    private void SetWorn(CustomMarking marking, bool worn)
    {
        if (worn == _worn.Contains(marking) || worn && _worn.Count >= MaxWorn)
            return;

        if (worn)
            _worn.Add(marking);
        else
            _worn.Remove(marking);

        WornChanged();
    }

    private void WornChanged()
    {
        SetStatus(null, false);
        OnWornChanged?.Invoke(_worn.ToList());
        Rebuild();
    }

    private CustomMarkingArt? ReadArt(string hash)
    {
        return _system.TryReadArt(hash, out var art) ? art : null;
    }

    /// <summary>Opens the editor; saving there puts a new marking on, or swaps a changed one the character wears.</summary>
    private void OpenEditor(CustomMarkingEntry? entry, CustomMarkingArt art, string name)
    {
        UserInterfaceManager.GetUIController<CustomMarkingUIController>()
            .OpenEditor(entry, art, name, _profile, worn => OnWornChanged?.Invoke(worn), slot: _slot());
    }

    private async void Import()
    {
        if (_busy)
            return;

        _busy = true;
        try
        {
            await using var file = await _dialog.OpenFile(new FileDialogFilters(new FileDialogFilters.Group("png")));
            if (file == null || Disposed)
                return;

            var art = file.Length <= CustomMarkingPng.MaxFileBytes ? CustomMarkingPng.Read(file.CopyToArray()) : null;
            if (art == null)
            {
                SetStatus("wg-custom-marking-error-import", true);
                return;
            }

            SetStatus(null, false);
            OpenEditor(null, art, string.Empty);
        }
        catch (Exception e)
        {
            _log.GetSawmill("wg.custom_markings").Error($"Importing a custom marking failed: {e}");
            SetStatus("wg-custom-marking-error-import", true);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Where exported markings go, under the game's own user data folder.</summary>
    private static readonly ResPath ExportFolder = new("/CustomMarkings");

    /// <summary>
    /// Writes the marking to the user data folder as "name.png" and offers to open that folder. The native save
    /// dialog let a name be typed without ".png" and wrote it as a file with no extension; the client can't rename
    /// what it picked.
    /// </summary>
    private void Export(CustomMarkingEntry entry)
    {
        if (_busy || !_system.TryGetPng(entry.Hash, out var png))
            return;

        try
        {
            _res.UserData.CreateDir(ExportFolder);
            var path = ExportPath(entry.Name);
            using (var stream = _res.UserData.OpenWrite(path))
                stream.Write(png);

            _status.Text = Loc.GetString("wg-custom-marking-exported", ("file", path.Filename));
            _status.StyleClasses.Remove("Danger");
            _openFolder.Visible = true;
        }
        catch (Exception e)
        {
            _log.GetSawmill("wg.custom_markings").Error($"Exporting a custom marking failed: {e}");
            SetStatus("wg-custom-marking-error-export", true);
        }
    }

    /// <summary>A free "name.png" in the export folder, keeping only characters every file system accepts.</summary>
    private ResPath ExportPath(string name)
    {
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        if (clean.Length == 0)
            clean = Loc.GetString("wg-custom-marking-default-name");

        var path = ExportFolder / $"{clean}.png";
        for (var copy = 2; _res.UserData.Exists(path); copy++)
            path = ExportFolder / $"{clean} ({copy}).png";
        return path;
    }

    private void SetStatus(string? loc, bool error)
    {
        _openFolder.Visible = false;
        _status.Text = loc == null ? string.Empty : Loc.GetString(loc);
        if (error)
            _status.StyleClasses.Add("Danger");
        else
            _status.StyleClasses.Remove("Danger");
    }
}
