using System.Linq;
using Content.Shared._WF.Hideouts;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Hideouts;

/// <summary>
/// The outpost console's Hideout tab: the hangar with the owner's ship and its insurance, and the modules with their
/// levels, prices and what each needs.
/// </summary>
public sealed class WFHideoutPanel : BoxContainer
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly Color GoodColor = Color.FromHex("#6FCF6F");
    private static readonly Color BadColor = Color.FromHex("#E05A4F");

    /// <summary>The owner starts a module's next level.</summary>
    public event Action<string>? OnBuild;

    public event Action? OnRegister;
    public event Action? OnUnregister;
    public event Action? OnRecall;
    public event Action? OnInsure;
    public event Action? OnClaim;

    /// <summary>The owner moves the outpost's home world.</summary>
    public event Action<string>? OnMoveHome;

    private WFHideoutView? _view;
    private Label? _building;

    public WFHideoutPanel()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 8;
        Margin = new Thickness(8);
    }

    /// <summary>The list's scroll container and body, kept across updates so the list stays where it was scrolled to.</summary>
    private ScrollContainer? _scroll;
    private BoxContainer? _body;

    /// <summary>What the list was last built from; the console sends its state every second, and a rebuild is only for a change.</summary>
    private string? _built;

    /// <summary>Everything the list shows, as one string to compare; the build's end is left out, its countdown runs by itself.</summary>
    private static string Shown(WFHideoutView view, bool isOwner)
    {
        // Built piece by piece: one Join over this many values compiles to an inline array, which the client sandbox refuses.
        var shown = new System.Text.StringBuilder();
        void Add(object? value) => shown.Append(value).Append('|');

        Add(isOwner);
        Add(view.Building);
        Add(view.Balance);
        Add(view.Company);
        Add(view.HomeWorld);
        Add(view.MoveFee);
        foreach (var (module, level) in view.Levels.OrderBy(pair => pair.Key))
        {
            Add(module);
            Add(level);
        }

        foreach (var (thing, count) in view.Held.OrderBy(pair => pair.Key))
        {
            Add(thing);
            Add(count);
        }

        foreach (var world in view.Worlds)
        {
            Add(world.Id);
        }

        var hangar = view.Hangar;
        Add(hangar.HasPad);
        Add(hangar.Status);
        Add(hangar.ShipName);
        Add(hangar.Value);
        Add(hangar.InsuredValue);
        Add(hangar.Premium);
        Add(hangar.Deductible);
        Add(hangar.Unregistered);
        Add(hangar.Busy);
        return shown.ToString();
    }

    public void UpdateState(WFHideoutView? view, bool isOwner)
    {
        _view = view;
        if (view == null)
        {
            _building = null;
            _built = null;
            _scroll = null;
            _body = null;
            RemoveAllChildren();
            return;
        }

        var shown = Shown(view, isOwner);
        if (shown == _built && _body != null)
            return;

        _built = shown;
        _building = null;
        if (_scroll == null || _body == null)
        {
            RemoveAllChildren();
            _scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
            _body = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
            _scroll.AddChild(_body);
            AddChild(_scroll);
        }

        // Only the body is rebuilt, inside the same scroll container, so a change doesn't jump the list back to its top.
        var body = _body;
        body.RemoveAllChildren();

        body.AddChild(Card(Home(view, isOwner)));
        body.AddChild(Card(Hangar(view.Hangar, view.Balance, isOwner)));
        if (view.Building is { } building && _proto.TryIndex<WFHideoutModulePrototype>(building, out var underWay))
        {
            _building = new Label { FontColorOverride = GoodColor };
            body.AddChild(Card(_building));
            UpdateBuilding(underWay);
        }

        foreach (var module in _proto.EnumeratePrototypes<WFHideoutModulePrototype>().OrderBy(module => module.Order).ThenBy(module => module.ID))
        {
            body.AddChild(Card(Module(module, view, isOwner)));
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_building != null && _view?.Building is { } building && _proto.TryIndex<WFHideoutModulePrototype>(building, out var module))
            UpdateBuilding(module);
    }

    private void UpdateBuilding(WFHideoutModulePrototype module)
    {
        if (_building == null || _view == null)
            return;

        var left = Math.Max(0, (int) Math.Ceiling((_view.BuildEnds - _timing.CurTime).TotalSeconds));
        _building.Text = Loc.GetString("wf-hideout-ui-building", ("module", Loc.GetString(module.Name)),
            ("level", _view.Levels.GetValueOrDefault(module.ID) + 1), ("time", Duration(left)));
    }

    private static string Duration(int seconds)
    {
        return seconds >= 60
            ? Loc.GetString("wf-hideout-ui-minutes", ("minutes", seconds / 60), ("seconds", seconds % 60))
            : Loc.GetString("wf-hideout-ui-seconds", ("seconds", seconds));
    }

    /// <summary>The home world the outpost lands on each round, and moving it for the fee.</summary>
    private Control Home(WFHideoutView view, bool isOwner)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        box.AddChild(Heading(Loc.GetString("wf-hideout-ui-home")));
        var current = view.Worlds.FirstOrDefault(w => w.Id == view.HomeWorld)?.Name ?? view.HomeWorld ?? Loc.GetString("wf-hideout-ui-home-unset");
        box.AddChild(new Label { Text = Loc.GetString("wf-hideout-ui-home-world", ("world", current)) });
        box.AddChild(Secondary(Loc.GetString("wf-hideout-ui-home-help")));

        var picker = new OptionButton { HorizontalExpand = true, Disabled = !isOwner };
        var others = view.Worlds.Where(w => w.Id != view.HomeWorld).ToList();
        for (var i = 0; i < others.Count; i++)
            picker.AddItem(others[i].Name, i);
        picker.OnItemSelected += args => picker.SelectId(args.Id);
        var move = Button(Loc.GetString("wf-hideout-ui-home-move", ("price", view.MoveFee)),
            !isOwner || others.Count == 0 || view.MoveFee > view.Balance,
            () =>
            {
                if (picker.SelectedId >= 0 && picker.SelectedId < others.Count)
                    OnMoveHome?.Invoke(others[picker.SelectedId].Id);
            });
        box.AddChild(Row(picker, move));
        return box;
    }

    private Control Hangar(WFHideoutHangarView hangar, int balance, bool isOwner)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        box.AddChild(Heading(Loc.GetString("wf-hideout-ui-hangar")));
        if (!hangar.HasPad)
            box.AddChild(Secondary(Loc.GetString("wf-hideout-ui-no-hangar")));

        var status = Loc.GetString($"wf-hideout-ui-ship-{hangar.Status.ToString().ToLowerInvariant()}");
        box.AddChild(hangar.ShipName is { } name
            ? new Label { Text = Loc.GetString("wf-hideout-ui-ship", ("name", name), ("status", status), ("value", hangar.Value)) }
            : Secondary(status));
        box.AddChild(Secondary(hangar.InsuredValue > 0
            ? Loc.GetString("wf-hideout-ui-insured", ("value", hangar.InsuredValue))
            : Loc.GetString("wf-hideout-ui-uninsured")));

        var buttons = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6 };
        var locked = !isOwner || hangar.Busy;
        if (hangar.Status == WFHideoutShipStatus.None)
        {
            buttons.AddChild(Button(hangar.Unregistered is { } parked
                    ? Loc.GetString("wf-hideout-ui-register", ("name", parked))
                    : Loc.GetString("wf-hideout-ui-register-none"),
                locked || hangar.Unregistered == null, () => OnRegister?.Invoke()));
        }
        else
        {
            if (hangar.Status == WFHideoutShipStatus.Stored)
                buttons.AddChild(Button(Loc.GetString("wf-hideout-ui-recall"), locked || !hangar.HasPad, () => OnRecall?.Invoke()));
            if (hangar.Status == WFHideoutShipStatus.Parked)
            {
                buttons.AddChild(Button(Loc.GetString("wf-hideout-ui-insure", ("price", hangar.Premium)),
                    locked || hangar.Premium > balance, () => OnInsure?.Invoke()));
            }

            if (hangar.InsuredValue > 0)
            {
                buttons.AddChild(Button(Loc.GetString("wf-hideout-ui-claim", ("price", hangar.Deductible)),
                    locked || !hangar.HasPad || hangar.Deductible > balance, () => OnClaim?.Invoke()));
            }

            buttons.AddChild(Button(Loc.GetString("wf-hideout-ui-unregister"), locked, () => OnUnregister?.Invoke()));
        }

        box.AddChild(buttons);
        return box;
    }

    private Control Module(WFHideoutModulePrototype module, WFHideoutView view, bool isOwner)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        var level = view.Levels.GetValueOrDefault(module.ID);
        var title = Heading(Loc.GetString(module.Name));
        title.HorizontalExpand = true;
        box.AddChild(Row(title, new Label { Text = Loc.GetString("wf-hideout-ui-level", ("level", level), ("max", module.Levels.Count)) }));
        box.AddChild(Secondary(Loc.GetString(module.Description)));
        if (level >= module.Levels.Count)
        {
            box.AddChild(new Label { Text = Loc.GetString("wf-hideout-ui-complete"), FontColorOverride = GoodColor });
            return box;
        }

        var next = module.Levels[level];
        if (next.Summary is { } summary)
            box.AddChild(new Label { Text = Loc.GetString("wf-hideout-ui-next", ("summary", Loc.GetString(summary))) });

        var ready = !module.Company || view.Company;
        if (!ready)
            box.AddChild(new Label { Text = Loc.GetString("wf-hideout-ui-company-only"), FontColorOverride = BadColor });

        foreach (var (stack, amount) in next.Stacks)
        {
            var held = view.Held.GetValueOrDefault("stack:" + stack.Id);
            ready &= held >= amount;
            var name = _proto.TryIndex(stack, out var proto) ? Loc.GetString(proto.Name) : stack.Id;
            box.AddChild(new Label
            {
                Text = Loc.GetString("wf-hideout-ui-material", ("name", name), ("held", held), ("amount", amount)),
                FontColorOverride = held >= amount ? GoodColor : BadColor,
            });
        }

        foreach (var (part, amount) in next.Parts)
        {
            var held = view.Held.GetValueOrDefault("part:" + part.Id);
            ready &= held >= amount;
            var name = _proto.TryIndex(part, out var proto) ? proto.Name : part.Id;
            box.AddChild(new Label
            {
                Text = Loc.GetString("wf-hideout-ui-material", ("name", name), ("held", held), ("amount", amount)),
                FontColorOverride = held >= amount ? GoodColor : BadColor,
            });
        }

        foreach (var (needed, at) in next.Requires)
        {
            var met = view.Levels.GetValueOrDefault(needed) >= at;
            ready &= met;
            var name = _proto.TryIndex(needed, out var other) ? Loc.GetString(other.Name) : needed.Id;
            box.AddChild(new Label
            {
                Text = Loc.GetString("wf-hideout-ui-requires", ("module", name), ("level", at)),
                FontColorOverride = met ? GoodColor : BadColor,
            });
        }

        var id = module.ID;
        var build = Button(Loc.GetString("wf-hideout-ui-build", ("price", next.Price), ("time", Duration((int) next.BuildTime))),
            !isOwner || !ready || view.Building != null || next.Price > view.Balance, () => OnBuild?.Invoke(id));
        box.AddChild(Row(build));
        return box;
    }

    private static Button Button(string text, bool disabled, Action pressed)
    {
        var button = new Button { Text = text, Disabled = disabled };
        button.OnPressed += _ => pressed();
        return button;
    }

    private static PanelContainer Card(Control content)
    {
        var card = new PanelContainer { HorizontalExpand = true };
        card.AddStyleClass("AngleRect");
        content.Margin = new Thickness(10, 6);
        card.AddChild(content);
        return card;
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddStyleClass("LabelHeading");
        return label;
    }

    private static Label Secondary(string text)
    {
        var label = new Label { Text = text, ClipText = true };
        label.AddStyleClass("LabelSecondaryColor");
        return label;
    }

    private static BoxContainer Row(params Control[] children)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
        foreach (var child in children)
        {
            row.AddChild(child);
        }

        return row;
    }
}
