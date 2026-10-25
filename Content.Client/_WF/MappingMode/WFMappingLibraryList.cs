using System.Globalization;
using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.MappingMode;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.MappingMode;

/// <summary>
/// A player's library maps with Open, Export, Download and Delete, each map's versions folding out underneath. Shared
/// by the lobby panel and the Mapping window's Library tab.
/// </summary>
public sealed class WFMappingLibraryList : BoxContainer
{
    private readonly HashSet<string> _expanded = new();
    private WFMappingLibraryEvent? _library;
    private bool _showOpen = true;

    /// <summary>Raised with a map and a version folder, or null for the newest.</summary>
    public event Action<string, string?>? OpenRequested;

    /// <summary>Raised with a map and a version folder, or null for the newest, to download its plain level files.</summary>
    public event Action<string, string?>? DownloadRequested;

    /// <summary>Raised with a map and a version folder, or null for the newest, to open the export wizard on it.</summary>
    public event Action<string, string?>? ExportRequested;

    /// <summary>Raised with a map and a version folder, or null for the whole map.</summary>
    public event Action<string, string?>? DeleteRequested;

    /// <summary>Whether maps can be opened from here; not inside a session.</summary>
    public bool ShowOpen
    {
        get => _showOpen;
        set
        {
            _showOpen = value;
            Rebuild();
        }
    }

    public WFMappingLibraryList()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;
        HorizontalExpand = true;
        Rebuild();
    }

    public void SetLibrary(WFMappingLibraryEvent? library)
    {
        _library = library;
        Rebuild();
    }

    private void Rebuild()
    {
        RemoveAllChildren();

        if (_library == null)
        {
            AddChild(SubLabel(Loc.GetString("wf-mapping-mode-library-loading")));
            return;
        }

        if (_library.Maps.Count == 0)
        {
            AddChild(SubLabel(Loc.GetString("wf-mapping-mode-library-empty")));
            return;
        }

        var maps = _library.Maps
            .OrderByDescending(m => m.Versions.Count > 0 ? m.Versions[0].Name.Stamp : DateTime.MinValue)
            .ToList();

        foreach (var map in maps)
        {
            AddChild(MapRow(map));
        }
    }

    private Control MapRow(WFMappingLibraryMap map)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        var header = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        box.AddChild(header);

        var text = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        text.AddChild(new Label { Text = map.Name, ClipText = true });
        var saved = map.Versions.Count > 0
            ? FormatStamp(map.Versions[0].Name.Stamp)
            : Loc.GetString("wf-mapping-mode-library-never");
        text.AddChild(SubLabel(Loc.GetString("wf-mapping-mode-library-map-info",
            ("count", map.Versions.Count), ("saved", saved))));
        header.AddChild(text);

        if (_showOpen)
            header.AddChild(ActionButton("wf-mapping-mode-library-open", () => OpenRequested?.Invoke(map.Name, null),
                "wf-mapping-mode-library-open-latest-tooltip"));

        header.AddChild(ActionButton("wf-mapping-export-library-button",
            () => ExportRequested?.Invoke(map.Name, null), "wf-mapping-export-library-latest-tooltip"));
        header.AddChild(SmallButton("wf-mapping-mode-library-download",
            () => DownloadRequested?.Invoke(map.Name, null), "wf-mapping-mode-library-download-latest-tooltip"));

        var expanded = _expanded.Contains(map.Name);
        header.AddChild(ActionButton(expanded ? "wf-mapping-mode-library-hide-versions" : "wf-mapping-mode-library-versions",
            () => ToggleVersions(map.Name)));
        header.AddChild(DeleteButton(() => DeleteRequested?.Invoke(map.Name, null),
            "wf-mapping-mode-library-delete-map-tooltip"));

        if (!expanded)
            return box;

        var versions = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 2,
            Margin = new Thickness(16, 2, 0, 0),
        };

        foreach (var version in map.Versions)
        {
            versions.AddChild(VersionRow(map.Name, version));
        }

        box.AddChild(versions);
        return box;
    }

    private Control VersionRow(string mapName, WFMappingLibraryVersion version)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var folder = version.Name.Folder;
        var kind = Loc.GetString(version.Name.Kind switch
        {
            WFMappingVersionKind.Auto => "wf-mapping-mode-kind-auto",
            WFMappingVersionKind.Upload => "wf-mapping-mode-kind-upload",
            _ => "wf-mapping-mode-kind-manual",
        });

        var text = Loc.GetString("wf-mapping-mode-library-version",
            ("version", "v" + version.Name.Number.ToString("D4", CultureInfo.InvariantCulture)),
            ("saved", FormatStamp(version.Name.Stamp)),
            ("kind", kind),
            ("levels", version.Levels),
            ("size", FormatMiB(version.Bytes)));
        if (version.Ship)
            text = Loc.GetString("wf-mapping-ship-library-version", ("text", text));

        row.AddChild(new Label
        {
            Text = text,
            HorizontalExpand = true,
            ClipText = true,
            StyleClasses = { "LabelSubText" },
        });

        if (_showOpen)
            row.AddChild(ActionButton("wf-mapping-mode-library-open", () => OpenRequested?.Invoke(mapName, folder)));

        row.AddChild(ActionButton("wf-mapping-export-library-button", () => ExportRequested?.Invoke(mapName, folder),
            "wf-mapping-export-library-tooltip"));
        row.AddChild(SmallButton("wf-mapping-mode-library-download", () => DownloadRequested?.Invoke(mapName, folder),
            "wf-mapping-export-library-download-tooltip"));
        row.AddChild(DeleteButton(() => DeleteRequested?.Invoke(mapName, folder)));
        return row;
    }

    private void ToggleVersions(string mapName)
    {
        if (!_expanded.Remove(mapName))
            _expanded.Add(mapName);

        Rebuild();
    }

    private static Button ActionButton(string text, Action pressed, string? tooltip = null)
    {
        var button = new Button { Text = Loc.GetString(text) };
        if (tooltip != null)
        {
            button.ToolTip = Loc.GetString(tooltip);
            button.TooltipDelay = 0;
        }

        button.OnPressed += _ => pressed();
        return button;
    }

    /// <summary>The plain level download beside Export: a smaller, secondary button.</summary>
    private static Button SmallButton(string text, Action pressed, string? tooltip = null)
    {
        var button = ActionButton(text, pressed, tooltip);
        button.StyleClasses.Add("OpenBoth");
        button.Label.StyleClasses.Add("LabelSubText");
        return button;
    }

    private static ConfirmButton DeleteButton(Action pressed, string? tooltip = null)
    {
        var button = new ConfirmButton { Text = Loc.GetString("wf-mapping-mode-library-delete") };
        if (tooltip != null)
        {
            button.ToolTip = Loc.GetString(tooltip);
            button.TooltipDelay = 0;
        }

        button.OnPressed += _ => pressed();
        return button;
    }

    private static Label SubLabel(string text)
    {
        return new Label { Text = text, StyleClasses = { "LabelSubText" }, ClipText = true };
    }

    /// <summary>A UTC save time in the player's local time.</summary>
    public static string FormatStamp(DateTime utc)
    {
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    public static string FormatMiB(long bytes)
    {
        return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
