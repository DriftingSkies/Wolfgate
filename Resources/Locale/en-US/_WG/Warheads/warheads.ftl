wg-warhead-examine-state-safe = The safety is [color=green]on[/color].
wg-warhead-examine-state-armed = The safety is [color=red]off[/color].
wg-warhead-examine-state-countdown = It is [color=red]counting down[/color].
wg-warhead-examine-state-final = It is [color=red]about to go off[/color].
wg-warhead-examine-time = {$seconds} seconds left.
wg-warhead-examine-timer = The timer is set to {$seconds} seconds.
wg-warhead-examine-fuze-timer = It has a hand-set timer fuze.
wg-warhead-examine-fuze-two-person = It needs two keys turned together.
wg-warhead-examine-fuze-remote = It has a remote detonation port.
wg-warhead-examine-fuze-impact = It goes off on impact.

wg-warhead-class-tactical = TACTICAL / SUB-KILOTON
wg-warhead-class-field = FIELD / MULTI-KILOTON
wg-warhead-class-strategic = STRATEGIC / MEGATON
wg-warhead-class-scuttle = SCUTTLING CHARGE
wg-warhead-class-custom = CUSTOM PAYLOAD

wg-warhead-ui-status-safe = SAFETY ENGAGED
wg-warhead-ui-status-armed = SAFETY OFF - AWAITING SIGNAL
wg-warhead-ui-status-countdown = COUNTDOWN IN PROGRESS
wg-warhead-ui-status-final = DETONATION IMMINENT
wg-warhead-ui-lamp-safe = SAFE
wg-warhead-ui-lamp-armed = ARMED
wg-warhead-ui-lamp-countdown = COUNT
wg-warhead-ui-lamp-final = FINAL
wg-warhead-ui-caption-timer = TIMER SETTING
wg-warhead-ui-caption-counting = TIME TO DETONATION

wg-warhead-ui-timer-heading = TIMER FUZE
wg-warhead-ui-timer-minus-minute = -1 min
wg-warhead-ui-timer-minus-ten = -10 s
wg-warhead-ui-timer-plus-ten = +10 s
wg-warhead-ui-timer-plus-minute = +1 min
wg-warhead-ui-minutes = {$minutes} min
wg-warhead-ui-seconds = {$seconds} s
wg-warhead-ui-cover-closed = Lift safety cover
wg-warhead-ui-cover-open = Cover open
wg-warhead-ui-arm = START COUNTDOWN

wg-warhead-ui-keys-heading = TWO-PERSON ARMING
wg-warhead-ui-turn-key = Turn your key
wg-warhead-ui-key-off = OFF
wg-warhead-ui-key-turned = TURNED
wg-warhead-ui-key-turned-by = TURNED: {$name}
wg-warhead-ui-key-waiting = TURN NOW

wg-warhead-ui-remote-heading = REMOTE FUZE
wg-warhead-ui-remote-safe = Safety on. Signals are ignored.
wg-warhead-ui-remote-armed = Safety off. A signal starts the countdown.
wg-warhead-ui-remote-counting = Signal received.
wg-warhead-ui-remote-links = Linked detonators: {$links}. Countdown on signal: {$seconds} s.
wg-warhead-ui-safety-off = Take safety off
wg-warhead-ui-safety-on = Put safety on

wg-warhead-ui-impact-heading = IMPACT FUZE
wg-warhead-ui-impact-body = Goes off when it hits something.

wg-warhead-ui-specs-heading = PAYLOAD
wg-warhead-ui-spec-blast = Blast radius
wg-warhead-ui-spec-flash = Flash range
wg-warhead-ui-spec-fallout = Fallout
wg-warhead-ui-spec-emp = EMP range
wg-warhead-ui-spec-grid = Grid
wg-warhead-ui-spec-grid-value = DESTROYED OUTRIGHT
wg-warhead-ui-spec-tiles = ~{$tiles} tiles
wg-warhead-ui-spec-fallout-value = {$sources} sources over ~{$tiles} tiles
wg-warhead-ui-spec-none = none

wg-warhead-armed = Countdown started: {$seconds} seconds.
wg-warhead-key-denied = Your key doesn't fit.
wg-warhead-key-first = Key turned. The second key has {$window} seconds.
wg-warhead-safety-off = Safety off. It will go off on a signal.
wg-warhead-safety-on = Safety on.
wg-warhead-disarmed = Countdown stopped. Safety on.
wg-warhead-disarm-too-late = Too late. It can't be stopped now.
wg-warhead-wires-board = W-9 FUZING ASSEMBLY
wg-warhead-wire-name = FUZE
wg-warhead-wire-disarmed = The countdown dies. The warhead is safe.

wg-deadman-verb-grip = Grip the switch
wg-deadman-verb-release = Ease off the grip
wg-deadman-gripped = You squeeze the grip. Don't let go.
wg-deadman-released-safely = You ease off the grip. It's safe.
wg-deadman-examine-gripped = The grip is [color=red]squeezed[/color].
wg-deadman-examine-loose = The grip is loose.

wg-signal-port-name-warhead-detonate = Detonate
wg-signal-port-description-warhead-detonate = Starts the countdown if the safety is off.
wg-signal-port-name-deadman-released = Released
wg-signal-port-description-deadman-released = Fires when the grip is let go.

cmd-nukedesigner-desc = Opens the nuke designer.
cmd-nukedesigner-help = Usage: nukedesigner

wg-nuke-designer-button = Nuke Designer
wg-nuke-designer-title = Nuke designer
wg-nuke-designer-preset = Preset
wg-nuke-designer-chassis = Chassis
wg-nuke-designer-name = Name
wg-nuke-designer-timer = Timer (seconds)
wg-nuke-designer-fuzes = Fuzes
wg-nuke-designer-fuze-timer = Timer
wg-nuke-designer-fuze-two-person = Two keys
wg-nuke-designer-fuze-remote = Remote
wg-nuke-designer-fuze-impact = Impact
wg-nuke-designer-start-now = Start the countdown when spawned
wg-nuke-designer-payload = Payload
wg-nuke-designer-explosion-none = None
wg-nuke-designer-spawn = Spawn warhead here
wg-nuke-designer-detonate = Detonate here now

wg-nuke-designer-status-spawned = Spawned {$entity}.
wg-nuke-designer-status-detonated = Detonated.
wg-nuke-designer-status-bad-chassis = Pick a chassis.
wg-nuke-designer-status-bad-explosion = Unknown explosion type.
wg-nuke-designer-status-bad-fallout = Unknown fallout prototype.
wg-nuke-designer-status-bad-field = { $field } isn't a valid value.

wg-nuke-designer-field-explosion-type = Explosion type
wg-nuke-designer-field-total-intensity = Total intensity
wg-nuke-designer-field-total-intensity-tooltip = Overall size of the blast. The station nuke is 5,000,000.
wg-nuke-designer-field-intensity-slope = Intensity slope
wg-nuke-designer-field-intensity-slope-tooltip = How quickly the blast falls off from the centre. Lower is flatter and wider.
wg-nuke-designer-field-max-intensity = Max tile intensity
wg-nuke-designer-field-max-intensity-tooltip = The most damage any one tile takes.
wg-nuke-designer-field-tile-break-scale = Tile break scale
wg-nuke-designer-field-tile-break-scale-tooltip = Multiplier on how readily floor tiles break.
wg-nuke-designer-field-delete-grid = Delete grid (true/false)
wg-nuke-designer-field-delete-grid-tooltip = Deletes the whole grid it sits on before the blast. Never deletes a planet.
wg-nuke-designer-field-erase-radius = Erase radius
wg-nuke-designer-field-erase-radius-tooltip = Metres around ground zero where everything is deleted, floor and decals too. A planet keeps its ground.
wg-nuke-designer-field-prompt-radiation = Burst radiation
wg-nuke-designer-field-prompt-radiation-tooltip = Rads per second at the centre during the burst.
wg-nuke-designer-field-prompt-radiation-slope = Burst slope
wg-nuke-designer-field-prompt-radiation-slope-tooltip = How fast the burst falls off with distance. Lower reaches further.
wg-nuke-designer-field-prompt-radiation-duration = Burst seconds
wg-nuke-designer-field-prompt-radiation-duration-tooltip = How long the burst lasts.
wg-nuke-designer-field-fallout-prototype = Fallout prototype
wg-nuke-designer-field-fallout-prototype-tooltip = Entity spawned for each fallout source.
wg-nuke-designer-field-fallout-count = Fallout sources
wg-nuke-designer-field-fallout-count-tooltip = Extra sources scattered around ground zero, besides the one at the centre.
wg-nuke-designer-field-fallout-spread = Fallout spread
wg-nuke-designer-field-fallout-spread-tooltip = Tiles the scattered fallout lands within.
wg-nuke-designer-field-fallout-intensity = Fallout radiation
wg-nuke-designer-field-fallout-intensity-tooltip = Rads per second of each source when fresh. Zero leaves no fallout.
wg-nuke-designer-field-fallout-half-life = Fallout half-life
wg-nuke-designer-field-fallout-half-life-tooltip = Seconds for fallout to lose half its strength.
wg-nuke-designer-field-fallout-radius = Fallout radius
wg-nuke-designer-field-fallout-radius-tooltip = Tiles each source's haze and radiation reach.
wg-nuke-designer-field-emp-range = EMP range
wg-nuke-designer-field-emp-range-tooltip = Tiles the EMP reaches. Zero for none.
wg-nuke-designer-field-emp-energy = EMP energy
wg-nuke-designer-field-emp-energy-tooltip = Energy drained from batteries in range.
wg-nuke-designer-field-emp-duration = EMP seconds
wg-nuke-designer-field-emp-duration-tooltip = How long electronics stay disabled.
wg-nuke-designer-field-flash-range = Flash range
wg-nuke-designer-field-flash-range-tooltip = Tiles within which the flash whites out the screen.
wg-nuke-designer-field-fireball-radius = Fireball radius
wg-nuke-designer-field-fireball-radius-tooltip = Size of the fireball, and of the mushroom cloud on a planet.
wg-nuke-designer-field-shockwave-range = Shockwave range
wg-nuke-designer-field-shockwave-range-tooltip = Tiles the shockwave ring travels.
wg-nuke-designer-field-shockwave-speed = Shockwave speed
wg-nuke-designer-field-shockwave-speed-tooltip = Tiles per second.
wg-nuke-designer-field-shake-strength = Shake strength
wg-nuke-designer-field-shake-strength-tooltip = How hard the camera shakes as the shockwave passes.
wg-nuke-designer-field-effect-duration = Cloud seconds
wg-nuke-designer-field-effect-duration-tooltip = How long the fireball or mushroom cloud lasts.
wg-nuke-designer-field-map-wide-audio = Map-wide alarm (true/false)
wg-nuke-designer-field-map-wide-audio-tooltip = Plays the final alarm and countdown voice to the whole map rather than nearby.
wg-nuke-designer-field-detonation-sound = Detonation sound
wg-nuke-designer-field-detonation-sound-tooltip = Sound played to the whole map. Leave empty for none.

wg-warhead-key-missing = It won't arm. Its key isn't in the keyhole.
wg-warhead-examine-key-in = Its key is in the keyhole.
wg-warhead-examine-key-out = It has an empty keyhole. It won't arm without its key.
