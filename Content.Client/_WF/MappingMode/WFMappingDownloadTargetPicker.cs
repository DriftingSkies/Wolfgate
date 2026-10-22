using Content.Shared._WF.MappingMode;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.Client._WF.MappingMode;

/// <summary>
/// Where downloads go: the client's data folder, or a save dialog per file. Every picker shows the same
/// <see cref="WFMappingModeCVars.DownloadSaveTo"/>, which is kept between games.
/// </summary>
public sealed partial class WFMappingDownloadTargetPicker : BoxContainer
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private const int DataFolder = 0;
    private const int SaveTo = 1;

    private readonly OptionButton _target;

    public WFMappingDownloadTargetPicker()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;

        _target = new OptionButton
        {
            ToolTip = Loc.GetString("wf-mapping-mode-download-target-tooltip"),
            TooltipDelay = 0,
        };
        _target.AddItem(Loc.GetString("wf-mapping-mode-download-target-data"), DataFolder);
        _target.AddItem(Loc.GetString("wf-mapping-mode-download-target-save-to"), SaveTo);
        _target.OnItemSelected += args =>
        {
            _target.SelectId(args.Id);
            _cfg.SetCVar(WFMappingModeCVars.DownloadSaveTo, args.Id == SaveTo);
            _cfg.SaveToFile();
        };

        AddChild(new Label
        {
            Text = Loc.GetString("wf-mapping-mode-download-target"),
            StyleClasses = { "LabelSubText" },
            VAlign = Label.VAlignMode.Center,
        });
        AddChild(_target);
    }

    /// <summary>Whether this picker shows Save to.</summary>
    public bool ShowsSaveTo => _target.SelectedId == SaveTo;

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _cfg.OnValueChanged(WFMappingModeCVars.DownloadSaveTo, OnChanged, true);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _cfg.UnsubValueChanged(WFMappingModeCVars.DownloadSaveTo, OnChanged);
    }

    private void OnChanged(bool saveTo)
    {
        _target.SelectId(saveTo ? SaveTo : DataFolder);
    }
}
