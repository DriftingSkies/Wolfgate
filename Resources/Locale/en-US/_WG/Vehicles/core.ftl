# Core vehicle features (overcharge, horn, spotlight) and the hardpoint window.
ent-WGActionVehicleOvercharge = Overcharge engine
    .desc = Pushes the engine past its limit for a few seconds of extra top speed. It needs time to recover afterwards.

rmc-hardpoint-ui-title = Hardpoints
rmc-hardpoint-ui-empty-slot = Empty
rmc-hardpoint-ui-removing = Removing...
rmc-hardpoint-ui-integrity = {$current}/{$max} ({$percent}%)

wg-vehicles-hardpoint-ui-heading = Vehicle Hardpoints
wg-vehicles-hardpoint-ui-frame-integrity = Frame Integrity
wg-vehicles-hardpoint-ui-part-integrity = {$current}/{$max}
wg-vehicles-hardpoint-ui-open-slot = Open: {$slot}
wg-vehicles-hardpoint-ui-nested-slot = {$parent} / {$slot}

cmd-rmc_vehicle_hardpoint-desc = Toggles the vehicle hardpoint overlay: turret anchors and offsets.
cmd-rmc_vehicle_hardpoint-help = Usage: {$command}

wg-vehicles-entity-category = Vehicles

wg-vehicle-plow-working = The plow bites into the structure. Keep pushing forward!
wg-vehicle-plow-indestructible = The plow cannot break through this structure.

ent-WGActionVehicleSteeringMode = Switch steering
    .desc = Switch between free steering and fixed four-way driving.
ent-WGActionVehicleScanner = Scanner
    .desc = Open the vehicle's mass scanner.
wg-vehicle-scanner-position = Position: { $x }, { $y }
wg-vehicle-steering-free = Steering: free.
wg-vehicle-steering-fixed = Steering: four-way.
wg-vehicle-steering-no-room = No room to square the vehicle up.
