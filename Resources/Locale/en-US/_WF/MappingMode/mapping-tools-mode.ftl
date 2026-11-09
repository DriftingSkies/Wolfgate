# Tools build mode (key 0): the wrench and the instant toggles.
wf-mapping-screen-mouse-wrench = Pick a tool in Properties, then left click: wrench anchors, bolts, weld, lock, panel and door flip that state.
wf-mapping-screen-readout-tool-target = {$tool}: {$name} is {$state}
wf-mapping-screen-readout-tool-wrench = Wrench: click something to anchor or unanchor it
wf-mapping-screen-readout-tool-bolts = Bolts: click a door to bolt or unbolt it
wf-mapping-screen-readout-tool-weld = Weld: click a door, vent, scrubber or locker to weld or unweld it
wf-mapping-screen-readout-tool-lock = Lock: click a locker, crate or machine to lock or unlock it
wf-mapping-screen-readout-tool-panel = Panel: click something with a maintenance panel to open or close it
wf-mapping-screen-readout-tool-door = Door: click a door to open or close it

wf-mapping-tool-wrench = Wrench
wf-mapping-tool-wrench-tooltip = Anchor or unanchor.
wf-mapping-tool-bolts = Bolts
wf-mapping-tool-bolts-tooltip = Bolt or unbolt a door. No power needed.
wf-mapping-tool-weld = Weld
wf-mapping-tool-weld-tooltip = Weld or unweld a closed door, vent, scrubber or locker.
wf-mapping-tool-lock = Lock
wf-mapping-tool-lock-tooltip = Lock or unlock.
wf-mapping-tool-panel = Panel
wf-mapping-tool-panel-tooltip = Open or close a maintenance panel.
wf-mapping-tool-door = Door
wf-mapping-tool-door-tooltip = Open or close a door, without the animation.

wf-mapping-tool-state-wrench = {$on ->
    [yes] anchored
   *[other] loose
}
wf-mapping-tool-state-bolts = {$on ->
    [yes] bolted
   *[other] unbolted
}
wf-mapping-tool-state-weld = {$on ->
    [yes] welded
   *[other] not welded
}
wf-mapping-tool-state-lock = {$on ->
    [yes] locked
   *[other] unlocked
}
wf-mapping-tool-state-panel = {$on ->
    [yes] panel open
   *[other] panel closed
}
wf-mapping-tool-state-door = {$on ->
    [yes] open
   *[other] closed
}

wf-mapping-tool-none-wrench = Nothing here takes a wrench.
wf-mapping-tool-none-bolts = No door with bolts here.
wf-mapping-tool-none-weld = Nothing here can be welded.
wf-mapping-tool-none-lock = Nothing here has a lock.
wf-mapping-tool-none-panel = Nothing here has a panel.
wf-mapping-tool-none-door = No door here.
wf-mapping-tool-bad = That tool doesn't work on that.
wf-mapping-tool-refused = That won't change.
wf-mapping-tool-close-first = Close it first.
wf-mapping-tool-unweld-first = Unweld it first.
wf-mapping-tool-done = {$name}: {$state}.

wf-mapping-wrench-bad = That can't be wrenched.
wf-mapping-wrench-no-grid = That needs a tile under it.
wf-mapping-wrench-fixed = That can't be turned that way.
wf-mapping-wrench-anchored = Anchored {$name}.
wf-mapping-wrench-unanchored = Unanchored {$name}.

wf-mapping-tools-edit-wrench = wrench
wf-mapping-tools-edit-bolts = bolts
wf-mapping-tools-edit-weld = weld
wf-mapping-tools-edit-lock = lock
wf-mapping-tools-edit-panel = panel
wf-mapping-tools-edit-door = door
