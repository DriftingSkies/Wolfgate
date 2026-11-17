## Mapping screen: Link mode (signal links, device lists, access)

wg-mapping-screen-mode-link = Link
wg-mapping-screen-mouse-link = Signals: click a source, then a sink to pick ports; right click a sink to unlink. Devices: click an air alarm or other list, then click devices to add or remove them, or drag a box to add them all. Access: click a door or other reader. Alarms: click a vent, scrubber, sensor or air alarm to set its alarm thresholds. Tab: next tool.
wg-mapping-screen-layer-links = Links (signals, device lists)
wg-mapping-screen-layer-links-count = {$links ->
    [one] 1 signal link
   *[other] {$links} signal links
} and {$lists ->
    [one] 1 device list
   *[other] {$lists} device lists
} on this level.
wg-mapping-screen-layer-links-none = Nothing to draw: no signal links or device lists on this level yet. Wire some in Link mode (9).
wg-mapping-screen-key-WGMappingLinkCycle = Link mode: next tool (signals, devices, access, alarms)

wg-mapping-link-tool-signals = Signals
wg-mapping-link-tool-signals-tooltip = Link a button, lever or sensor to doors, lights and other machines.
wg-mapping-link-tool-devices = Devices
wg-mapping-link-tool-devices-tooltip = Choose the vents, scrubbers, sensors and firelocks an air alarm, fire alarm or console watches.
wg-mapping-link-tool-access = Access
wg-mapping-link-tool-access-tooltip = Set which access opens a door, locker or other reader.
wg-mapping-link-tool-thresholds = Alarms
wg-mapping-link-tool-thresholds-tooltip = Set when vents, scrubbers and sensors raise their air alarm. Click an air alarm to set every device it watches.
wg-mapping-link-clear = Clear selection
wg-mapping-link-selected = Selected: {$name}
wg-mapping-link-hint-signals = Click a signal source.
wg-mapping-link-hint-devices = Click an air alarm, fire alarm or console.
wg-mapping-link-hint-access = Click a door or other reader.
wg-mapping-link-hint-thresholds = Click a vent, scrubber, sensor or air alarm.

wg-mapping-link-readout-signals = Link: signals, click a source
wg-mapping-link-readout-source = Link: signals, source: {$name} ({$count} linked)
wg-mapping-link-readout-devices = Link: devices, click a device list
wg-mapping-link-readout-holder = Link: devices, list: {$name} ({$count}/{$max})
wg-mapping-link-readout-access = Link: access, click a reader
wg-mapping-link-readout-thresholds = Link: alarms, click a vent, scrubber, sensor or air alarm

wg-mapping-link-not-source = That doesn't send signals.
wg-mapping-link-not-sink = That doesn't take signals.
wg-mapping-link-switched-devices = Switched to Devices.
wg-mapping-link-switched-devices-pick = Switched to Devices. Click an air alarm, fire alarm or console.
wg-mapping-link-not-holder = That has no device list.
wg-mapping-link-not-device = That can't go in a device list.
wg-mapping-link-not-reader = That has no access reader.
wg-mapping-link-box-empty = No devices in that box.
wg-mapping-link-access-no-selection = Nothing in the selection has an access reader.
wg-mapping-link-thresholds-no-selection = Nothing is selected.
wg-mapping-link-not-monitor = That isn't a vent, scrubber, sensor or air alarm.

wg-mapping-link-apply = Apply
wg-mapping-link-ports-title = Signal links
wg-mapping-link-ports-heading = {$source} → {$sink}
wg-mapping-link-ports-corner = Source ↓ Sink →
wg-mapping-link-ports-none = One of them has no ports.
wg-mapping-link-ports-defaults = Link defaults
wg-mapping-link-ports-clear = Unlink all

wg-mapping-link-access-title = Access
wg-mapping-link-access-target = Reader: {$name}
wg-mapping-link-access-note = Anyone with one of the ticked accesses gets in. None ticked: anyone gets in.
wg-mapping-link-access-other = Other
wg-mapping-link-access-copy = Copy from last
wg-mapping-link-access-clear = Clear
wg-mapping-link-access-selection = Apply to selection
wg-mapping-link-access-selection-tooltip = Sets every door and reader in the current selection.

wg-mapping-link-thresholds-title = Alarm thresholds
wg-mapping-link-thresholds-target = {$name}: sets {$count} {$count ->
    [one] device
   *[other] devices
}
wg-mapping-link-thresholds-note = These save with the map. A device only takes the kinds it already checks.
wg-mapping-link-thresholds-kelvin = In kelvin: 273 K is 0 °C, 293 K is 20 °C.
wg-mapping-link-thresholds-preset = {$kind} preset:
wg-mapping-link-thresholds-preset-pick = Load a preset...
wg-mapping-link-thresholds-none = This device checks nothing.
wg-mapping-link-thresholds-selection-tooltip = Sets every vent, scrubber and sensor in the current selection, and every device the selected air alarms watch.

wg-mapping-link-bad = That can't be linked.
wg-mapping-link-bad-port = One of those ports doesn't exist on it.
wg-mapping-link-out-of-range = Too far apart to link; the source reaches {$range} tiles.
wg-mapping-link-unchanged = Nothing to change.
wg-mapping-link-linked = Linked {$count} {$count ->
    [one] port
   *[other] ports
}.
wg-mapping-link-unlinked = Unlinked.
wg-mapping-link-some-refused = {$count} {$count ->
    [one] link was
   *[other] links were
} refused by the devices.
wg-mapping-link-devices-bad = Only network devices on the same map can go in a device list.
wg-mapping-link-devices-too-many = A device list holds at most {$max} devices.
wg-mapping-link-devices-set = The list holds {$count}/{$max} devices.
wg-mapping-link-access-bad = Unknown access level.
wg-mapping-link-access-no-reader = Only things with an access reader can be set.
wg-mapping-link-access-set = Access set on {$count} {$count ->
    [one] reader
   *[other] readers
}.
wg-mapping-link-thresholds-no-devices = That air alarm watches no vents, scrubbers or sensors yet.
wg-mapping-link-thresholds-set = Alarm thresholds set on {$count} {$count ->
    [one] device
   *[other] devices
}.

wg-mapping-tools-edit-link = signal link
wg-mapping-tools-edit-unlink = signal unlink
wg-mapping-tools-edit-device-list = device list change
wg-mapping-tools-edit-access = access change
wg-mapping-tools-edit-thresholds = alarm threshold change
