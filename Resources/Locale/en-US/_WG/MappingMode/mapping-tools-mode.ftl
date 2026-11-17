# Tools build mode (key 0): the wrench and the instant toggles.
wg-mapping-screen-mouse-wrench = Pick a tool in Properties, then left click: wrench anchors, bolts, weld, lock, panel and door flip that state.
wg-mapping-screen-readout-tool-target = {$tool}: {$name} is {$state}
wg-mapping-screen-readout-tool-wrench = Wrench: click something to anchor or unanchor it
wg-mapping-screen-readout-tool-bolts = Bolts: click a door to bolt or unbolt it
wg-mapping-screen-readout-tool-weld = Weld: click a door, vent, scrubber or locker to weld or unweld it
wg-mapping-screen-readout-tool-lock = Lock: click a locker, crate or machine to lock or unlock it
wg-mapping-screen-readout-tool-panel = Panel: click something with a maintenance panel to open or close it
wg-mapping-screen-readout-tool-door = Door: click a door to open or close it

wg-mapping-tool-wrench = Wrench
wg-mapping-tool-wrench-tooltip = Anchor or unanchor.
wg-mapping-tool-bolts = Bolts
wg-mapping-tool-bolts-tooltip = Bolt or unbolt a door. No power needed.
wg-mapping-tool-weld = Weld
wg-mapping-tool-weld-tooltip = Weld or unweld a closed door, vent, scrubber or locker.
wg-mapping-tool-lock = Lock
wg-mapping-tool-lock-tooltip = Lock or unlock.
wg-mapping-tool-panel = Panel
wg-mapping-tool-panel-tooltip = Open or close a maintenance panel.
wg-mapping-tool-door = Door
wg-mapping-tool-door-tooltip = Open or close a door, without the animation.

wg-mapping-tool-state-wrench = {$on ->
    [yes] anchored
   *[other] loose
}
wg-mapping-tool-state-bolts = {$on ->
    [yes] bolted
   *[other] unbolted
}
wg-mapping-tool-state-weld = {$on ->
    [yes] welded
   *[other] not welded
}
wg-mapping-tool-state-lock = {$on ->
    [yes] locked
   *[other] unlocked
}
wg-mapping-tool-state-panel = {$on ->
    [yes] panel open
   *[other] panel closed
}
wg-mapping-tool-state-door = {$on ->
    [yes] open
   *[other] closed
}

wg-mapping-tool-none-wrench = Nothing here takes a wrench.
wg-mapping-tool-none-bolts = No door with bolts here.
wg-mapping-tool-none-weld = Nothing here can be welded.
wg-mapping-tool-none-lock = Nothing here has a lock.
wg-mapping-tool-none-panel = Nothing here has a panel.
wg-mapping-tool-none-door = No door here.
wg-mapping-tool-bad = That tool doesn't work on that.
wg-mapping-tool-refused = That won't change.
wg-mapping-tool-close-first = Close it first.
wg-mapping-tool-unweld-first = Unweld it first.
wg-mapping-tool-done = {$name}: {$state}.

wg-mapping-wrench-bad = That can't be wrenched.
wg-mapping-wrench-no-grid = That needs a tile under it.
wg-mapping-wrench-fixed = That can't be turned that way.
wg-mapping-wrench-anchored = Anchored {$name}.
wg-mapping-wrench-unanchored = Unanchored {$name}.

wg-mapping-tools-edit-wrench = wrench
wg-mapping-tools-edit-bolts = bolts
wg-mapping-tools-edit-weld = weld
wg-mapping-tools-edit-lock = lock
wg-mapping-tools-edit-panel = panel
wg-mapping-tools-edit-door = door
