using Content.Client._WF.ShipPreview;
using Content.Shared._WF.Outposts;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;

namespace Content.Client._WF.Outposts;

/// <summary>A preview of one of the player's outpost saves, fetched from the server; hidden until asked to show one.</summary>
public sealed class WFOutpostPreviewBox : BoxContainer
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IResourceManager _resources = default!;

    private readonly Label _title;
    private readonly ShipPreviewControl _preview;

    /// <summary>The slot asked for and its name, until the answer comes.</summary>
    private int? _slot;
    private string? _name;
    private WFOutpostSavesSystem? _saves;

    public WFOutpostPreviewBox()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        Visible = false;

        var header = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
        _title = new Label { HorizontalExpand = true, ClipText = true };
        header.AddChild(_title);
        var close = new Button { Text = Loc.GetString("wf-outpost-preview-close") };
        close.OnPressed += _ => HidePreview();
        header.AddChild(close);
        AddChild(header);

        _preview = new ShipPreviewControl { MinHeight = 240, VerticalExpand = true };
        AddChild(_preview);

        var hint = new Label { Text = Loc.GetString("wf-ship-preview-hint") };
        hint.AddStyleClass("LabelSecondaryColor");
        AddChild(hint);
    }

    /// <summary>Asks the server for a slot's save and shows it once it comes.</summary>
    public void ShowSlot(int slot, string name)
    {
        _slot = slot;
        _name = name;
        Visible = true;
        _title.Text = Loc.GetString("wf-outpost-preview-title", ("name", name));
        _preview.ShowMessage("wf-ship-preview-loading");
        _entities.System<WFOutpostSavesSystem>().RequestPreview(slot);
    }

    /// <summary>Shows a preset from its grid file, which every client has.</summary>
    public void ShowPreset(WFOutpostPresetPrototype preset)
    {
        _slot = null;
        var name = Loc.GetString(preset.Name);
        Visible = true;
        _title.Text = Loc.GetString("wf-outpost-preview-title", ("name", name));
        if (!_resources.ContentFileExists(preset.Grid))
        {
            _preview.ShowMessage("wf-outpost-preview-failed");
            return;
        }

        _preview.SetGridText($"/WFOutpostPreset/{preset.ID}.yml", _resources.ContentFileReadAllText(preset.Grid), name);
    }

    /// <summary>Hides the preview and drops its grid.</summary>
    public void HidePreview()
    {
        _slot = null;
        Visible = false;
        _preview.Release();
    }

    private void OnPreview(WFOutpostPreviewEvent ev)
    {
        if (ev.Slot != _slot)
            return;

        _slot = null;
        if (ev.Yaml == null)
        {
            _preview.ShowMessage(ev.Error ?? "wf-outpost-preview-failed");
            return;
        }

        // The save time tells an overwritten slot from the version already shown.
        _preview.SetGridText($"/WFOutpostSave/{ev.Slot}/{ev.Saved.Ticks}.yml", ev.Yaml, _name);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (_entities.TrySystem(out _saves))
            _saves.PreviewReceived += OnPreview;
    }

    // The lobby can leave the tree after the client's systems are gone, so this keeps the one it subscribed to.
    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_saves != null)
            _saves.PreviewReceived -= OnPreview;
        _saves = null;
        HidePreview();
    }
}
