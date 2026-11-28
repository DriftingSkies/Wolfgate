wg-ship-module-key = { $label } { $charges }/{ $max }
wg-ship-module-key-toggle = { $label } { $on ->
    [true] ON
   *[false] OFF
}
wg-ship-module-tooltip = Fire with { $key } while piloting, or press this key.
wg-ship-module-status-ready = READY
wg-ship-module-status-cooldown = COOLDOWN
wg-ship-module-status-charging = CHARGING { $seconds }s
wg-ship-module-status-unpowered = HELM UNPOWERED
wg-ship-module-status-shields = SHIELDS ON
wg-ship-module-status-ftl = IN FTL
wg-ship-module-status-docked = DOCKED
wg-ship-module-status-anchored = ANCHORED
wg-ship-module-status-venting = VENTING
wg-ship-module-status-overloaded = OVERLOADED
wg-ship-module-status-held = HELD
wg-ship-module-status-planet = OVER PLANET
wg-ship-module-status-nofield = NO FIELD
wg-ship-module-status-active = ACTIVE { $seconds }s
wg-ship-module-status-engaged = ENGAGED
wg-ship-module-status-online = ONLINE
wg-ship-module-status-reserve = { $percent }%
wg-ship-module-status-empty = EMPTY

wg-ship-module-refused-unpowered = The helm has no power to fire it.
wg-ship-module-refused-shields = A shield generator is on. Switch it off and give the field a few seconds to clear.
wg-ship-module-refused-ftl = Not during a jump.
wg-ship-module-refused-docked = Not while docked.
wg-ship-module-refused-anchored = The hull is held fast.
wg-ship-module-refused-venting = Not while the ship vents its heat.
wg-ship-module-refused-overloaded = Not while the shield generator is overloaded.
wg-ship-module-refused-empty = No charge ready.
wg-ship-module-refused-held = Something has hold of the hull.
wg-ship-module-refused-planet = Not over a planet.
wg-ship-module-refused-nofield = Raise the ship's shield field first.
wg-ship-module-refused-passive = It is always on while fitted.
wg-ship-module-refused-running = It is already running.

wg-ship-module-jets-name = MJ-3 "Sidestep" Maneuvering Jets
wg-ship-module-jets-description = Kicks the ship whichever way the pilot steers. Three charges, one back every 8 s. Won't fire while a shield generator is on, except a deflector. Light hulls get the biggest push.
wg-ship-module-jets-label = JETS
wg-ship-module-jets-no-direction = Hold a direction to burn along.

wg-ship-module-skimmer-name = PS-2 "Wolf" Phase Skimmer
wg-ship-module-skimmer-description = Phases the ship out and drops it back up to 160 m along its course: the way it is moving, or the way the pilot steers if it is nearly still. Stops short of other hulls, shakes off missile locks and heats the hull a little. Heavy ships skim less. Three charges, one back every 10 s.
wg-ship-module-skimmer-label = SKIM
wg-ship-module-skimmer-blocked = No clear space to skim into.

wg-ship-module-gyros-name = GX-4 Advanced Gun Gyros
wg-ship-module-gyros-description = Every turning ship gun on the hull swings half as fast again. Always on.
wg-ship-module-gyros-label = GYROS

wg-ship-module-fortress-name = FS-9 "Bastille" Fortress Shield
wg-ship-module-fortress-description = Hardens the raised shield field: hits it absorbs heat the ship a quarter as much. While it is on the guns stay silent and the field builds hard heat every second, so it can overload you. Switch it on and off; it drops when the field does.
wg-ship-module-fortress-label = FORTRESS

wg-ship-module-feeder-name = AF-2 Accelerated Ammo Feeder
wg-ship-module-feeder-description = For 6 s every ballistic ship gun fires twice as fast, and heats the ship twice as fast too. Energy guns and missile launchers are left alone. One charge, back in 24 s.
wg-ship-module-feeder-label = FEEDER

ui-options-function-wg-ship-module-first = Ship module 1
ui-options-function-wg-ship-module-second = Ship module 2
ui-options-function-wg-ship-module-third = Ship module 3

cmd-shipmodule-desc = Fits a ship module into a slot of any ship, free and at once, or empties the slot.
cmd-shipmodule-help = Usage: { $command } <grid entity | here> <slot 1-3> <module id | none>
cmd-shipmodule-hint-slot = <slot 1-3>
cmd-shipmodule-hint-module = <module id | none>
cmd-shipmodule-unknown = "{ $module }" isn't a ship module.
cmd-shipmodule-fitted = Fitted { $module } into slot { $slot }.
cmd-shipmodule-fitted-elsewhere = { $module } is already fitted in slot { $slot }; a ship carries each module once.
cmd-shipmodule-emptied = Emptied slot { $slot }.
