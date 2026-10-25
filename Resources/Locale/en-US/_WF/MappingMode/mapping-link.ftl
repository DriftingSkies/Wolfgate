## Mapping screen: Link mode (signal links, device lists, access)

wf-mapping-screen-mode-link = Link
wf-mapping-screen-mouse-link = Signals: click a source, then a sink to pick ports; right click a sink to unlink. Devices: click an air alarm or other list, then click devices to add or remove them, or drag a box to add them all. Access: click a door or other reader. Tab: next tool.
wf-mapping-screen-layer-links = Links (signals, device lists)
wf-mapping-screen-layer-links-count = {$links ->
    [one] 1 signal link
   *[other] {$links} signal links
} and {$lists ->
    [one] 1 device list
   *[other] {$lists} device lists
} on this level.
wf-mapping-screen-layer-links-none = Nothing to draw: no signal links or device lists on this level yet. Wire some in Link mode (9).
wf-mapping-screen-key-WFMappingLinkCycle = Link mode: next tool (signals, devices, access)

wf-mapping-link-tool-signals = Signals
wf-mapping-link-tool-signals-tooltip = Link a button, lever or sensor to doors, lights and other machines.
wf-mapping-link-tool-devices = Devices
wf-mapping-link-tool-devices-tooltip = Choose the vents, scrubbers, sensors and firelocks an air alarm, fire alarm or console watches.
wf-mapping-link-tool-access = Access
wf-mapping-link-tool-access-tooltip = Set which access opens a door, locker or other reader.
wf-mapping-link-clear = Clear selection
wf-mapping-link-selected = Selected: {$name}
wf-mapping-link-hint-signals = Click a signal source.
wf-mapping-link-hint-devices = Click an air alarm, fire alarm or console.
wf-mapping-link-hint-access = Click a door or other reader.

wf-mapping-link-readout-signals = Link: signals, click a source
wf-mapping-link-readout-source = Link: signals, source: {$name} ({$count} linked)
wf-mapping-link-readout-devices = Link: devices, click a device list
wf-mapping-link-readout-holder = Link: devices, list: {$name} ({$count}/{$max})
wf-mapping-link-readout-access = Link: access, click a reader

wf-mapping-link-not-source = That doesn't send signals.
wf-mapping-link-not-sink = That doesn't take signals.
wf-mapping-link-not-holder = That has no device list.
wf-mapping-link-not-device = That can't go in a device list.
wf-mapping-link-not-reader = That has no access reader.
wf-mapping-link-box-empty = No devices in that box.
wf-mapping-link-access-no-selection = Nothing in the selection has an access reader.

wf-mapping-link-apply = Apply
wf-mapping-link-ports-title = Signal links
wf-mapping-link-ports-heading = {$source} → {$sink}
wf-mapping-link-ports-corner = Source ↓ Sink →
wf-mapping-link-ports-none = One of them has no ports.
wf-mapping-link-ports-defaults = Link defaults
wf-mapping-link-ports-clear = Unlink all

wf-mapping-link-access-title = Access
wf-mapping-link-access-target = Reader: {$name}
wf-mapping-link-access-note = Anyone with one of the ticked accesses gets in. None ticked: anyone gets in.
wf-mapping-link-access-other = Other
wf-mapping-link-access-copy = Copy from last
wf-mapping-link-access-clear = Clear
wf-mapping-link-access-selection = Apply to selection
wf-mapping-link-access-selection-tooltip = Sets every door and reader in the current selection.

wf-mapping-link-bad = That can't be linked.
wf-mapping-link-bad-port = One of those ports doesn't exist on it.
wf-mapping-link-out-of-range = Too far apart to link; the source reaches {$range} tiles.
wf-mapping-link-unchanged = Nothing to change.
wf-mapping-link-linked = Linked {$count} {$count ->
    [one] port
   *[other] ports
}.
wf-mapping-link-unlinked = Unlinked.
wf-mapping-link-some-refused = {$count} {$count ->
    [one] link was
   *[other] links were
} refused by the devices.
wf-mapping-link-devices-bad = Only network devices on the same map can go in a device list.
wf-mapping-link-devices-too-many = A device list holds at most {$max} devices.
wf-mapping-link-devices-set = The list holds {$count}/{$max} devices.
wf-mapping-link-access-bad = Unknown access level.
wf-mapping-link-access-no-reader = Only things with an access reader can be set.
wf-mapping-link-access-set = Access set on {$count} {$count ->
    [one] reader
   *[other] readers
}.

wf-mapping-tools-edit-link = signal link
wf-mapping-tools-edit-unlink = signal unlink
wf-mapping-tools-edit-device-list = device list change
wf-mapping-tools-edit-access = access change
