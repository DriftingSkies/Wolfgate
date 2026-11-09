using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Outposts;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.Outposts;

/// <summary>
/// The account's outpost saves, each with a preview button: in the character editor's Outposts tab with a delete button,
/// on an Outpost crew's page with a choose button, and the presets after them there.
/// </summary>
public sealed class WFOutpostSavesPanel : BoxContainer
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private readonly BoxContainer _slots;
    private readonly WFOutpostPreviewBox _preview;
    private WFOutpostSavesSystem? _saves;
    private readonly Action<int>? _choose;
    private readonly int? _chosen;
    private readonly Action<string>? _choosePreset;
    private readonly string? _chosenPreset;

    public WFOutpostSavesPanel() : this(null, null, "wf-outpost-editor-hint")
    {
    }

    /// <summary>
    /// A panel whose rows pick a save with <paramref name="choose"/> instead of deleting it, if given, and list the presets
    /// to pick with <paramref name="choosePreset"/>.
    /// </summary>
    public WFOutpostSavesPanel(Action<int>? choose, int? chosen, string hintKey, Action<string>? choosePreset = null,
        string? chosenPreset = null)
    {
        IoCManager.InjectDependencies(this);
        _choose = choose;
        _chosen = chosen;
        _choosePreset = choosePreset;
        _chosenPreset = chosenPreset;
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 8;
        Margin = new Thickness(8);

        var hint = new RichTextLabel();
        hint.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(hintKey)));
        AddChild(hint);

        _slots = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 6 };
        AddChild(_slots);

        _preview = new WFOutpostPreviewBox { VerticalExpand = true };
        AddChild(_preview);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (!_entities.TrySystem(out _saves))
            return;

        _saves.SavesUpdated += Rebuild;
        // The crew page is rebuilt on every crew update, so it asks only once.
        if (_choose == null || _saves.Slots == null)
            _saves.RequestSaves();
        Rebuild();
    }

    // The lobby can leave the tree after the client's systems are gone, so this keeps the one it subscribed to.
    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_saves != null)
            _saves.SavesUpdated -= Rebuild;
        _saves = null;
    }

    private void Rebuild()
    {
        _slots.RemoveAllChildren();
        var slots = _entities.System<WFOutpostSavesSystem>().Slots;
        if (slots == null)
        {
            _slots.AddChild(Secondary(Loc.GetString("wf-outpost-console-saves-reading")));
            return;
        }

        var any = false;
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i] is not { } slot)
                continue;

            any = true;
            _slots.AddChild(BuildRow(i, slot));
        }

        if (!any)
            _slots.AddChild(Secondary(Loc.GetString("wf-outpost-editor-none")));

        if (_choosePreset == null)
            return;

        _slots.AddChild(new Label { Text = Loc.GetString("wf-outpost-presets-title"), Margin = new Thickness(0, 6, 0, 0) });
        foreach (var preset in _proto.EnumeratePrototypes<WFOutpostPresetPrototype>().OrderBy(p => p.Order).ThenBy(p => p.ID))
        {
            _slots.AddChild(BuildPresetRow(preset, _choosePreset));
        }
    }

    private Control BuildPresetRow(WFOutpostPresetPrototype preset, Action<string> choose)
    {
        var preview = new Button { Text = Loc.GetString("wf-outpost-console-preview") };
        preview.OnPressed += _ => _preview.ShowPreset(preset);

        var chosen = _chosenPreset == preset.ID;
        var pick = new Button
        {
            Text = Loc.GetString(chosen ? "wf-outpost-saves-chosen" : "wf-outpost-saves-choose"),
            Disabled = chosen,
            Name = "OutpostChoosePreset" + preset.ID,
        };
        pick.OnPressed += _ => choose(preset.ID);

        var description = new FormattedMessage();
        description.PushColor(Color.DarkGray);
        description.AddText(Loc.GetString(preset.Description));
        description.Pop();
        var descriptionLabel = new RichTextLabel();
        descriptionLabel.SetMessage(description);

        var price = Secondary(Loc.GetString("wf-outpost-console-slot-price", ("price", preset.Price)));
        return ButtonRow(Loc.GetString(preset.Name), new Control[] { descriptionLabel, price }, preview, pick);
    }

    private Control BuildRow(int index, WFOutpostSaveSlot slot)
    {
        var title = Loc.GetString("wf-outpost-console-slot", ("slot", index + 1));
        var details = Secondary(Loc.GetString("wf-outpost-console-slot-details", ("tiles", slot.Tiles),
            ("entities", slot.Entities), ("saved", slot.Saved.ToLocalTime().ToString("g"))));
        var price = Secondary(slot.InUse
            ? Loc.GetString("wf-outpost-console-slot-in-use")
            : Loc.GetString("wf-outpost-console-slot-price", ("price", slot.Price)));
        var lines = new Control[] { details, price };

        var preview = new Button { Text = Loc.GetString("wf-outpost-console-preview") };
        preview.OnPressed += _ => _preview.ShowSlot(index, slot.Name);

        if (_choose is { } choose)
        {
            var chosen = _chosen == index;
            var pick = new Button
            {
                Text = Loc.GetString(chosen ? "wf-outpost-saves-chosen" : "wf-outpost-saves-choose"),
                Disabled = chosen || slot.InUse,
                Name = "OutpostChoose" + index,
            };
            pick.OnPressed += _ => choose(index);
            return ButtonRow($"{title}: {slot.Name}", lines, preview, pick);
        }

        var delete = new ConfirmButton { Text = Loc.GetString("wf-outpost-console-delete") };
        delete.OnPressed += _ => _entities.System<WFOutpostSavesSystem>().RequestDelete(index);
        return ButtonRow($"{title}: {slot.Name}", lines, preview, delete);
    }

    /// <summary>
    /// A row with its title and buttons on the top line and its lines below at the full width, so a long line neither
    /// runs under the buttons nor wraps into the line after it.
    /// </summary>
    private static Control ButtonRow(string title, IEnumerable<Control> lines, params Control[] buttons)
    {
        var top = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
        top.AddChild(new Label
        {
            Text = title,
            ClipText = true,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        });
        foreach (var button in buttons)
        {
            top.AddChild(button);
        }

        var row = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        row.AddChild(top);
        foreach (var line in lines)
        {
            row.AddChild(line);
        }

        return row;
    }

    private static Label Secondary(string text)
    {
        var label = new Label { Text = text };
        label.AddStyleClass("LabelSecondaryColor");
        return label;
    }
}
