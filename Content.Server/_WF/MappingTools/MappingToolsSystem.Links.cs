using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.DeviceLinking.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Shared._WF.MappingTools;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers;
using Content.Shared.Database;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingTools;

public sealed partial class MappingToolsSystem
{
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private DeviceLinkSystem _deviceLink = default!;
    [Dependency] private DeviceListSystem _deviceList = default!;

    /// <summary>Most port pairs, devices or access levels one link request may name.</summary>
    private const int MaxLinkItems = 256;

    /// <summary>Most readers one access request may set.</summary>
    private const int MaxAccessTargets = 4096;

    /// <summary>Most links a snapshot sends.</summary>
    private const int MaxSnapshotLinks = 16384;

    private void InitializeLinks()
    {
        SubscribeNetworkEvent<MappingToolsLinkEvent>(OnLink);
        SubscribeNetworkEvent<MappingToolsLinkPortsRequestEvent>(OnLinkPortsRequest);
        SubscribeNetworkEvent<MappingToolsLinksRequestEvent>(OnLinksRequest);
        SubscribeNetworkEvent<MappingToolsDeviceListEvent>(OnDeviceList);
        SubscribeNetworkEvent<MappingToolsAccessEvent>(OnAccess);
        SubscribeLocalEvent<WFMappingAccessFillComponent, MapInitEvent>(OnAccessFillMapInit,
            after: [typeof(ContainerFillSystem)]);
    }

    private void OnLink(MappingToolsLinkEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            SetLinks(args.SenderSession, ev);
    }

    private void OnLinkPortsRequest(MappingToolsLinkPortsRequestEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            SendLinkPorts(args.SenderSession, ev.Source, ev.Sink);
    }

    private void OnLinksRequest(MappingToolsLinksRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CanUse(args.SenderSession, request: true) ||
            !TryGetEntity(ev.Map, out var map) ||
            !HasComp<MapComponent>(map) ||
            !CanEdit(new WFMappingCanEditEvent(args.SenderSession, map.Value, WFMappingEditKind.Read) { Quiet = true }))
        {
            return;
        }

        SendLinkSnapshot(args.SenderSession, map.Value);
    }

    private void OnDeviceList(MappingToolsDeviceListEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            SetDeviceList(args.SenderSession, ev);
    }

    private void OnAccess(MappingToolsAccessEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            SetAccess(args.SenderSession, ev);
    }

    /// <summary>Tells the player why a link request did nothing, and logs it.</summary>
    private void RefuseLink(ICommonSession session, string key, params (string, object)[] args)
    {
        Log.Debug($"Refused a link request from {session}: {key}");
        _popup.PopupCursor(Loc.GetString(key, args), session);
    }

    /// <summary>An entity a request names, if it exists on a map and has the component.</summary>
    private bool TryGetOnMap<T>(NetEntity net, out EntityUid uid, out EntityUid map, [NotNullWhen(true)] out T? comp)
        where T : IComponent
    {
        map = default;
        comp = default;
        if (!TryGetEntity(net, out var found) || TerminatingOrDeleted(found) ||
            Transform(found.Value).MapUid is not { } onMap || !TryComp(found, out comp))
        {
            uid = found ?? EntityUid.Invalid;
            return false;
        }

        uid = found.Value;
        map = onMap;
        return true;
    }

    /// <summary>Asks whether a request may edit each map once; the canceller tells the player why.</summary>
    private bool CanEditMaps(ICommonSession session, IEnumerable<EntityUid> maps, int touched)
    {
        foreach (var map in maps.Distinct())
        {
            var check = new WFMappingCanEditEvent(session, map, WFMappingEditKind.Edit) { TouchedEntities = touched };
            if (CanEdit(check))
                continue;

            Log.Debug($"Refused a link request from {session} on {ToPrettyString(map)}: {check.Reason ?? "cancelled"}");
            if (check.Reason == null)
                _popup.PopupCursor(Loc.GetString("wf-mapping-tools-draw-not-allowed"), session);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Sets the links between a source and a sink to the requested port pairs, as one undoable edit. The game's own
    /// link checks (range, link attempts) apply; no user is named, so nothing pops up from them.
    /// </summary>
    public void SetLinks(ICommonSession session, MappingToolsLinkEvent ev)
    {
        if (!TryGetOnMap<DeviceLinkSourceComponent>(ev.Source, out var source, out var sourceMap, out var sourceComp) ||
            !TryGetOnMap<DeviceLinkSinkComponent>(ev.Sink, out var sink, out var sinkMap, out var sinkComp))
        {
            RefuseLink(session, "wf-mapping-link-bad");
            return;
        }

        var links = ev.Links.Distinct().ToList();
        var sourcePorts = sourceComp.Ports;
        var sinkPorts = sinkComp.Ports;
        if (links.Count > MaxLinkItems ||
            links.Any(l => !sourcePorts.Contains(l.Source) || !sinkPorts.Contains(l.Sink) ||
                           !_prototypes.HasIndex<SourcePortPrototype>(l.Source) ||
                           !_prototypes.HasIndex<SinkPortPrototype>(l.Sink)))
        {
            RefuseLink(session, "wf-mapping-link-bad-port");
            return;
        }

        if (links.Count > 0 && (sourceMap != sinkMap || !_transform.GetMapCoordinates(source)
                .InRange(_transform.GetMapCoordinates(sink), sourceComp.Range)))
        {
            RefuseLink(session, "wf-mapping-link-out-of-range", ("range", (int) sourceComp.Range));
            return;
        }

        var old = CurrentLinks(sourceComp, sink);
        if (old.Count == links.Count && old.All(links.Contains))
        {
            RefuseLink(session, "wf-mapping-link-unchanged");
            return;
        }

        if (!CanEditMaps(session, [sourceMap, sinkMap], 2))
            return;

        var edit = new MappingEdit(links.Count == 0 ? "wf-mapping-tools-edit-unlink" : "wf-mapping-tools-edit-link");
        edit.Links.Add(new MappingLinkChange(GetRef(source), GetRef(sink), old, links));
        Commit(session, edit);

        var now = CurrentLinks(sourceComp, sink);
        _popup.PopupCursor(now.Count == 0
                ? Loc.GetString("wf-mapping-link-unlinked")
                : Loc.GetString("wf-mapping-link-linked", ("count", now.Count)),
            session);
        if (now.Count != links.Count)
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-link-some-refused", ("count", links.Count - now.Count)),
                session);
        }

        _adminLog.Add(LogType.DeviceLinking, LogImpact.Low,
            $"{session:player} used mapping tools to set {now.Count} links from {ToPrettyString(source):source} to {ToPrettyString(sink):sink}");
    }

    private static List<(string Source, string Sink)> CurrentLinks(DeviceLinkSourceComponent source, EntityUid sink)
    {
        var links = new List<(string Source, string Sink)>();
        var linked = source.LinkedPorts;
        if (!linked.TryGetValue(sink, out var ports))
            return links;

        foreach (var (sourcePort, sinkPort) in ports)
        {
            links.Add((sourcePort.Id, sinkPort.Id));
        }

        return links;
    }

    /// <summary>Sends a source's and a sink's ports with their links and the game's default links.</summary>
    private void SendLinkPorts(ICommonSession session, NetEntity sourceNet, NetEntity sinkNet)
    {
        if (!TryGetOnMap<DeviceLinkSourceComponent>(sourceNet, out var source, out var sourceMap, out var sourceComp) ||
            !TryGetOnMap<DeviceLinkSinkComponent>(sinkNet, out var sink, out var sinkMap, out var sinkComp))
        {
            RefuseLink(session, "wf-mapping-link-bad");
            return;
        }

        foreach (var map in new[] { sourceMap, sinkMap }.Distinct())
        {
            if (!CanEdit(new WFMappingCanEditEvent(session, map, WFMappingEditKind.Read)))
                return;
        }

        var sourcePorts = sourceComp.Ports.Select(p => p.Id)
            .Where(id => _prototypes.HasIndex<SourcePortPrototype>(id))
            .ToList();
        var sinkPorts = sinkComp.Ports.Select(p => p.Id)
            .Where(id => _prototypes.HasIndex<SinkPortPrototype>(id))
            .ToList();
        sourcePorts.Sort(StringComparer.Ordinal);
        sinkPorts.Sort(StringComparer.Ordinal);

        var sinkSet = sinkComp.Ports;
        var defaults = _deviceLink.GetDefaults(_deviceLink.GetSourcePorts(source, sourceComp))
            .Where(l => sinkSet.Contains(l.sink))
            .Select(l => (l.source, l.sink))
            .ToList();

        RaiseNetworkEvent(new MappingToolsLinkPortsEvent(sourceNet, sinkNet, sourcePorts, sinkPorts,
            CurrentLinks(sourceComp, sink), defaults), session);
    }

    /// <summary>Sends the signal links whose source is on a map.</summary>
    private void SendLinkSnapshot(ICommonSession session, EntityUid map)
    {
        var links = new List<MappingSignalLinkState>();
        var query = AllEntityQuery<DeviceLinkSourceComponent, TransformComponent>(); // All: session maps are paused
        while (query.MoveNext(out var uid, out var source, out var xform) && links.Count < MaxSnapshotLinks)
        {
            if (xform.MapUid != map || source.LinkedPorts.Count == 0)
                continue;

            var net = GetNetEntity(uid);
            foreach (var (sink, ports) in source.LinkedPorts)
            {
                if (ports.Count > 0 && !TerminatingOrDeleted(sink))
                    links.Add(new MappingSignalLinkState(net, GetNetEntity(sink), ports.Count));
            }
        }

        RaiseNetworkEvent(new MappingToolsLinksSnapshotEvent(GetNetEntity(map), links), session);
    }

    /// <summary>Sends a fresh snapshot of every map whose signal links an edit changed.</summary>
    private void SendLinkSnapshots(ICommonSession session, MappingEdit edit)
    {
        if (edit.Links.Count == 0)
            return;

        var maps = new HashSet<EntityUid>();
        foreach (var change in edit.Links)
        {
            if (Exists(change.Source.Uid) && Transform(change.Source.Uid).MapUid is { } map)
                maps.Add(map);
        }

        foreach (var map in maps)
        {
            SendLinkSnapshot(session, map);
        }
    }

    /// <summary>
    /// Adds devices to a device list or takes them out, as one undoable edit. A device must be on the holder's map,
    /// have a device network and a savable address, as the network configurator asks.
    /// </summary>
    public void SetDeviceList(ICommonSession session, MappingToolsDeviceListEvent ev)
    {
        if (!TryGetOnMap<DeviceListComponent>(ev.Holder, out var holder, out var holderMap, out var list) ||
            ev.Devices.Count == 0 || ev.Devices.Count > MaxLinkItems || !Enum.IsDefined(ev.Op))
        {
            RefuseLink(session, "wf-mapping-link-bad");
            return;
        }

        var devices = new List<EntityUid>();
        var maps = new List<EntityUid> { holderMap };
        foreach (var net in ev.Devices.Distinct())
        {
            if (ev.Op == MappingDeviceListOp.Remove)
            {
                if (!TryGetEntity(net, out var removed))
                    continue;

                devices.Add(removed.Value);
                if (Exists(removed.Value) && Transform(removed.Value).MapUid is { } removedMap)
                    maps.Add(removedMap);

                continue;
            }

            if (!TryGetOnMap<DeviceNetworkComponent>(net, out var device, out var deviceMap, out var network) ||
                device == holder || deviceMap != holderMap || !network.SavableAddress)
            {
                RefuseLink(session, "wf-mapping-link-devices-bad");
                return;
            }

            devices.Add(device);
        }

        var old = list.Devices.ToList();
        var updated = new HashSet<EntityUid>(old);
        if (ev.Op == MappingDeviceListOp.Add)
            updated.UnionWith(devices);
        else
            updated.ExceptWith(devices);

        if (updated.SetEquals(old))
        {
            RefuseLink(session, "wf-mapping-link-unchanged");
            return;
        }

        if (updated.Count > list.DeviceLimit)
        {
            RefuseLink(session, "wf-mapping-link-devices-too-many", ("max", list.DeviceLimit));
            return;
        }

        if (!CanEditMaps(session, maps, 1 + devices.Count))
            return;

        var edit = new MappingEdit("wf-mapping-tools-edit-device-list");
        edit.DeviceLists.Add(new MappingDeviceListChange(GetRef(holder), old.ConvertAll(GetRef),
            updated.Select(GetRef).ToList()));
        Commit(session, edit);

        _popup.PopupCursor(Loc.GetString("wf-mapping-link-devices-set", ("count", list.Devices.Count),
            ("max", list.DeviceLimit)), session);
        _adminLog.Add(LogType.DeviceLinking, LogImpact.Low,
            $"{session:player} used mapping tools to {(ev.Op == MappingDeviceListOp.Add ? "add" : "remove")} {devices.Count} devices on {ToPrettyString(holder):subject}, now {list.Devices.Count}");
    }

    /// <summary>
    /// Sets the access of every target's reader to any one of the given levels, as one undoable edit. The reader
    /// written is the one the game checks (an airlock's electronics); before map init, when those aren't there yet,
    /// the target's own reader is written and marked so map init copies it over.
    /// </summary>
    public void SetAccess(ICommonSession session, MappingToolsAccessEvent ev)
    {
        if (ev.Targets.Count == 0 || ev.Targets.Count > MaxAccessTargets || ev.AccessLevels.Count > MaxLinkItems)
        {
            RefuseLink(session, "wf-mapping-link-bad");
            return;
        }

        var levels = new List<ProtoId<AccessLevelPrototype>>();
        foreach (var id in ev.AccessLevels.Distinct())
        {
            if (!_prototypes.HasIndex<AccessLevelPrototype>(id))
            {
                RefuseLink(session, "wf-mapping-link-access-bad");
                return;
            }

            levels.Add(id);
        }

        var targets = new List<EntityUid>();
        var maps = new List<EntityUid>();
        foreach (var net in ev.Targets.Distinct())
        {
            if (!TryGetOnMap<AccessReaderComponent>(net, out var target, out var map, out _))
            {
                RefuseLink(session, "wf-mapping-link-access-no-reader");
                return;
            }

            targets.Add(target);
            maps.Add(map);
        }

        var lists = levels.ConvertAll(l => new HashSet<ProtoId<AccessLevelPrototype>> { l });
        var edit = new MappingEdit("wf-mapping-tools-edit-access");
        var writers = new HashSet<EntityUid>();
        foreach (var target in targets)
        {
            if (!_accessReader.GetMainAccessReader(target, out var main) || !writers.Add(main.Value.Owner))
                continue;

            var reader = main.Value;
            var oldFill = HasComp<WFMappingAccessFillComponent>(reader);
            var newFill = reader.Owner == target && reader.Comp.ContainerAccessProvider != null &&
                          MetaData(target).EntityLifeStage < EntityLifeStage.MapInitialized;
            var old = reader.Comp.AccessLists.ConvertAll(set => new HashSet<ProtoId<AccessLevelPrototype>>(set));
            if (oldFill == newFill && SameAccess(old, lists))
                continue;

            edit.Access.Add(new MappingAccessChange(GetRef(reader.Owner), old, lists, oldFill, newFill));
        }

        if (edit.IsEmpty)
        {
            RefuseLink(session, "wf-mapping-link-unchanged");
            return;
        }

        if (!CanEditMaps(session, maps, edit.Access.Count))
            return;

        Commit(session, edit);
        _popup.PopupCursor(Loc.GetString("wf-mapping-link-access-set", ("count", edit.Access.Count)), session);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to set the access of {edit.Access.Count} readers to [{string.Join(", ", levels)}]");
    }

    private static bool SameAccess(List<HashSet<ProtoId<AccessLevelPrototype>>> a,
        List<HashSet<ProtoId<AccessLevelPrototype>>> b)
    {
        return a.Count == b.Count && a.All(set => b.Any(set.SetEquals));
    }

    /// <summary>Takes links, device lists and access to one side of an edit.</summary>
    private void ApplySettings(MappingEdit edit, bool forward)
    {
        foreach (var change in edit.Links)
        {
            ApplyLinks(change.Source.Uid, change.Sink.Uid, forward ? change.New : change.Old);
        }

        foreach (var change in edit.DeviceLists)
        {
            var holder = change.Holder.Uid;
            if (TerminatingOrDeleted(holder) || !HasComp<DeviceListComponent>(holder))
                continue;

            var devices = (forward ? change.New : change.Old)
                .Select(r => r.Uid)
                .Where(uid => !TerminatingOrDeleted(uid))
                .ToList();
            _deviceList.UpdateDeviceList(holder, devices);
        }

        foreach (var change in edit.Access)
        {
            ApplyAccess(change.Reader.Uid, forward ? change.New : change.Old, forward ? change.NewFill : change.OldFill);
        }
    }

    private void ApplyLinks(EntityUid source, EntityUid sink, List<(string Source, string Sink)> links)
    {
        if (TerminatingOrDeleted(source) || TerminatingOrDeleted(sink) ||
            !TryComp(source, out DeviceLinkSourceComponent? sourceComp) ||
            !TryComp(sink, out DeviceLinkSinkComponent? sinkComp))
        {
            return;
        }

        if (links.Count == 0)
            _deviceLink.RemoveSinkFromSource(source, sink, sourceComp, sinkComp);
        else
            _deviceLink.SaveLinks(null, source, sink, links, sourceComp, sinkComp);
    }

    /// <summary>Sets a reader's access lists, through the reader system's setter when each holds one level.</summary>
    private void ApplyAccess(EntityUid uid, List<HashSet<ProtoId<AccessLevelPrototype>>> lists, bool fill)
    {
        if (TerminatingOrDeleted(uid) || !TryComp(uid, out AccessReaderComponent? reader))
            return;

        if (lists.All(set => set.Count == 1))
        {
            _accessReader.SetAccesses(uid, reader, lists.ConvertAll(set => set.First()));
        }
        else
        {
            reader.AccessLists = lists.ConvertAll(set => new HashSet<ProtoId<AccessLevelPrototype>>(set));
            Dirty(uid, reader);
            RaiseLocalEvent(uid, new AccessReaderConfigurationChangedEvent());
        }

        if (fill)
            EnsureComp<WFMappingAccessFillComponent>(uid);
        else
            RemComp<WFMappingAccessFillComponent>(uid);
    }

    /// <summary>Copies access set before map init into the reader map init put in the container.</summary>
    private void OnAccessFillMapInit(Entity<WFMappingAccessFillComponent> ent, ref MapInitEvent args)
    {
        if (TryComp(ent, out AccessReaderComponent? own) &&
            _accessReader.GetMainAccessReader(ent, out var main) &&
            main.Value.Owner != ent.Owner)
        {
            ApplyAccess(main.Value.Owner, own.AccessLists, false);
        }

        RemCompDeferred<WFMappingAccessFillComponent>(ent);
    }
}
