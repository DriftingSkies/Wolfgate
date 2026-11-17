tiles-wg-asteroid-core = asteroid core

wg-asteroid-examine-scan = A mining laser on it will show its contents on the gunnery console.
wg-asteroid-name-metallic = metallic {$name}
wg-asteroid-examine-metallic = Bare metal shows through the rock. It takes a lot of heat to crack.

wg-mining-gunnery-title = MINING LASER
wg-mining-gunnery-power = Power {$power}%
wg-mining-gunnery-power-help = Sets every mining laser on this ship. Keep the rock's heat in the green band. Yellow wastes ore, red blows it up.
wg-mining-gunnery-power-up = +{$amount}
wg-mining-gunnery-power-down = -{$amount}
wg-mining-gunnery-lasers = {$count ->
    [one] 1 laser
    *[other] {$count} lasers
}
wg-mining-gunnery-cooldown = Overloaded: {$seconds} s
wg-mining-gunnery-idle = Fire on a rock to scan it.
wg-mining-gunnery-idle-help = Fire a mining laser at an asteroid to scan it and track its heat.
wg-mining-gunnery-rock = {CAPITALIZE($name)}, {$mass} veins
wg-mining-gunnery-ore = {CAPITALIZE($name)} {$percent}%
wg-mining-gunnery-inert = Plain rock {$percent}%
wg-mining-gunnery-resistance = Resistance {$percent}%
wg-mining-gunnery-heat = Heat {$charge}%, window {$low}-{$high}%
wg-mining-gunnery-meter-help = The needle is the rock's heat. Keep it in the green band to crack the rock. Heat lags behind the power, so adjust in small steps.
wg-mining-gunnery-progress = Crack progress
wg-mining-gunnery-yield = Yield {$percent}%
wg-mining-gunnery-yield-help = How much of the rock's ore you get when it cracks. The yellow band cracks faster but wastes ore.
wg-mining-gunnery-overcharge = OVERCHARGING!
wg-mining-gunnery-overcharge-help = Too hot. Lower the power or stop firing before it blows.

wg-cockpit-mining = MINING
wg-cockpit-mining-short = MIN
wg-mining-page-target = TARGET
wg-mining-page-rock = {CAPITALIZE($name)}
wg-mining-page-hint = Point at a rock on the ship cam or NAV to scan it.
wg-mining-page-hint-help = Rocks in view range scan fully. Farther ones only show a signal until you get closer.
wg-mining-lever-caption = POWER
wg-mining-lever-help = Drag or scroll to set every mining laser on this ship. Keep the rock's heat in the green band. Yellow wastes ore, red blows it up.
wg-mining-dial-heat = HEAT
wg-mining-dial-unit = %
wg-mining-dial-empty = --
wg-mining-dial-idle = NO TARGET
wg-mining-dial-overcharge = OVERCHARGE
wg-mining-figure-window = WINDOW
wg-mining-figure-window-range = {$low}-{$high}
wg-mining-figure-crack = CRACK
wg-mining-figure-yield = YIELD
wg-mining-figures-help = WINDOW: the heat that cracks the rock cleanly. CRACK: how far the rock is to breaking apart. YIELD: how much of its ore you get when it cracks.
wg-mining-lamp-window = WINDOW
wg-mining-lamp-hot = HOT
wg-mining-lamp-overcharge = OVERCHARGE
wg-mining-lamp-overload = OVERLOAD
wg-mining-lamps-help = WINDOW: cracking cleanly. HOT: faster, but wastes ore. OVERCHARGE: about to blow. OVERLOAD: lasers down after a blast.

wg-mining-scan-tag = SCAN
wg-mining-scan-tag-far = SIGNAL
wg-mining-scan-range = Range {$range} m
wg-mining-scan-size = Ø {$size} m
wg-mining-scan-far = Out of scan range
wg-mining-scan-veins = {$mass} veins
wg-mining-scan-ore = {CAPITALIZE($name)}
wg-mining-scan-inert = Plain rock
wg-mining-scan-percent = {$percent}%
wg-mining-scan-resistance = Resistance
wg-mining-scan-window = Window
wg-mining-scan-window-range = {$low}-{$high}%
wg-mining-scan-stability = Heat drift
wg-mining-scan-stability-band = {$band ->
    [steady] Steady
    [drifting] Drifting
   *[erratic] Erratic
}
wg-mining-scan-blast = Overcharge blast
wg-mining-scan-blast-band = {$band ->
    [low] Low
    [high] High
   *[severe] Severe
}
wg-mining-scan-heat = Heat
wg-mining-scan-crack = Crack
wg-mining-scan-overcharge = OVERCHARGING
