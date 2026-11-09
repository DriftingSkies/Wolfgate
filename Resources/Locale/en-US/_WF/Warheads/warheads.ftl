wf-warhead-examine-state-safe = The safety is [color=green]on[/color].
wf-warhead-examine-state-armed = The safety is [color=red]off[/color].
wf-warhead-examine-state-countdown = It is [color=red]counting down[/color].
wf-warhead-examine-state-final = It is [color=red]about to go off[/color].
wf-warhead-examine-time = {$seconds} seconds left.
wf-warhead-examine-timer = The timer is set to {$seconds} seconds.
wf-warhead-examine-fuze-timer = It has a hand-set timer fuze.
wf-warhead-examine-fuze-two-person = It needs two keys turned together.
wf-warhead-examine-fuze-remote = It has a remote detonation port.
wf-warhead-examine-fuze-impact = It goes off on impact.

wf-warhead-class-tactical = TACTICAL / SUB-KILOTON
wf-warhead-class-field = FIELD / MULTI-KILOTON
wf-warhead-class-strategic = STRATEGIC / MEGATON
wf-warhead-class-scuttle = SCUTTLING CHARGE
wf-warhead-class-custom = CUSTOM PAYLOAD

wf-warhead-ui-status-safe = SAFETY ENGAGED
wf-warhead-ui-status-armed = SAFETY OFF - AWAITING SIGNAL
wf-warhead-ui-status-countdown = COUNTDOWN IN PROGRESS
wf-warhead-ui-status-final = DETONATION IMMINENT
wf-warhead-ui-lamp-safe = SAFE
wf-warhead-ui-lamp-armed = ARMED
wf-warhead-ui-lamp-countdown = COUNT
wf-warhead-ui-lamp-final = FINAL
wf-warhead-ui-caption-timer = TIMER SETTING
wf-warhead-ui-caption-counting = TIME TO DETONATION

wf-warhead-ui-timer-heading = TIMER FUZE
wf-warhead-ui-timer-minus-minute = -1 min
wf-warhead-ui-timer-minus-ten = -10 s
wf-warhead-ui-timer-plus-ten = +10 s
wf-warhead-ui-timer-plus-minute = +1 min
wf-warhead-ui-minutes = {$minutes} min
wf-warhead-ui-seconds = {$seconds} s
wf-warhead-ui-cover-closed = Lift safety cover
wf-warhead-ui-cover-open = Cover open
wf-warhead-ui-arm = START COUNTDOWN

wf-warhead-ui-keys-heading = TWO-PERSON ARMING
wf-warhead-ui-turn-key = Turn your key
wf-warhead-ui-key-off = OFF
wf-warhead-ui-key-turned = TURNED
wf-warhead-ui-key-turned-by = TURNED: {$name}
wf-warhead-ui-key-waiting = TURN NOW

wf-warhead-ui-remote-heading = REMOTE FUZE
wf-warhead-ui-remote-safe = Safety on. Signals are ignored.
wf-warhead-ui-remote-armed = Safety off. A signal starts the countdown.
wf-warhead-ui-remote-counting = Signal received.
wf-warhead-ui-remote-links = Linked detonators: {$links}. Countdown on signal: {$seconds} s.
wf-warhead-ui-safety-off = Take safety off
wf-warhead-ui-safety-on = Put safety on

wf-warhead-ui-impact-heading = IMPACT FUZE
wf-warhead-ui-impact-body = Goes off when it hits something.

wf-warhead-ui-specs-heading = PAYLOAD
wf-warhead-ui-spec-blast = Blast radius
wf-warhead-ui-spec-flash = Flash range
wf-warhead-ui-spec-fallout = Fallout
wf-warhead-ui-spec-emp = EMP range
wf-warhead-ui-spec-grid = Grid
wf-warhead-ui-spec-grid-value = DESTROYED OUTRIGHT
wf-warhead-ui-spec-tiles = ~{$tiles} tiles
wf-warhead-ui-spec-fallout-value = {$sources} sources over ~{$tiles} tiles
wf-warhead-ui-spec-none = none
wf-warhead-ui-footer = To abort, use a multitool on the device. Not possible in the last 10 seconds.

wf-warhead-armed = Countdown started: {$seconds} seconds.
wf-warhead-key-denied = Your key doesn't fit.
wf-warhead-key-first = Key turned. The second key has {$window} seconds.
wf-warhead-safety-off = Safety off. It will go off on a signal.
wf-warhead-safety-on = Safety on.
wf-warhead-disarmed = Countdown stopped. Safety on.
wf-warhead-disarm-too-late = Too late. It can't be stopped now.

wf-deadman-verb-grip = Grip the switch
wf-deadman-verb-release = Ease off the grip
wf-deadman-gripped = You squeeze the grip. Don't let go.
wf-deadman-released-safely = You ease off the grip. It's safe.
wf-deadman-examine-gripped = The grip is [color=red]squeezed[/color].
wf-deadman-examine-loose = The grip is loose.

wf-signal-port-name-warhead-detonate = Detonate
wf-signal-port-description-warhead-detonate = Starts the countdown if the safety is off.
wf-signal-port-name-deadman-released = Released
wf-signal-port-description-deadman-released = Fires when the grip is let go.

cmd-nukedesigner-desc = Opens the nuke designer.
cmd-nukedesigner-help = Usage: nukedesigner

wf-nuke-designer-button = Nuke Designer
wf-nuke-designer-title = Nuke designer
wf-nuke-designer-preset = Preset
wf-nuke-designer-chassis = Chassis
wf-nuke-designer-name = Name
wf-nuke-designer-timer = Timer (seconds)
wf-nuke-designer-fuzes = Fuzes
wf-nuke-designer-fuze-timer = Timer
wf-nuke-designer-fuze-two-person = Two keys
wf-nuke-designer-fuze-remote = Remote
wf-nuke-designer-fuze-impact = Impact
wf-nuke-designer-start-now = Start the countdown when spawned
wf-nuke-designer-payload = Payload
wf-nuke-designer-explosion-none = None
wf-nuke-designer-spawn = Spawn warhead here
wf-nuke-designer-detonate = Detonate here now

wf-nuke-designer-status-spawned = Spawned {$entity}.
wf-nuke-designer-status-detonated = Detonated.
wf-nuke-designer-status-bad-chassis = Pick a chassis.
wf-nuke-designer-status-bad-explosion = Unknown explosion type.
wf-nuke-designer-status-bad-fallout = Unknown fallout prototype.
wf-nuke-designer-status-bad-field = { $field } isn't a valid value.

wf-nuke-designer-field-explosion-type = Explosion type
wf-nuke-designer-field-total-intensity = Total intensity
wf-nuke-designer-field-total-intensity-tooltip = Overall size of the blast. The station nuke is 5,000,000.
wf-nuke-designer-field-intensity-slope = Intensity slope
wf-nuke-designer-field-intensity-slope-tooltip = How quickly the blast falls off from the centre. Lower is flatter and wider.
wf-nuke-designer-field-max-intensity = Max tile intensity
wf-nuke-designer-field-max-intensity-tooltip = The most damage any one tile takes.
wf-nuke-designer-field-tile-break-scale = Tile break scale
wf-nuke-designer-field-tile-break-scale-tooltip = Multiplier on how readily floor tiles break.
wf-nuke-designer-field-delete-grid = Delete grid (true/false)
wf-nuke-designer-field-delete-grid-tooltip = Deletes the whole grid it sits on before the blast. Never deletes a planet.
wf-nuke-designer-field-erase-radius = Erase radius
wf-nuke-designer-field-erase-radius-tooltip = Metres around ground zero where everything is deleted, floor and decals too. A planet keeps its ground.
wf-nuke-designer-field-prompt-radiation = Burst radiation
wf-nuke-designer-field-prompt-radiation-tooltip = Rads per second at the centre during the burst.
wf-nuke-designer-field-prompt-radiation-slope = Burst slope
wf-nuke-designer-field-prompt-radiation-slope-tooltip = How fast the burst falls off with distance. Lower reaches further.
wf-nuke-designer-field-prompt-radiation-duration = Burst seconds
wf-nuke-designer-field-prompt-radiation-duration-tooltip = How long the burst lasts.
wf-nuke-designer-field-fallout-prototype = Fallout prototype
wf-nuke-designer-field-fallout-prototype-tooltip = Entity spawned for each fallout source.
wf-nuke-designer-field-fallout-count = Fallout sources
wf-nuke-designer-field-fallout-count-tooltip = Extra sources scattered around ground zero, besides the one at the centre.
wf-nuke-designer-field-fallout-spread = Fallout spread
wf-nuke-designer-field-fallout-spread-tooltip = Tiles the scattered fallout lands within.
wf-nuke-designer-field-fallout-intensity = Fallout radiation
wf-nuke-designer-field-fallout-intensity-tooltip = Rads per second of each source when fresh. Zero leaves no fallout.
wf-nuke-designer-field-fallout-half-life = Fallout half-life
wf-nuke-designer-field-fallout-half-life-tooltip = Seconds for fallout to lose half its strength.
wf-nuke-designer-field-fallout-radius = Fallout radius
wf-nuke-designer-field-fallout-radius-tooltip = Tiles each source's haze and radiation reach.
wf-nuke-designer-field-emp-range = EMP range
wf-nuke-designer-field-emp-range-tooltip = Tiles the EMP reaches. Zero for none.
wf-nuke-designer-field-emp-energy = EMP energy
wf-nuke-designer-field-emp-energy-tooltip = Energy drained from batteries in range.
wf-nuke-designer-field-emp-duration = EMP seconds
wf-nuke-designer-field-emp-duration-tooltip = How long electronics stay disabled.
wf-nuke-designer-field-flash-range = Flash range
wf-nuke-designer-field-flash-range-tooltip = Tiles within which the flash whites out the screen.
wf-nuke-designer-field-fireball-radius = Fireball radius
wf-nuke-designer-field-fireball-radius-tooltip = Size of the fireball, and of the mushroom cloud on a planet.
wf-nuke-designer-field-shockwave-range = Shockwave range
wf-nuke-designer-field-shockwave-range-tooltip = Tiles the shockwave ring travels.
wf-nuke-designer-field-shockwave-speed = Shockwave speed
wf-nuke-designer-field-shockwave-speed-tooltip = Tiles per second.
wf-nuke-designer-field-shake-strength = Shake strength
wf-nuke-designer-field-shake-strength-tooltip = How hard the camera shakes as the shockwave passes.
wf-nuke-designer-field-effect-duration = Cloud seconds
wf-nuke-designer-field-effect-duration-tooltip = How long the fireball or mushroom cloud lasts.
wf-nuke-designer-field-map-wide-audio = Map-wide alarm (true/false)
wf-nuke-designer-field-map-wide-audio-tooltip = Plays the final alarm and countdown voice to the whole map rather than nearby.
wf-nuke-designer-field-detonation-sound = Detonation sound
wf-nuke-designer-field-detonation-sound-tooltip = Sound played to the whole map. Leave empty for none.

wf-warhead-key-missing = It won't arm. Its key isn't in the keyhole.
wf-warhead-examine-key-in = Its key is in the keyhole.
wf-warhead-examine-key-out = It has an empty keyhole. It won't arm without its key.
