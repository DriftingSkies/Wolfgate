using Content.Shared._WG.MappingMode;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.MappingMode;

/// <summary>
/// The running download's progress with Cancel, the file a Save to download's dialog is asking for, or the finished
/// one's Open folder.
/// </summary>
public sealed class WGMappingDownloadRow : BoxContainer
{
    private readonly Label _label;
    private readonly Button _cancel;
    private readonly Button _openFolder;

    public event Action? CancelPressed;
    public event Action? OpenFolderPressed;

    public WGMappingDownloadRow()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        Visible = false;

        _label = new Label { HorizontalExpand = true, ClipText = true };
        _cancel = new Button { Text = Loc.GetString("wg-mapping-mode-download-cancel") };
        _openFolder = new Button { Text = Loc.GetString("wg-mapping-mode-download-open-folder") };
        _cancel.OnPressed += _ => CancelPressed?.Invoke();
        _openFolder.OnPressed += _ => OpenFolderPressed?.Invoke();

        AddChild(_label);
        AddChild(_cancel);
        AddChild(_openFolder);
    }

    public void SetProgress(WGMappingDownloadProgress? progress)
    {
        Visible = progress != null;
        if (progress == null)
            return;

        _cancel.Visible = progress.Active;
        _openFolder.Visible = !progress.Active && !progress.SaveTo;
        if (progress.Active)
        {
            _label.Text = Loc.GetString("wg-mapping-mode-download-progress",
                ("name", progress.MapName),
                ("version", progress.Version),
                ("file", progress.FileIndex + 1),
                ("files", progress.FileCount),
                ("percent", progress.ChunkCount == 0 ? 0 : progress.Chunks * 100 / progress.ChunkCount));
        }
        else if (progress.Asking is { } file)
        {
            _label.Text = Loc.GetString("wg-mapping-mode-download-asking",
                ("file", file),
                ("kind", Loc.GetString(KindKey(progress, file))),
                ("index", progress.AskingIndex + 1),
                ("count", progress.FileCount));
        }
        else if (progress.SaveTo)
        {
            _label.Text = Loc.GetString("wg-mapping-mode-download-saved-row",
                ("name", progress.MapName),
                ("version", progress.Version),
                ("saved", progress.Saved),
                ("count", progress.FileCount));
        }
        else
        {
            _label.Text = Loc.GetString("wg-mapping-mode-download-ready",
                ("name", progress.MapName), ("version", progress.Version));
        }

        _label.ToolTip = _label.Text;
    }

    /// <summary>What a downloaded file is, so the player can tell a ship's grid from its vessel prototype.</summary>
    private static string KindKey(WGMappingDownloadProgress progress, string file)
    {
        if (WGMappingLibraryNames.IsZipFile(file))
            return "wg-mapping-mode-download-kind-zip";

        if (file == WGMappingLibraryNames.DownloadVesselFile(progress.MapName))
            return "wg-mapping-mode-download-kind-vessel";

        // A grid export arrives under the ship grid's file name, which can equal a lowercase map's own file name.
        if (progress.Ship || progress.Zip)
            return "wg-mapping-mode-download-kind-grid";

        return file == WGMappingLibraryNames.DownloadMapFile(progress.MapName)
            ? "wg-mapping-mode-download-kind-map"
            : "wg-mapping-mode-download-kind-level";
    }
}
