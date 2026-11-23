wg-ship-module-key = { $label } { $charges }/{ $max }
wg-ship-module-tooltip = Fire with { $key } while piloting, or press this key.
wg-ship-module-status-ready = READY
wg-ship-module-status-cooldown = COOLDOWN
wg-ship-module-status-charging = CHARGING { $seconds }s
wg-ship-module-status-unpowered = HELM UNPOWERED
wg-ship-module-status-shields = SHIELDS ON
wg-ship-module-status-ftl = IN FTL
wg-ship-module-status-docked = DOCKED
wg-ship-module-status-anchored = ANCHORED

wg-ship-module-refused-unpowered = The helm has no power to fire it.
wg-ship-module-refused-shields = A shield generator is on. Switch it off and give the field a few seconds to clear.
wg-ship-module-refused-ftl = Not during a jump.
wg-ship-module-refused-docked = Not while docked.
wg-ship-module-refused-anchored = The hull is held fast.
wg-ship-module-refused-empty = No charge ready.

wg-ship-module-jets-name = MJ-3 "Sidestep" Maneuvering Jets
wg-ship-module-jets-description = Kicks the ship whichever way the pilot steers. Three charges, one back every 8 s. Won't fire while a shield generator is on, except a deflector. Light hulls get the biggest push.
wg-ship-module-jets-label = JETS
wg-ship-module-jets-no-direction = Hold a direction to burn along.

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
