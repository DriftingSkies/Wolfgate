using System.Numerics;
using Content.Shared._WF.MappingTools;
using Content.Shared.DeviceLinking;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.MappingMode.Link;

/// <summary>
/// The ports of a signal source (rows) against a sink's (columns) as checkboxes; Apply sets the links to the ticked
/// pairs. Link defaults ticks the game's default pairs, Unlink all clears every box.
/// </summary>
public sealed class WFMappingLinkPortsWindow : DefaultWindow
{
    private readonly IPrototypeManager _prototypes;
    private readonly Label _heading;
    private readonly GridContainer _grid;
    private readonly Button _defaultsButton;
    private readonly Button _applyButton;
    private readonly Dictionary<(string Source, string Sink), CheckBox> _boxes = new();
    private readonly List<(string Source, string Sink)> _defaults = new();

    private NetEntity _source;
    private NetEntity _sink;

    /// <summary>Raised with the source, the sink and the ticked port pairs.</summary>
    public event Action<NetEntity, NetEntity, List<(string Source, string Sink)>>? Applied;

    public WFMappingLinkPortsWindow(IPrototypeManager prototypes)
    {
        _prototypes = prototypes;
        Title = Loc.GetString("wf-mapping-link-ports-title");
        MinSize = new Vector2(380, 220);
        SetSize = new Vector2(480, 320);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true,
        };

        _heading = new Label { ClipText = true };
        root.AddChild(_heading);

        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        _grid = new GridContainer { HSeparationOverride = 10, VSeparationOverride = 2 };
        scroll.AddChild(_grid);
        root.AddChild(scroll);

        var footer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _defaultsButton = new Button { Text = Loc.GetString("wf-mapping-link-ports-defaults"), HorizontalExpand = true };
        var clearButton = new Button { Text = Loc.GetString("wf-mapping-link-ports-clear"), HorizontalExpand = true };
        _applyButton = new Button { Text = Loc.GetString("wf-mapping-link-apply"), HorizontalExpand = true };
        footer.AddChild(_defaultsButton);
        footer.AddChild(clearButton);
        footer.AddChild(_applyButton);
        root.AddChild(footer);
        Contents.AddChild(root);

        _defaultsButton.OnPressed += _ => SetTicked(_defaults);
        clearButton.OnPressed += _ => SetTicked(new List<(string Source, string Sink)>());
        _applyButton.OnPressed += _ => Applied?.Invoke(_source, _sink, Ticked());
    }

    /// <summary>Shows the ports of a source and a sink, ticked where they are linked now.</summary>
    public void SetPorts(MappingToolsLinkPortsEvent ev, string sourceName, string sinkName)
    {
        _source = ev.Source;
        _sink = ev.Sink;
        _boxes.Clear();
        _defaults.Clear();
        _defaults.AddRange(ev.Defaults);
        _grid.RemoveAllChildren();
        _heading.Text = Loc.GetString("wf-mapping-link-ports-heading", ("source", sourceName), ("sink", sinkName));
        _defaultsButton.Disabled = _defaults.Count == 0;
        _applyButton.Disabled = ev.SourcePorts.Count == 0 || ev.SinkPorts.Count == 0;

        if (ev.SourcePorts.Count == 0 || ev.SinkPorts.Count == 0)
        {
            _grid.Columns = 1;
            _grid.AddChild(new Label { Text = Loc.GetString("wf-mapping-link-ports-none") });
            return;
        }

        _grid.Columns = ev.SinkPorts.Count + 1;
        _grid.AddChild(new Label
        {
            Text = Loc.GetString("wf-mapping-link-ports-corner"),
            StyleClasses = { "LabelSubText" },
        });

        foreach (var sink in ev.SinkPorts)
        {
            _grid.AddChild(PortLabel<SinkPortPrototype>(sink));
        }

        foreach (var source in ev.SourcePorts)
        {
            _grid.AddChild(PortLabel<SourcePortPrototype>(source));
            foreach (var sink in ev.SinkPorts)
            {
                var box = new CheckBox { HorizontalAlignment = HAlignment.Center };
                _boxes[(source, sink)] = box;
                _grid.AddChild(box);
            }
        }

        SetTicked(ev.Links);
    }

    private Label PortLabel<T>(string id) where T : DevicePortPrototype, IPrototype
    {
        var label = new Label { Text = id };
        if (_prototypes.TryIndex<T>(id, out var port))
        {
            label.Text = Loc.GetString(port.Name);
            label.ToolTip = Loc.GetString(port.Description);
        }

        return label;
    }

    private void SetTicked(List<(string Source, string Sink)> links)
    {
        foreach (var (pair, box) in _boxes)
        {
            box.Pressed = links.Contains(pair);
        }
    }

    private List<(string Source, string Sink)> Ticked()
    {
        var links = new List<(string Source, string Sink)>();
        foreach (var (pair, box) in _boxes)
        {
            if (box.Pressed)
                links.Add(pair);
        }

        return links;
    }
}
