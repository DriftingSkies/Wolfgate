using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.MappingMode;

/// <summary>
/// The running upload's progress with Cancel, the unknown-prototype warning with Load anyway and Cancel, or the
/// finished one with Open.
/// </summary>
public sealed class WGMappingUploadRow : BoxContainer
{
    private readonly Label _label;
    private readonly RichTextLabel _warning;
    private readonly Button _loadAnyway;
    private readonly Button _cancel;
    private readonly Button _open;
    private WGMappingUploadProgress? _progress;
    private bool _showOpen = true;

    public event Action? CancelPressed;
    public event Action? LoadAnywayPressed;

    /// <summary>Raised with the uploaded map and its version folder.</summary>
    public event Action<string, string>? OpenPressed;

    /// <summary>Whether the finished upload can be opened from here; not inside a session.</summary>
    public bool ShowOpen
    {
        get => _showOpen;
        set
        {
            _showOpen = value;
            SetProgress(_progress);
        }
    }

    public WGMappingUploadRow()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        Visible = false;

        _label = new Label { HorizontalExpand = true, ClipText = true };
        _warning = new RichTextLabel { HorizontalExpand = true, Visible = false };
        _loadAnyway = new Button
        {
            Text = Loc.GetString("wg-mapping-mode-upload-load-anyway"),
            ToolTip = Loc.GetString("wg-mapping-mode-upload-load-anyway-tooltip"),
            TooltipDelay = 0,
            VerticalAlignment = VAlignment.Top,
        };
        _cancel = new Button { Text = Loc.GetString("wg-mapping-mode-upload-cancel") };
        _open = new Button { Text = Loc.GetString("wg-mapping-mode-upload-open") };
        _cancel.OnPressed += _ => CancelPressed?.Invoke();
        _loadAnyway.OnPressed += _ => LoadAnywayPressed?.Invoke();
        _open.OnPressed += _ =>
        {
            if (_progress is { Stage: WGMappingUploadStage.Done } done)
                OpenPressed?.Invoke(done.MapName, done.Version);
        };

        AddChild(_label);
        AddChild(_warning);
        AddChild(_loadAnyway);
        AddChild(_cancel);
        AddChild(_open);
    }

    public void SetProgress(WGMappingUploadProgress? progress)
    {
        _progress = progress;
        Visible = progress != null;
        if (progress == null)
            return;

        var done = progress.Stage == WGMappingUploadStage.Done;
        var confirming = progress.Stage == WGMappingUploadStage.Confirming;
        _cancel.Visible = !done;
        _open.Visible = done && _showOpen;
        _loadAnyway.Visible = confirming;
        _warning.Visible = confirming;
        _label.Visible = !confirming;
        _cancel.VerticalAlignment = confirming ? VAlignment.Top : VAlignment.Stretch;
        if (confirming)
        {
            _warning.SetMessage(WarningText(progress));
            return;
        }

        _label.Text = progress.Stage switch
        {
            WGMappingUploadStage.Starting => Loc.GetString("wg-mapping-mode-upload-starting", ("name", progress.MapName)),
            WGMappingUploadStage.Sending => Loc.GetString("wg-mapping-mode-upload-progress", ("name", progress.MapName),
                ("percent", progress.ChunkCount == 0 ? 0 : progress.Acked * 100 / progress.ChunkCount)),
            WGMappingUploadStage.Checking => Loc.GetString("wg-mapping-mode-upload-checking", ("name", progress.MapName)),
            _ when progress.EntitiesToDrop > 0 => Loc.GetString("wg-mapping-mode-upload-ready-dropped",
                ("name", progress.MapName), ("version", progress.Version), ("dropped", progress.EntitiesToDrop)),
            _ => Loc.GetString("wg-mapping-mode-upload-ready", ("name", progress.MapName), ("version", progress.Version)),
        };
    }

    /// <summary>The warning for an upload with unknown prototypes, naming those the server listed.</summary>
    public static string WarningText(WGMappingUploadProgress progress)
    {
        var ids = string.Join(", ", progress.MissingPrototypes);
        if (progress.MissingPrototypeCount > progress.MissingPrototypes.Count)
            ids = $"{ids}, {Loc.GetString("wg-mapping-mode-upload-more")}";

        return Loc.GetString("wg-mapping-mode-upload-needs-confirm",
            ("count", progress.MissingPrototypeCount),
            ("entities", progress.EntitiesToDrop),
            ("ids", ids));
    }
}
