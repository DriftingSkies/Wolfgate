using System.Linq;
using System.Text;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._WF.Hideouts.Shipwright;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Hideouts.Shipwright;

/// <summary>
/// The shipwright console: a plan of the hull on the bay with its refit points; each hardpoint with a type picker and a
/// gun picker; new hardpoints placed by clicking a free edge tile on the plan; and a count for each system. The refit is
/// priced and pointed as it is picked. With the bay empty, it lists the frames the cranes build.
/// </summary>
public sealed class WFShipwrightConsoleWindow : DefaultWindow
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly Color GoodColor = Color.FromHex("#6FCF6F");
    private static readonly Color BadColor = Color.FromHex("#E05A4F");

    public event Action<string>? OnBuildFrame;
    public event Action? OnRegister;
    public event Action? OnCertify;
    public event Action<Dictionary<NetEntity, string>, Dictionary<string, int>, Dictionary<NetEntity, string>, List<WFShipwrightPlacement>>? OnRefit;

    /// <summary>The owner has the cranes bring a refit line up a tier.</summary>
    public event Action<string>? OnUpgrade;

    /// <summary>The owner opens the plan of the hull's interior.</summary>
    public event Action? OnInterior;

    private readonly Label _status = new() { HorizontalExpand = true, ClipText = true };
    private readonly Button _interior = new();
    private readonly BoxContainer _body = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };

    private List<WFShipwrightGunView> _guns = new();
    private List<WFShipwrightMountTypeView> _mountTypes = new();
    private WFShipwrightConsoleState? _state;
    private string? _built;

    /// <summary>The gun picked for each hardpoint, where it differs from what is fitted.</summary>
    private readonly Dictionary<NetEntity, string> _mounts = new();

    /// <summary>Hardpoints swapped for another type, or taken off with an empty id.</summary>
    private readonly Dictionary<NetEntity, string> _hardpoints = new();

    /// <summary>New hardpoints on free edge tiles: their type and gun.</summary>
    private readonly Dictionary<Vector2i, (string Hardpoint, string Gun)> _placements = new();

    /// <summary>The count picked for each system, where it differs from what is fitted.</summary>
    private readonly Dictionary<string, int> _systems = new();

    private Vector2i? _selected;
    private WFShipwrightHullMap? _map;
    private BoxContainer? _mountList;
    private Label? _points;
    private Button? _apply;

    public WFShipwrightConsoleWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("wf-shipwright-ui-title");
        MinSize = new System.Numerics.Vector2(720, 620);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        top.AddChild(_status);
        _interior.Text = Loc.GetString("wf-shipwright-ui-interior");
        _interior.ToolTip = Loc.GetString("wf-shipwright-ui-interior-tip");
        _interior.OnPressed += _ => OnInterior?.Invoke();
        top.AddChild(_interior);
        root.AddChild(top);
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_body);
        root.AddChild(scroll);
        Contents.AddChild(root);
    }

    public void SetCatalogue(List<WFShipwrightGunView> guns, List<WFShipwrightMountTypeView> mounts)
    {
        _guns = guns;
        _mountTypes = mounts;
        _built = null;
        if (_state != null)
            UpdateState(_state);
    }

    public void UpdateState(WFShipwrightConsoleState state)
    {
        _state = state;
        Title = Loc.GetString(state.Faction ? "wf-shipwright-ui-faction-title" : "wf-shipwright-ui-title");
        var status = new StringBuilder();
        status.Append(Loc.GetString("wf-shipwright-ui-balance", ("balance", state.Balance)));
        if (state.Working)
            status.Append("   ").Append(Loc.GetString("wf-shipwright-ui-working", ("done", state.Done), ("total", state.Total)));
        else if (state.Blocked != null)
            status.Append("   ").Append(Loc.GetString(state.Blocked));
        _status.Text = status.ToString();
        _interior.Disabled = state.Hull == null || state.Working;

        // Rebuilt only when the hull itself changes, so picks aren't lost to the once-a-second refresh.
        var key = Key(state);
        if (key != _built)
        {
            _built = key;
            _mounts.Clear();
            _hardpoints.Clear();
            _placements.Clear();
            _systems.Clear();
            _selected = null;
            Build(state);
        }

        UpdatePreview();
    }

    private static string Key(WFShipwrightConsoleState state)
    {
        var key = new StringBuilder();
        key.Append(state.Working).Append(state.Blocked).Append(state.CanBuildFrame).Append(state.IsOwner);
        if (state.Hull is { } hull)
        {
            key.Append(hull.Name).Append(hull.Registered).Append(hull.Yours).Append(hull.Budget).Append(hull.Spent)
                .Append(hull.UnfittedWeapons).Append(hull.UnfittedPoints).Append(hull.MaxMounts);
            foreach (var mount in hull.Mounts)
                key.Append(mount.Hardpoint).Append(mount.Proto).Append(mount.Gun).Append(mount.Fitted).Append(mount.MountFitted);
            foreach (var (id, count) in hull.Systems.OrderBy(p => p.Key))
                key.Append(id).Append(count);
            foreach (var (id, count) in hull.Unfitted.OrderBy(p => p.Key))
                key.Append(id).Append(count);
            foreach (var upgrade in state.Upgrades)
                key.Append(upgrade.Id).Append(upgrade.Tier).Append(upgrade.NextTier).Append(upgrade.NextPrice);
        }

        return key.ToString();
    }

    private void Build(WFShipwrightConsoleState state)
    {
        _body.RemoveAllChildren();
        _points = null;
        _apply = null;
        _map = null;
        _mountList = null;
        if (state.Faction)
            _body.AddChild(Secondary(Loc.GetString("wf-shipwright-ui-faction-help")));

        if (state.Hull is not { } hull)
        {
            if (state.Faction)
                _body.AddChild(Secondary(Loc.GetString("wf-shipwright-ui-faction-empty")));
            else
                BuildFrames(state);
            return;
        }

        _body.AddChild(Heading(hull.Name));
        _body.AddChild(Secondary(hull.Frame != null && _proto.TryIndex<WFShipwrightFramePrototype>(hull.Frame, out var frame)
            ? Loc.GetString("wf-shipwright-ui-hull-frame", ("frame", Loc.GetString(frame.Name)), ("tiles", hull.Tiles))
            : Loc.GetString("wf-shipwright-ui-hull-custom", ("tiles", hull.Tiles))));

        _body.AddChild(new Label
        {
            Text = Loc.GetString(state.Faction ? "wf-shipwright-ui-firepower" : "wf-shipwright-ui-points", ("spent", hull.Spent), ("budget", hull.Budget)),
            FontColorOverride = hull.Spent <= hull.Budget ? null : BadColor,
        });
        _body.AddChild(new ProgressBar
        {
            MinValue = 0, MaxValue = 1, MinHeight = 10, HorizontalExpand = true,
            Value = hull.Budget <= 0 ? 1 : Math.Clamp(hull.Spent / (float) hull.Budget, 0f, 1f),
        });

        var systemsUnfitted = hull.Unfitted.Values.Sum();
        _body.AddChild(new Label
        {
            Text = hull.Certified
                ? Loc.GetString("wf-shipwright-ui-certified")
                : Loc.GetString("wf-shipwright-ui-uncertified", ("weapons", hull.UnfittedWeapons), ("systems", systemsUnfitted), ("points", hull.UnfittedPoints)),
            FontColorOverride = hull.Certified ? GoodColor : BadColor,
        });

        var locked = state.Working || state.Blocked != null;
        if (!hull.Registered && state.Faction)
        {
            _body.AddChild(Secondary(Loc.GetString("wf-shipwright-faction-not-registered")));
            return;
        }

        if (!hull.Registered)
        {
            _body.AddChild(Button(Loc.GetString("wf-shipwright-ui-register"), locked, () => OnRegister?.Invoke()));
            return;
        }

        if (!hull.Yours)
        {
            _body.AddChild(Secondary(Loc.GetString("wf-shipwright-ui-not-yours")));
            return;
        }

        if (hull.UnfittedWeapons + systemsUnfitted > 0)
        {
            var price = (hull.UnfittedWeapons + systemsUnfitted) * CertifyPrice();
            _body.AddChild(Button(Loc.GetString("wf-shipwright-ui-certify", ("price", price)), locked || price > state.Balance,
                () => OnCertify?.Invoke()));
        }

        // The plan beside the hardpoint list; clicking a free edge tile places a new hardpoint there.
        _body.AddChild(Heading(Loc.GetString("wf-shipwright-ui-mounts")));
        _body.AddChild(Secondary(Loc.GetString("wf-shipwright-ui-plan-help")));
        var split = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        _map = new WFShipwrightHullMap { MinSize = new System.Numerics.Vector2(260, 260) };
        _map.Set(hull.Grid, hull.Mounts.Select(m => m.Tile), hull.MountTiles);
        _map.OnTileClicked += tile => OnTile(hull, tile, locked);
        split.AddChild(_map);
        _mountList = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, HorizontalExpand = true };
        split.AddChild(_mountList);
        _body.AddChild(split);
        BuildMounts(hull, locked);

        BuildSystems(hull, locked);
        BuildUpgrades(state, locked);

        _points = new Label();
        _body.AddChild(_points);
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        _apply = Button(string.Empty, true, () => OnRefit?.Invoke(
            new Dictionary<NetEntity, string>(_mounts),
            new Dictionary<string, int>(_systems),
            new Dictionary<NetEntity, string>(_hardpoints),
            _placements.Select(p => new WFShipwrightPlacement(p.Key, p.Value.Hardpoint, p.Value.Gun)).ToList()));
        buttons.AddChild(_apply);
        buttons.AddChild(Button(Loc.GetString("wf-shipwright-ui-reset"), false, () =>
        {
            _built = null;
            if (_state != null)
                UpdateState(_state);
        }));
        _body.AddChild(buttons);
    }

    /// <summary>A click on the plan: a free edge tile gets a new hardpoint, or loses the one just placed there.</summary>
    private void OnTile(WFShipwrightHullView hull, Vector2i tile, bool locked)
    {
        _selected = tile;
        if (!locked && _state is { Faction: false } && hull.MountTiles.Contains(tile))
        {
            if (_placements.ContainsKey(tile))
                _placements.Remove(tile);
            else if (_mountTypes.Count > 0)
                _placements[tile] = (_mountTypes[0].Id, string.Empty);
        }

        BuildMounts(hull, locked);
        UpdatePreview();
    }

    private void BuildFrames(WFShipwrightConsoleState state)
    {
        _body.AddChild(Secondary(Loc.GetString("wf-shipwright-ui-empty")));
        _body.AddChild(Heading(Loc.GetString("wf-shipwright-ui-frames")));
        if (!state.CanBuildFrame)
            _body.AddChild(Secondary(Loc.GetString("wf-shipwright-ui-frame-slot-taken")));

        foreach (var frame in _proto.EnumeratePrototypes<WFShipwrightFramePrototype>().OrderBy(f => f.Order).ThenBy(f => f.ID))
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            row.AddChild(new Label { Text = Loc.GetString(frame.Name) });
            row.AddChild(Secondary(Loc.GetString(frame.Description)));
            var id = frame.ID;
            row.AddChild(Button(Loc.GetString("wf-shipwright-ui-frame-build", ("price", frame.Price)),
                !state.CanBuildFrame || state.Blocked != null || frame.Price > state.Balance, () => OnBuildFrame?.Invoke(id)));
            _body.AddChild(row);
        }
    }

    /// <summary>The hardpoint list: each mount with its type and gun pickers, then the new ones placed on the plan.</summary>
    private void BuildMounts(WFShipwrightHullView hull, bool locked)
    {
        if (_mountList == null)
            return;

        _mountList.RemoveAllChildren();
        _map?.SetPicks(_placements.Keys, hull.Mounts.Where(m => _hardpoints.TryGetValue(m.Hardpoint, out var id) && id.Length == 0).Select(m => m.Tile), _selected);
        if (hull.Mounts.Count == 0 && _placements.Count == 0)
            _mountList.AddChild(Secondary(Loc.GetString("wf-shipwright-ui-no-mounts")));

        foreach (var mount in hull.Mounts)
        {
            var net = mount.Hardpoint;
            var type = MountType(net, mount);
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            if (_selected == mount.Tile)
                row.Modulate = Color.FromHex("#FFFFE0");

            if (_state is { Faction: true })
            {
                row.AddChild(new Label { Text = Loc.GetString("wf-shipwright-ui-mount-at", ("x", mount.Tile.X), ("y", mount.Tile.Y)) + "  " + MountName(mount.Proto) });
                row.AddChild(GunPicker(type!.Value.Size, type.Value.Types, _mounts.GetValueOrDefault(net, mount.Gun ?? string.Empty),
                    mount.Gun ?? string.Empty, null, locked, gun =>
                    {
                        if (gun == (mount.Gun ?? string.Empty))
                            _mounts.Remove(net);
                        else
                            _mounts[net] = gun;
                    }));
                _mountList.AddChild(row);
                continue;
            }

            // The hardpoint's own type: kept, swapped, or taken off.
            var typePicker = new OptionButton { HorizontalExpand = true, Disabled = locked };
            var typeIds = new List<string> { KeepMark };
            typePicker.AddItem(Loc.GetString(mount.Stock ? "wf-shipwright-ui-mount-keep-stock" : "wf-shipwright-ui-mount-keep",
                ("mount", MountName(mount.Proto))), 0);
            foreach (var other in _mountTypes.Where(t => t.Id != mount.Proto))
            {
                typePicker.AddItem(Loc.GetString("wf-shipwright-ui-mount-type", ("name", other.Name), ("points", other.Points), ("price", other.Price)), typeIds.Count);
                typeIds.Add(other.Id);
            }

            typePicker.AddItem(Loc.GetString("wf-shipwright-ui-mount-remove"), typeIds.Count);
            typeIds.Add(string.Empty);
            var typeIndex = _hardpoints.TryGetValue(net, out var picked) ? typeIds.IndexOf(picked) : 0;
            typePicker.SelectId(Math.Max(0, typeIndex));
            typePicker.OnItemSelected += args =>
            {
                var id = typeIds[args.Id];
                if (id == KeepMark)
                    _hardpoints.Remove(net);
                else
                    _hardpoints[net] = id;
                _mounts.Remove(net);
                BuildMounts(hull, locked);
                UpdatePreview();
            };
            row.AddChild(Row(new Label { Text = Loc.GetString("wf-shipwright-ui-mount-at", ("x", mount.Tile.X), ("y", mount.Tile.Y)), MinWidth = 70 }, typePicker));

            if (type is { } fits)
            {
                // A swapped hardpoint starts empty; a kept one shows its gun.
                var swapped = _hardpoints.ContainsKey(net);
                var fitted = swapped ? string.Empty : mount.Gun == null ? string.Empty : mount.Fitted ? mount.Gun : UnfittedMark;
                row.AddChild(GunPicker(fits.Size, fits.Types, _mounts.GetValueOrDefault(net, fitted), fitted,
                    swapped ? null : mount.Gun, locked, gun =>
                    {
                        if (gun == fitted)
                            _mounts.Remove(net);
                        else
                            _mounts[net] = gun == UnfittedMark ? string.Empty : gun;
                    }));
            }

            _mountList.AddChild(row);
        }

        foreach (var (tile, placement) in _placements.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X))
        {
            var at = tile;
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            var typePicker = new OptionButton { HorizontalExpand = true, Disabled = locked };
            for (var i = 0; i < _mountTypes.Count; i++)
            {
                var type = _mountTypes[i];
                typePicker.AddItem(Loc.GetString("wf-shipwright-ui-mount-type", ("name", type.Name), ("points", type.Points), ("price", type.Price)), i);
            }

            typePicker.SelectId(Math.Max(0, _mountTypes.FindIndex(t => t.Id == placement.Hardpoint)));
            typePicker.OnItemSelected += args =>
            {
                _placements[at] = (_mountTypes[args.Id].Id, string.Empty);
                BuildMounts(hull, locked);
                UpdatePreview();
            };
            row.AddChild(Row(new Label { Text = Loc.GetString("wf-shipwright-ui-mount-new", ("x", tile.X), ("y", tile.Y)), MinWidth = 70, FontColorOverride = GoodColor },
                typePicker, Button("x", locked, () =>
                {
                    _placements.Remove(at);
                    BuildMounts(hull, locked);
                    UpdatePreview();
                })));

            if (_mountTypes.FirstOrDefault(t => t.Id == placement.Hardpoint) is { } fits)
            {
                row.AddChild(GunPicker(fits.Size, fits.Types, placement.Gun, string.Empty, null, locked,
                    gun => _placements[at] = (placement.Hardpoint, gun)));
            }

            _mountList.AddChild(row);
        }
    }

    /// <summary>The size and types a hardpoint takes once the picked swap is made, or null when it comes off.</summary>
    private (int Size, int Types)? MountType(NetEntity net, WFShipwrightMountView mount)
    {
        if (_hardpoints.TryGetValue(net, out var id))
        {
            if (id.Length == 0)
                return null;
            var type = _mountTypes.FirstOrDefault(t => t.Id == id);
            return type == null ? null : (type.Size, type.Types);
        }

        return (Enum.TryParse<weaponSizes>(mount.Size, out var size) ? (int) size : 0, mount.Types);
    }

    private OptionButton GunPicker(int size, int types, string current, string fitted, string? unfittedGun, bool locked, Action<string> picked)
    {
        var picker = new OptionButton { HorizontalExpand = true, Disabled = locked };
        var ids = new List<string> { string.Empty };
        picker.AddItem(Loc.GetString("wf-shipwright-ui-mount-empty"), 0);
        foreach (var gun in _guns.Where(g => g.Size <= size && (g.Types & types) != 0))
        {
            picker.AddItem(Loc.GetString("wf-shipwright-ui-gun", ("name", gun.Name), ("points", gun.Points), ("price", gun.Price)), ids.Count);
            ids.Add(gun.Id);
        }

        // A hand-built gun shows as not fitted and stays on unless the owner picks something else.
        if (fitted == UnfittedMark && unfittedGun != null)
        {
            var name = _proto.TryIndex<EntityPrototype>(unfittedGun, out var proto) ? proto.Name : unfittedGun;
            picker.AddItem(Loc.GetString("wf-shipwright-ui-mount-unfitted", ("gun", name)), ids.Count);
            ids.Add(UnfittedMark);
        }

        picker.SelectId(Math.Max(0, ids.IndexOf(current)));
        picker.OnItemSelected += args =>
        {
            picker.SelectId(args.Id);
            picked(ids[args.Id]);
            UpdatePreview();
        };
        return picker;
    }

    /// <summary>Stands for a hand-built gun kept where it is.</summary>
    private const string UnfittedMark = "\u0000unfitted";

    /// <summary>Stands for a hardpoint kept as it is.</summary>
    private const string KeepMark = "\u0000keep";

    private string MountName(string proto)
    {
        return _mountTypes.FirstOrDefault(t => t.Id == proto)?.Name
               ?? (_proto.TryIndex<EntityPrototype>(proto, out var entity) ? entity.Name : proto);
    }

    private void BuildSystems(WFShipwrightHullView hull, bool locked)
    {
        var systems = _proto.EnumeratePrototypes<WFShipwrightSystemPrototype>().Where(s => !IsMounted(s))
            .Where(s => _state is not { Faction: true } || hull.Systems.ContainsKey(s.ID))
            .OrderBy(s => s.Order).ThenBy(s => s.ID).ToList();
        if (systems.Count == 0)
            return;

        _body.AddChild(Heading(Loc.GetString("wf-shipwright-ui-systems")));
        WFShipwrightCategory? category = null;
        foreach (var system in systems)
        {
            if (system.Category != category)
            {
                category = system.Category;
                _body.AddChild(Secondary(Loc.GetString($"wf-shipwright-category-{category.ToString()!.ToLowerInvariant()}")));
            }

            var fitted = hull.Systems.GetValueOrDefault(system.ID);
            var count = new Label { Text = fitted.ToString(), MinWidth = 24, Align = Label.AlignMode.Center };
            var id = system.ID;
            var minus = Button("-", locked, () => Change(id, fitted, -1, count, system.Limit));
            var plus = Button("+", locked, () => Change(id, fitted, 1, count, system.Limit));
            var name = new Label
            {
                Text = Loc.GetString("wf-shipwright-ui-system", ("name", Loc.GetString(system.Name)), ("points", system.Points), ("price", system.Price)),
                HorizontalExpand = true,
                ClipText = true,
                ToolTip = Loc.GetString(system.Description),
            };
            var row = Row(name, minus, count, plus);
            if (hull.Unfitted.GetValueOrDefault(system.ID) is var loose and > 0)
                row.AddChild(new Label { Text = Loc.GetString("wf-shipwright-ui-system-unfitted", ("count", loose)), FontColorOverride = BadColor });
            _body.AddChild(row);
        }
    }

    /// <summary>The drydock refit lines on the hull: armour plating and machine part tiers.</summary>
    private void BuildUpgrades(WFShipwrightConsoleState state, bool locked)
    {
        if (state.Upgrades.Count == 0)
            return;

        _body.AddChild(Heading(Loc.GetString("wf-shipwright-ui-upgrades")));
        _body.AddChild(Secondary(Loc.GetString("wf-shipwright-ui-upgrades-help")));
        foreach (var upgrade in state.Upgrades)
        {
            var id = upgrade.Id;
            var name = new Label
            {
                Text = Loc.GetString("wf-shipwright-ui-upgrade", ("name", upgrade.Name), ("tier", upgrade.Tier), ("max", upgrade.MaxTier)),
                HorizontalExpand = true,
            };
            Control action = upgrade.NextTier > 0
                ? Button(Loc.GetString("wf-shipwright-ui-upgrade-buy", ("next", upgrade.NextTier), ("price", upgrade.NextPrice)),
                    locked || upgrade.NextPrice > state.Balance, () => OnUpgrade?.Invoke(id))
                : new Label { Text = Loc.GetString("wf-shipwright-ui-upgrade-done"), FontColorOverride = GoodColor };
            _body.AddChild(Row(name, action));
        }
    }

    private void Change(string id, int fitted, int step, Label count, int limit)
    {
        var next = Math.Max(0, _systems.GetValueOrDefault(id, fitted) + step);
        if (limit > 0)
            next = Math.Min(next, limit);
        if (next == fitted)
            _systems.Remove(id);
        else
            _systems[id] = next;
        count.Text = next.ToString();
        UpdatePreview();
    }

    /// <summary>Prices and points the picked refit as the server will, and enables Apply when it fits.</summary>
    private void UpdatePreview()
    {
        if (_points == null || _apply == null || _state?.Hull is not { } hull)
            return;

        var guns = _guns.ToDictionary(g => g.Id);
        var types = _mountTypes.ToDictionary(t => t.Id);
        var spent = hull.Spent;
        var price = 0;
        var mountCount = hull.Mounts.Count;
        foreach (var mount in hull.Mounts)
        {
            var swapping = _hardpoints.TryGetValue(mount.Hardpoint, out var swap);
            var gunPick = _mounts.TryGetValue(mount.Hardpoint, out var picked);
            if (swapping)
            {
                // The old hardpoint comes off with its gun.
                if (mount.Gun != null)
                {
                    if (mount.Fitted && guns.TryGetValue(mount.Gun, out var oldGun))
                        spent -= oldGun.Points;
                    price += Labour();
                }

                if (mount.MountFitted && !mount.Stock && types.TryGetValue(mount.Proto, out var oldType))
                    spent -= oldType.Points;
                price += Labour();
                mountCount--;
                if (swap!.Length > 0 && types.TryGetValue(swap, out var newType))
                {
                    mountCount++;
                    spent += newType.Points;
                    price += newType.Price;
                }
            }
            else if (gunPick && mount.Gun != null)
            {
                if (mount.Fitted && guns.TryGetValue(mount.Gun, out var oldGun))
                    spent -= oldGun.Points;
                price += Labour();
            }

            if (gunPick && picked!.Length > 0 && guns.TryGetValue(picked, out var gun) && (!swapping || swap!.Length > 0))
            {
                spent += gun.Points;
                price += gun.Price;
            }
        }

        foreach (var (_, placement) in _placements)
        {
            if (!types.TryGetValue(placement.Hardpoint, out var type))
                continue;

            mountCount++;
            spent += type.Points;
            price += type.Price;
            if (placement.Gun.Length > 0 && guns.TryGetValue(placement.Gun, out var gun))
            {
                spent += gun.Points;
                price += gun.Price;
            }
        }

        foreach (var (id, count) in _systems)
        {
            if (!_proto.TryIndex<WFShipwrightSystemPrototype>(id, out var system))
                continue;

            var delta = count - hull.Systems.GetValueOrDefault(id);
            spent += delta * system.Points;
            price += delta > 0 ? delta * system.Price : -delta * Labour();
        }

        var changed = _mounts.Count + _hardpoints.Count + _placements.Count + _systems.Count > 0;
        var fits = spent <= hull.Budget && mountCount <= hull.MaxMounts;
        _points.Text = Loc.GetString(_state.Faction ? "wf-shipwright-ui-firepower" : "wf-shipwright-ui-points-after", ("spent", spent), ("budget", hull.Budget))
                       + "   " + Loc.GetString("wf-shipwright-ui-mount-count", ("count", mountCount), ("max", hull.MaxMounts));
        _points.FontColorOverride = fits ? GoodColor : BadColor;
        _apply.Text = Loc.GetString("wf-shipwright-ui-apply", ("price", price));
        _apply.Disabled = !changed || !fits || price > _state.Balance || _state.Working || _state.Blocked != null;
    }

    private WFShipwrightRulesPrototype? Rules =>
        _proto.TryIndex<WFShipwrightRulesPrototype>(WFShipwrightRulesPrototype.Default, out var rules) ? rules : null;

    private int Labour() => Rules?.Labour ?? 0;

    private int CertifyPrice() => Rules?.CertifyPrice ?? 0;

    private bool IsMounted(WFShipwrightSystemPrototype system)
    {
        return _proto.TryIndex(system.Install, out var proto)
               && proto.Components.ContainsKey(_factory.GetComponentName<HardpointAnchorableOnlyComponent>());
    }

    private static Button Button(string text, bool disabled, Action pressed)
    {
        var button = new Button { Text = text, Disabled = disabled };
        button.OnPressed += _ => pressed();
        return button;
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddStyleClass("LabelHeading");
        return label;
    }

    private static Label Secondary(string text)
    {
        var label = new Label { Text = text };
        label.AddStyleClass("LabelSecondaryColor");
        return label;
    }

    private static BoxContainer Row(params Control[] children)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        foreach (var child in children)
            row.AddChild(child);
        return row;
    }
}
