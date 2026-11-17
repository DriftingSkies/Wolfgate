# Synthetics: the chassis panel window and the bay-open overlay. ALL CAPS, like the synthetic HUD.
wg-chassis-ui-title = CHASSIS PANEL
wg-chassis-ui-tab-parts = PARTS
wg-chassis-ui-tab-components = COMPONENTS
wg-chassis-ui-controls = CONTROLS

wg-chassis-ui-col-part = PART
wg-chassis-ui-col-state = STATE
wg-chassis-ui-col-integrity = INTEGRITY
wg-chassis-ui-col-load = LOAD
wg-chassis-ui-col-faults = FAULTS
wg-chassis-ui-col-slot = SLOT
wg-chassis-ui-col-component = COMPONENT
wg-chassis-ui-col-health = HEALTH
wg-chassis-ui-col-band = STATUS
wg-chassis-ui-col-actions = ACTIONS
wg-chassis-ui-doll-hint = SELECT A LIMB

wg-chassis-ui-state-detached = DETACHED
wg-chassis-ui-state-isolated = ISOLATED
wg-chassis-ui-state-loose = LOOSE
wg-chassis-ui-state-offline = OFFLINE
wg-chassis-ui-state-unpowered = UNPOWERED
wg-chassis-ui-state-ok = OK

wg-chassis-ui-none = --
wg-chassis-ui-empty = EMPTY
wg-chassis-ui-vital = VITAL
wg-chassis-ui-isolate = ISOLATE
wg-chassis-ui-restore = RESTORE
wg-chassis-ui-detach = DETACH
wg-chassis-ui-disconnect = DISCONNECT
wg-chassis-ui-confirm = CONFIRM
wg-chassis-ui-bay-open = OPEN BAY
wg-chassis-ui-bay-close = CLOSE BAY
wg-chassis-ui-reboot = REBOOT
wg-chassis-ui-overclock-off = OVERCLOCK: OFF
wg-chassis-ui-overclock-on = OVERCLOCK: ON

wg-chassis-ui-bay-banner = CHASSIS BAY OPEN - COMPONENTS EXPOSED
wg-chassis-ui-overclock-tag = OVERCLOCK

wg-chassis-band-normal = NOMINAL
wg-chassis-band-impaired = IMPAIRED
wg-chassis-band-failed = FAILED
wg-chassis-band-charge = CHARGE

wg-chassis-slot-eyes = OPTICS
wg-chassis-slot-posbrain = POSITRONIC CORE
wg-chassis-slot-brain = CCU
wg-chassis-slot-pump = COOLANT PUMP
wg-chassis-slot-heart = PUMP
wg-chassis-slot-lungs = COOLING LOOP
wg-chassis-slot-stomach = CONVERTER
wg-chassis-slot-liver = FILTER
wg-chassis-slot-kidneys = SCRUBBER
wg-chassis-slot-ears = AUDIO PACK
wg-chassis-slot-tongue = VOCODER
wg-chassis-slot-appendix = SPARE
wg-chassis-slot-battery = POWER CELL
wg-chassis-slot-radcollector = RAD COLLECTOR

# Hover tips. Sentences, not readout caps.
wg-chassis-ui-tip-state-ok = Running within rating.
wg-chassis-ui-tip-state-offline = Too damaged to run. Weld the chassis, replace the servo, or isolate it to stop the sensor load.
wg-chassis-ui-tip-state-unpowered = The cell is empty, so only one arm has power. Route power to this arm from your own menu, or seat a charged cell.
wg-chassis-ui-tip-state-isolated = Servos and sensors cut from the panel. The limb does nothing and reports no pain. RESTORE to power it.
wg-chassis-ui-tip-state-loose = Seated by hand. Slow and clumsy until a cable coil recouples it.
wg-chassis-ui-tip-state-detached = The limb is off the chassis. Click the limb on the body to seat it; it runs loose until a cable coil recouples it.

wg-chassis-ui-tip-load = Pain this part's damage sensors put on the chassis, as a share of what drops it. Repair the wounds, or isolate the part to mute it.
wg-chassis-ui-tip-faults-none = No faults logged.
wg-chassis-ui-tip-fault = { $fault } - { $advice }

wg-chassis-ui-tip-band-normal = Running within rating.
wg-chassis-ui-tip-band-impaired = Worn or damaged and running badly. Repair it before it fails.
wg-chassis-ui-tip-band-failed = Dead. The chassis loses whatever this component does until it is replaced or repaired.
wg-chassis-ui-tip-band-charge = Charge left in the power cell. At zero the chassis drops to low power: crawl speed and one arm, until a charged cell is seated.
wg-chassis-ui-tip-vital = Pulling this stops the chassis (pump) or ends the mind (core).

wg-chassis-ui-tip-isolate = Cuts the limb's servos and sensors. The limb does nothing and its wounds put no pain on the chassis. RESTORE reverses it.
wg-chassis-ui-tip-restore = Powers the limb's servos and sensors again. Its wounds count towards the sensor load once more.
wg-chassis-ui-tip-detach = Releases the limb's coupling so it comes off in one piece. Click it on the body to seat it again; it runs loose until a cable coil recouples it.
wg-chassis-ui-tip-disconnect = Pulls the component out of the chassis. Needs the bay open. A vital one asks you to confirm.
wg-chassis-ui-tip-bay-open = Opens the torso plating. Takes 3 seconds, 6 on yourself. Components can be pulled and seated by hand, but the chassis is slower and its components are exposed while it is open.
wg-chassis-ui-tip-bay-close = Shuts the torso plating. Takes 3 seconds, 6 on yourself. Speed returns to normal and the components are covered again.
wg-chassis-ui-tip-reboot = Restarts the chassis. It is down for 4 seconds and uses 10% of the charge, and the sensor load and lockups are cleared. Once every 2 minutes. Click twice to confirm.
wg-chassis-ui-tip-overclock-off = Runs the servos 25% faster for 3 times the drain. The chassis heats, throws sparks and short circuits as it does, and cuts out at 15% charge or when the torso overheats.
wg-chassis-ui-tip-overclock-on = Overclock is running: 25% faster, 3 times the drain. Click to stop it and let the chassis cool.

# The radiation collector and the built-in radiation meter.
wg-chassis-ui-rad-collector = RAD COL
wg-chassis-ui-rad-collector-none = NONE
wg-chassis-ui-radiation = RAD {$value} r/s
wg-chassis-ui-tip-rad-collector = How full the radiation collector is. Full, more radiation strains it and leaks into the core; pull it through the bay to let it vent. NONE: missing or burnt out, and the core takes all of it.
wg-chassis-ui-tip-radiation = Radiation reaching the chassis right now, in rads a second.
