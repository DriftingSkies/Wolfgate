# Going in and out of a vehicle.
rmc-vehicle-enter-locked = The vehicle is locked.
rmc-vehicle-enter-use-doorway = You need to use a doorway to enter.
rmc-vehicle-enter-busy = Someone is already entering there.
rmc-vehicle-enter-passenger-full = There's no room for more passengers inside.
rmc-vehicle-enter-pulled-full = There's no room for who you're dragging inside.
rmc-vehicle-exit-busy = Someone is already using this exit.
rmc-vehicle-exit-blocked = The exit is blocked.
rmc-vehicle-look-inside = Look inside
wf-vehicle-viewport-driving = You already have a view of the outside from your seat.
wf-vehicle-demolition-warning = A demolition charge is ticking on the hull! Get out!

# The lock.
rmc-vehicle-lock-not-driver = You need to be in the driver seat to lock or unlock the vehicle.
rmc-vehicle-lock-broken-attempt = The vehicle cannot be locked until the broken lock is repaired.
rmc-vehicle-lock-frame-destroyed = The vehicle cannot be locked while its frame is destroyed.
rmc-vehicle-lock-set-locked = Vehicle doors locked.
rmc-vehicle-lock-set-unlocked = Vehicle doors unlocked.
rmc-vehicle-lock-broken-success = You break the vehicle lock.
rmc-vehicle-lock-repaired = You repair the vehicle lock.

# Keys.
rmc-vehicle-key-name = vehicle key
rmc-vehicle-key-name-copy = duplicate vehicle key
rmc-vehicle-key-name-specific = {$vehicle} key
rmc-vehicle-key-name-copy-specific = duplicate {$vehicle} key
rmc-vehicle-key-bind-success = You imprint the key to the vehicle.
rmc-vehicle-key-copy-success = You copy the vehicle key.
rmc-vehicle-key-copy-invalid = That key cannot be copied.
rmc-vehicle-key-copy-requires-source = You need to copy an existing vehicle key first.
rmc-vehicle-key-unbound = The key is not bound to any vehicle.
rmc-vehicle-key-invalid = The key does not fit this vehicle.
wf-vehicle-key-blank-locked = The lock will not take a new key while the vehicle is locked.
rmc-vehicle-key-examine-blank = [color=lightblue]This blank key can be imprinted onto a vehicle by using it on the vehicle.[/color]
rmc-vehicle-key-examine-duplicator = [color=lightblue]This blank key can copy an existing vehicle key by using it on that key.[/color]
rmc-vehicle-key-examine-bound = [color=lightblue]This key is bound to a vehicle lock.[/color]

# Console command that toggles the doorway overlay (admin only).
cmd-rmc_vehicle_entries-desc = Toggles the vehicle doorway overlay: where a vehicle can be entered, where entrants land and the exit markers.
cmd-rmc_vehicle_entries-help = Usage: {$command}

# Entity names.
ent-ActionVehicleToggleView = Toggle View
    .desc = Switch between the interior and exterior view.
ent-ActionVehicleLock = Toggle Vehicle Lock
    .desc = Locks or unlocks vehicle entry doors.
ent-RMCVehicleKey = vehicle key
    .desc = A vehicle key cut for a specific lock.
ent-RMCVehicleKeyBlank = blank vehicle key
    .desc = An unprogrammed vehicle key that can be imprinted to a vehicle.
ent-RMCVehicleKeyDuplicator = duplicate vehicle key blank
    .desc = A blank vehicle key designed to copy an existing vehicle key.
ent-VehicleExitArea = vehicle exit marker
    .desc = A clickable marker used for vehicle exits.
ent-RMCSeatDriver = driver's chair
    .desc = A specially designed chair for driver to sit in.
