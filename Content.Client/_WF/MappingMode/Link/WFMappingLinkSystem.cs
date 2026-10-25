using System.Linq;
using System.Numerics;
using Content.Client._WF.MappingMode.Modes;
using Content.Client._WF.MappingTools;
using Content.Client.Popups;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork.Components;
using Robust.Client.Graphics;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.MappingMode.Link;

/// <summary>
/// Link mode's state and requests: the sub-tool, the selected signal source or device list, the signal link
/// snapshot of the viewed level, the ports and access windows and the Links layer.
/// </summary>
public sealed partial class WFMappingLinkSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MappingToolsSystem _tools = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFMappingBuildModeSystem _modes = default!;

    /// <summary>How far from a click an entity may be and still be picked, in tiles.</summary>
    private const float PickRange = 0.45f;

    /// <summary>Most devices one box sends; the server holds the same cap.</summary>
    private const int MaxBoxDevices = 256;

    private static readonly TimeSpan SnapshotMinInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan SnapshotRefresh = TimeSpan.FromSeconds(3);

    private readonly List<MappingSignalLinkState> _snapshot = new();
    private EntityUid _snapshotMap;
    private EntityUid _requestedMap;
    private TimeSpan _lastRequest;
    private WFMappingLinkPortsWindow? _portsWindow;
    private WFMappingAccessWindow? _accessWindow;

    public WFMappingLinkTool Tool { get; private set; } = WFMappingLinkTool.Signals;

    /// <summary>The selected signal source, in Signals.</summary>
    public EntityUid? Source { get; private set; }

    /// <summary>The selected device list holder, in Devices.</summary>
    public EntityUid? Holder { get; private set; }

    /// <summary>Whether Link mode is the current build mode.</summary>
    public bool ModeActive { get; private set; }

    /// <summary>Whether the Links layer draws every link on the viewed level.</summary>
    public bool ShowAll { get; private set; }

    /// <summary>The access levels last applied, for Copy from last.</summary>
    public List<string>? LastAccess { get; private set; }

    /// <summary>The level the snapshot belongs to.</summary>
    public EntityUid SnapshotMap => _snapshotMap;

    /// <summary>Every signal link whose source is on <see cref="SnapshotMap"/>.</summary>
    public IReadOnlyList<MappingSignalLinkState> Snapshot => _snapshot;

    /// <summary>Raised when the tool or the selection changes.</summary>
    public event Action? Changed;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MappingToolsLinksSnapshotEvent>(OnSnapshot);
        SubscribeNetworkEvent<MappingToolsLinkPortsEvent>(OnPorts);

        CommandBinds.Builder
            .Bind(WFMappingKeyFunctions.WFMappingLinkCycle,
                new PointerInputCmdHandler((ICommonSession? _, EntityCoordinates _, EntityUid _) => CycleKey(),
                    outsidePrediction: true))
            .Register<WFMappingLinkSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<WFMappingLinkSystem>();
        CloseWindows();
        _overlays.RemoveOverlay<WFMappingLinkOverlay>();
    }

    /// <summary>Link mode became current or stopped being so; leaving drops the selection and the windows.</summary>
    public void SetModeActive(bool active)
    {
        if (ModeActive == active)
            return;

        ModeActive = active;
        if (!active)
        {
            Source = null;
            Holder = null;
            CloseWindows();
        }

        UpdateOverlay();
        Changed?.Invoke();
    }

    /// <summary>Shows or hides every link on the viewed level.</summary>
    public void SetShowAll(bool shown)
    {
        if (ShowAll == shown)
            return;

        ShowAll = shown;
        UpdateOverlay();
        if (shown)
            RequestSnapshot(force: true);

        SnapshotChanged?.Invoke();
    }

    public void SetTool(WFMappingLinkTool tool)
    {
        if (Tool == tool)
            return;

        Tool = tool;
        Source = null;
        Holder = null;
        _portsWindow?.Close();
        if (tool == WFMappingLinkTool.Signals)
            RequestSnapshot(force: true);

        Changed?.Invoke();
    }

    /// <summary>The next sub-tool, wrapping round.</summary>
    public void CycleTool()
    {
        SetTool((WFMappingLinkTool) (((int) Tool + 1) % Enum.GetValues<WFMappingLinkTool>().Length));
    }

    private bool CycleKey()
    {
        if (!ModeActive)
            return false;

        CycleTool();
        return true;
    }

    /// <summary>Drops the selected source or holder; false when there was none.</summary>
    public bool ClearSelection()
    {
        if (Source == null && Holder == null)
            return false;

        Source = null;
        Holder = null;
        _portsWindow?.Close();
        Changed?.Invoke();
        return true;
    }

    /// <summary>A left click in Link mode.</summary>
    public void Click(EntityCoordinates coords, EntityUid clicked)
    {
        switch (Tool)
        {
            case WFMappingLinkTool.Signals:
                ClickSignal(coords, clicked);
                break;
            case WFMappingLinkTool.Devices:
                ClickDevice(coords, clicked);
                break;
            case WFMappingLinkTool.Access:
                if (FindAt(coords, clicked, HasComp<AccessReaderComponent>) is { } reader)
                    OpenAccess(reader);
                else
                    Popup("wf-mapping-link-not-reader");
                break;
        }
    }

    /// <summary>A right click: unlinks the clicked sink from the source, or takes the clicked device out.</summary>
    public void RightClick(EntityCoordinates coords, EntityUid clicked)
    {
        if (Tool == WFMappingLinkTool.Signals && Source is { } source &&
            FindAt(coords, clicked, uid => uid != source && HasComp<DeviceLinkSinkComponent>(uid)) is { } sink)
        {
            Send(new MappingToolsLinkEvent(GetNetEntity(source), GetNetEntity(sink),
                new List<(string Source, string Sink)>()));
            return;
        }

        if (Tool == WFMappingLinkTool.Devices && Holder is { } holder && TryComp(holder, out DeviceListComponent? list) &&
            list.Devices is var devices && FindAt(coords, clicked, devices.Contains) is { } device)
        {
            Send(new MappingToolsDeviceListEvent(GetNetEntity(holder), new List<NetEntity> { GetNetEntity(device) },
                MappingDeviceListOp.Remove));
        }
    }

    private void ClickSignal(EntityCoordinates coords, EntityUid clicked)
    {
        if (Source is { } source)
        {
            if (FindAt(coords, clicked, uid => uid != source && HasComp<DeviceLinkSinkComponent>(uid)) is { } sink)
            {
                Send(new MappingToolsLinkPortsRequestEvent(GetNetEntity(source), GetNetEntity(sink)));
                return;
            }

            if (FindAt(coords, clicked, HasComp<DeviceLinkSourceComponent>) is { } other)
            {
                SetSource(other == source ? null : other);
                return;
            }

            Popup("wf-mapping-link-not-sink");
            return;
        }

        if (FindAt(coords, clicked, HasComp<DeviceLinkSourceComponent>) is { } found)
            SetSource(found);
        else
            Popup("wf-mapping-link-not-source");
    }

    private void SetSource(EntityUid? source)
    {
        Source = source;
        _portsWindow?.Close();
        if (source != null)
            RequestSnapshot(force: true);

        Changed?.Invoke();
    }

    private void ClickDevice(EntityCoordinates coords, EntityUid clicked)
    {
        if (Holder is { } holder && TryComp(holder, out DeviceListComponent? list))
        {
            if (clicked == holder)
            {
                ClearSelection();
                return;
            }

            if (FindAt(coords, clicked, uid => uid != holder && IsDevice(uid)) is not { } device)
            {
                Popup("wf-mapping-link-not-device");
                return;
            }

            var devices = list.Devices;
            var op = devices.Contains(device) ? MappingDeviceListOp.Remove : MappingDeviceListOp.Add;
            Send(new MappingToolsDeviceListEvent(GetNetEntity(holder), new List<NetEntity> { GetNetEntity(device) }, op));
            return;
        }

        if (FindAt(coords, clicked, HasComp<DeviceListComponent>) is { } found)
        {
            Holder = found;
            Changed?.Invoke();
        }
        else
        {
            Popup("wf-mapping-link-not-holder");
        }
    }

    /// <summary>Adds every device in a box of the map to the selected holder's list.</summary>
    public void AddBox(MapCoordinates a, MapCoordinates b)
    {
        if (Holder is not { } holder || a.MapId != b.MapId || a.MapId == MapId.Nullspace)
            return;

        var box = new Box2(Vector2.Min(a.Position, b.Position), Vector2.Max(a.Position, b.Position));
        var devices = new List<NetEntity>();
        foreach (var uid in _lookup.GetEntitiesIntersecting(a.MapId, box))
        {
            if (uid == holder || !IsDevice(uid) || devices.Count >= MaxBoxDevices)
                continue;

            devices.Add(GetNetEntity(uid));
        }

        if (devices.Count == 0)
        {
            Popup("wf-mapping-link-box-empty");
            return;
        }

        Send(new MappingToolsDeviceListEvent(GetNetEntity(holder), devices, MappingDeviceListOp.Add));
    }

    /// <summary>Whether an entity can go in a device list: a network device with a savable address.</summary>
    public bool IsDevice(EntityUid uid)
    {
        return TryComp(uid, out DeviceNetworkComponent? device) && device.SavableAddress;
    }

    /// <summary>
    /// The clicked entity if it matches, else the matching entity nearest the click; null when none is in reach.
    /// </summary>
    public EntityUid? FindAt(EntityCoordinates coords, EntityUid clicked, Func<EntityUid, bool> match)
    {
        if (clicked.IsValid() && Exists(clicked) && match(clicked))
            return clicked;

        if (!coords.IsValid(EntityManager))
            return null;

        var position = _transform.ToMapCoordinates(coords);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var uid in _lookup.GetEntitiesInRange(position, PickRange))
        {
            if (!match(uid))
                continue;

            var distance = (_transform.GetWorldPosition(uid) - position.Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>The levels a target's reader accepts (any one); before map init, those of the board it will get.</summary>
    public HashSet<string> CurrentAccess(EntityUid target)
    {
        var levels = new HashSet<string>();
        if (!_accessReader.GetMainAccessReader(target, out var main))
            return levels;

        var reader = main.Value;
        if (reader.Owner == target &&
            reader.Comp.ContainerAccessProvider is { } container &&
            !HasComp<WFMappingAccessFillComponent>(target) &&
            TryComp(target, out ContainerFillComponent? fill) &&
            fill.Containers is var filled && filled.TryGetValue(container, out var fills))
        {
            foreach (var id in fills)
            {
                if (!_prototypes.TryIndex<EntityPrototype>(id, out var prototype) ||
                    !prototype.TryGetComponent(out AccessReaderComponent? board, _factory))
                {
                    continue;
                }

                foreach (var set in board.AccessLists)
                {
                    levels.UnionWith(set.Select(l => l.Id));
                }

                return levels;
            }
        }

        foreach (var set in reader.Comp.AccessLists)
        {
            levels.UnionWith(set.Select(l => l.Id));
        }

        return levels;
    }

    private void OpenAccess(EntityUid target)
    {
        if (_accessWindow == null)
        {
            _accessWindow = new WFMappingAccessWindow(_prototypes);
            _accessWindow.Applied += levels => ApplyAccess(levels, false);
            _accessWindow.AppliedToSelection += levels => ApplyAccess(levels, true);
            _accessWindow.OnClose += () => _accessWindow = null;
        }

        _accessWindow.SetTarget(target, MetaData(target).EntityName, CurrentAccess(target), LastAccess);
        if (!_accessWindow.IsOpen)
            _accessWindow.OpenCentered();
    }

    private void ApplyAccess(List<string> levels, bool selection)
    {
        var targets = new List<NetEntity>();
        if (selection)
        {
            foreach (var uid in _tools.Selection.SelectedEntities())
            {
                if (HasComp<AccessReaderComponent>(uid))
                    targets.Add(GetNetEntity(uid));
            }

            if (targets.Count == 0)
            {
                Popup("wf-mapping-link-access-no-selection");
                return;
            }
        }
        else if (_accessWindow?.Target is { } target && Exists(target))
        {
            targets.Add(GetNetEntity(target));
        }
        else
        {
            return;
        }

        LastAccess = new List<string>(levels);
        Send(new MappingToolsAccessEvent(targets, new List<string>(levels)));
        _accessWindow?.SetLast(LastAccess);
    }

    private void OnPorts(MappingToolsLinkPortsEvent ev)
    {
        if (!ModeActive || Tool != WFMappingLinkTool.Signals || Source is not { } source ||
            GetNetEntity(source) != ev.Source || !TryGetEntity(ev.Sink, out var sink))
        {
            return;
        }

        if (_portsWindow == null)
        {
            _portsWindow = new WFMappingLinkPortsWindow(_prototypes);
            _portsWindow.Applied += (from, to, links) => Send(new MappingToolsLinkEvent(from, to, links));
            _portsWindow.OnClose += () => _portsWindow = null;
        }

        _portsWindow.SetPorts(ev, MetaData(source).EntityName, MetaData(sink.Value).EntityName);
        if (!_portsWindow.IsOpen)
            _portsWindow.OpenCentered();
    }

    private void OnSnapshot(MappingToolsLinksSnapshotEvent ev)
    {
        if (!TryGetEntity(ev.Map, out var map))
            return;

        _snapshotMap = map.Value;
        _snapshot.Clear();
        _snapshot.AddRange(ev.Links);
        SnapshotChanged?.Invoke();
    }

    /// <summary>Raised when a links snapshot arrives or the Links layer is switched.</summary>
    public event Action? SnapshotChanged;

    /// <summary>
    /// What the Links layer has to draw on the viewed level: the snapshot's signal links, when it is for this level,
    /// and the device lists the client knows there.
    /// </summary>
    public (int Links, int Lists) CountOnViewedLevel()
    {
        var mapId = _eye.CurrentEye.Position.MapId;
        var map = _map.GetMapOrInvalid(mapId);
        var links = map.IsValid() && map == _snapshotMap ? _snapshot.Count : 0;
        var lists = 0;
        var query = AllEntityQuery<DeviceListComponent, TransformComponent>();
        while (query.MoveNext(out _, out var list, out var xform))
        {
            if (xform.MapID == mapId && list.Devices.Count > 0)
                lists++;
        }

        return (links, lists);
    }

    /// <summary>The sinks a source links to in the snapshot.</summary>
    public IEnumerable<EntityUid> SinksOf(EntityUid source)
    {
        var net = GetNetEntity(source);
        foreach (var link in _snapshot)
        {
            if (link.Source == net && TryGetEntity(link.Sink, out var sink))
                yield return sink.Value;
        }
    }

    private bool WantsSnapshot => _modes.Active && (ShowAll || ModeActive && Tool == WFMappingLinkTool.Signals);

    private void RequestSnapshot(bool force = false)
    {
        if (!WantsSnapshot)
            return;

        var map = _map.GetMapOrInvalid(_eye.CurrentEye.Position.MapId);
        if (!map.IsValid())
            return;

        var now = _timing.RealTime;
        if (now - _lastRequest < SnapshotMinInterval ||
            !force && map == _requestedMap && now - _lastRequest < SnapshotRefresh)
        {
            return;
        }

        _lastRequest = now;
        _requestedMap = map;
        Send(new MappingToolsLinksRequestEvent(GetNetEntity(map)));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (Source is { } source && !Exists(source) || Holder is { } holder && !Exists(holder))
            ClearSelection();

        if (!WantsSnapshot)
            return;

        var map = _map.GetMapOrInvalid(_eye.CurrentEye.Position.MapId);
        RequestSnapshot(force: map != _requestedMap);
    }

    /// <summary>The cursor readout's line for Link mode.</summary>
    public string ReadoutText()
    {
        return Tool switch
        {
            WFMappingLinkTool.Signals when Source is { } source => Loc.GetString("wf-mapping-link-readout-source",
                ("name", MetaData(source).EntityName),
                ("count", SinksOf(source).Count())),
            WFMappingLinkTool.Signals => Loc.GetString("wf-mapping-link-readout-signals"),
            WFMappingLinkTool.Devices when Holder is { } holder && TryComp(holder, out DeviceListComponent? list) =>
                Loc.GetString("wf-mapping-link-readout-holder",
                    ("name", MetaData(holder).EntityName),
                    ("count", list.Devices.Count),
                    ("max", list.DeviceLimit)),
            WFMappingLinkTool.Devices => Loc.GetString("wf-mapping-link-readout-devices"),
            _ => Loc.GetString("wf-mapping-link-readout-access"),
        };
    }

    private void UpdateOverlay()
    {
        var wanted = ShowAll || ModeActive;
        if (wanted && !_overlays.HasOverlay<WFMappingLinkOverlay>())
            _overlays.AddOverlay(new WFMappingLinkOverlay(this, _modes));
        else if (!wanted)
            _overlays.RemoveOverlay<WFMappingLinkOverlay>();
    }

    private void CloseWindows()
    {
        _portsWindow?.Close();
        _accessWindow?.Close();
    }

    private void Send(EntityEventArgs ev)
    {
        RaiseNetworkEvent(ev);
    }

    private void Popup(string key)
    {
        _popup.PopupCursor(Loc.GetString(key));
    }
}
