using System.Linq;
using System.Numerics;
using Content.Shared.Access;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.MappingMode.Link;

/// <summary>
/// Access levels as checkboxes grouped by access group; a reader accepts any one of the ticked levels. Apply sets the
/// clicked reader, Apply to selection every reader in the mapping tools' selection.
/// </summary>
public sealed class WFMappingAccessWindow : DefaultWindow
{
    private readonly Label _targetLabel;
    private readonly Button _copyButton;
    private readonly Dictionary<string, CheckBox> _boxes = new();
    private List<string>? _last;

    /// <summary>The reader the window was opened on.</summary>
    public EntityUid? Target { get; private set; }

    /// <summary>Raised with the ticked levels for the target.</summary>
    public event Action<List<string>>? Applied;

    /// <summary>Raised with the ticked levels for every reader in the selection.</summary>
    public event Action<List<string>>? AppliedToSelection;

    public WFMappingAccessWindow(IPrototypeManager prototypes)
    {
        Title = Loc.GetString("wf-mapping-link-access-title");
        MinSize = new Vector2(420, 300);
        SetSize = new Vector2(560, 600);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true,
        };

        _targetLabel = new Label { ClipText = true };
        root.AddChild(_targetLabel);
        root.AddChild(new Label
        {
            Text = Loc.GetString("wf-mapping-link-access-note"),
            StyleClasses = { "LabelSubText" },
        });

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        var groups = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        scroll.AddChild(groups);
        root.AddChild(scroll);

        foreach (var (name, levels) in Group(prototypes))
        {
            groups.AddChild(new Label { Text = name, StyleClasses = { "LabelHeading" } });
            var grid = new GridContainer { Columns = 3, HSeparationOverride = 8 };
            foreach (var level in levels)
            {
                var box = new CheckBox { Text = level.GetAccessLevelName(), HorizontalExpand = true };
                _boxes[level.ID] = box;
                grid.AddChild(box);
            }

            groups.AddChild(grid);
        }

        var footer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _copyButton = new Button { Text = Loc.GetString("wf-mapping-link-access-copy"), HorizontalExpand = true };
        var clearButton = new Button { Text = Loc.GetString("wf-mapping-link-access-clear"), HorizontalExpand = true };
        var applyButton = new Button { Text = Loc.GetString("wf-mapping-link-apply"), HorizontalExpand = true };
        var selectionButton = new Button
        {
            Text = Loc.GetString("wf-mapping-link-access-selection"),
            ToolTip = Loc.GetString("wf-mapping-link-access-selection-tooltip"),
            HorizontalExpand = true,
        };
        footer.AddChild(_copyButton);
        footer.AddChild(clearButton);
        footer.AddChild(applyButton);
        footer.AddChild(selectionButton);
        root.AddChild(footer);
        Contents.AddChild(root);

        _copyButton.Disabled = true;
        _copyButton.OnPressed += _ =>
        {
            if (_last != null)
                SetTicked(_last);
        };
        clearButton.OnPressed += _ => SetTicked(Array.Empty<string>());
        applyButton.OnPressed += _ => Applied?.Invoke(Ticked());
        selectionButton.OnPressed += _ => AppliedToSelection?.Invoke(Ticked());
    }

    /// <summary>
    /// Each access level under the smallest group holding it, so a catch-all group doesn't swallow the rest; levels
    /// in no group go under Other. Groups and levels are sorted by name.
    /// </summary>
    private static List<(string Name, List<AccessLevelPrototype> Levels)> Group(IPrototypeManager prototypes)
    {
        var groups = prototypes.EnumeratePrototypes<AccessGroupPrototype>()
            .OrderBy(g => g.Tags.Count)
            .ThenBy(g => g.GetAccessGroupName(), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var byGroup = new Dictionary<string, List<AccessLevelPrototype>>();
        var other = Loc.GetString("wf-mapping-link-access-other");

        foreach (var level in prototypes.EnumeratePrototypes<AccessLevelPrototype>())
        {
            var group = groups.FirstOrDefault(g => g.Tags.Contains(level.ID));
            var name = group?.GetAccessGroupName() ?? other;
            if (!byGroup.TryGetValue(name, out var list))
            {
                list = new List<AccessLevelPrototype>();
                byGroup[name] = list;
            }

            list.Add(level);
        }

        return byGroup
            .OrderBy(pair => pair.Key == other)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (pair.Key,
                pair.Value.OrderBy(l => l.GetAccessLevelName(), StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    /// <summary>Shows a reader and ticks the levels it accepts now.</summary>
    public void SetTarget(EntityUid target, string name, HashSet<string> current, List<string>? last)
    {
        Target = target;
        _targetLabel.Text = Loc.GetString("wf-mapping-link-access-target", ("name", name));
        SetTicked(current);
        SetLast(last);
    }

    /// <summary>The levels Copy from last ticks.</summary>
    public void SetLast(List<string>? last)
    {
        _last = last;
        _copyButton.Disabled = last == null;
    }

    private void SetTicked(IEnumerable<string> levels)
    {
        var ticked = new HashSet<string>(levels);
        foreach (var (id, box) in _boxes)
        {
            box.Pressed = ticked.Contains(id);
        }
    }

    private List<string> Ticked()
    {
        var levels = new List<string>();
        foreach (var (id, box) in _boxes)
        {
            if (box.Pressed)
                levels.Add(id);
        }

        levels.Sort(StringComparer.Ordinal);
        return levels;
    }
}
