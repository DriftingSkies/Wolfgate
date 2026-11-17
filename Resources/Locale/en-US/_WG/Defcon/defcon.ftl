wg-defcon-sender = Sector Defense Command
wg-defcon-title = DEFCON {$level}: {$name}
wg-defcon-chip-title = DEFCON {$level}
wg-defcon-pda = DEFCON: [color={$color}]{$level}[/color]
wg-defcon-body-rising = The sector is now at DEFCON {$level}, {$name}.
wg-defcon-body-falling = The sector has stood down to DEFCON {$level}, {$name}.
wg-defcon-body-reasons = Reported threats:
wg-defcon-body-reason = - {$reason}

wg-defcon-name-5 = FADE OUT
wg-defcon-name-4 = DOUBLE TAKE
wg-defcon-name-3 = ROUND HOUSE
wg-defcon-name-2 = FAST PACE
wg-defcon-name-1 = COCKED PISTOL

wg-defcon-desc-5 = No known threats. Normal traffic.
wg-defcon-desc-4 = Hostile activity reported. Keep watch.
wg-defcon-desc-3 = Sustained hostile activity. Arm up and fly in company.
wg-defcon-desc-2 = The sector is under attack. Civilians keep close to stations.
wg-defcon-desc-1 = The sector is overrun. All crews to battle stations.

wg-defcon-reason-ads = ADS incursion at {$tier}
wg-defcon-reason-reaver = Reaver campaign at {$tier}
wg-defcon-reason-war = Open war between the TSF and the PDV
wg-defcon-reason-threats = {$count ->
    [one] One hostile contact in the sector
   *[other] {$count} hostile contacts in the sector
}
wg-defcon-reason-distress = {$count ->
    [one] One open distress call
   *[other] {$count} open distress calls
}
wg-defcon-reason-deaths = {$count ->
    [one] One death reported in the last few minutes
   *[other] {$count} deaths reported in the last few minutes
}
wg-defcon-reason-bias = Sector Command assessment

cmd-wg_defcon-desc = Shows and drives the sector DEFCON.
cmd-wg_defcon-help = Usage: {$command} status | set <1-5> | lock <1-5> | unlock | bias <score> | eval
    set moves the level now and lets it follow the threat again; lock holds it until unlock; bias adds a score on top of the sources; eval scores the threat now.
cmd-wg_defcon-hint-sub = Subcommand
cmd-wg_defcon-hint-level = Level, 1 (severest) to 5
cmd-wg_defcon-hint-bias = Score to add
cmd-wg_defcon-bad-level = The level must be 1 to 5.
cmd-wg_defcon-bad-bias = The bias must be a number.
cmd-wg_defcon-set = DEFCON set to {$level}.
cmd-wg_defcon-locked = DEFCON locked at {$level}.
cmd-wg_defcon-unlocked = DEFCON follows the threat again.
cmd-wg_defcon-biased = DEFCON bias set to {$bias}.
cmd-wg_defcon-status = DEFCON {$level} ({$name}), score {$score}, floor {$floor}{$locked ->
    [true] , locked
   *[false] {""}
}
cmd-wg_defcon-status-line = {"  "}{$score}: {$reason}

cmd-wg_defconpanel-desc = Opens the sector DEFCON panel.
cmd-wg_defconpanel-help = Usage: {$command}

wg-defcon-panel-button = Sector DEFCON
wg-defcon-panel-title = Sector DEFCON
wg-defcon-panel-level = [font size=20][color={$color}]DEFCON {$level}: {$name}[/color][/font]
wg-defcon-panel-status = Score {$score}, floor DEFCON {$floor}{$locked ->
    [true] , locked
   *[false] {""}
}
wg-defcon-panel-disabled = DEFCON is off (wg.defcon.enabled).
wg-defcon-panel-set = Set level
wg-defcon-panel-level-button = {$level}
wg-defcon-panel-lock = Lock
wg-defcon-panel-lock-tooltip = Hold the level until unlocked, whatever the threat.
wg-defcon-panel-announce = Announce
wg-defcon-panel-unlock = Unlock
wg-defcon-panel-threat = Threat
wg-defcon-panel-bias = Bias
wg-defcon-panel-bias-tooltip = Score added on top of the sources. Negative calms the sector.
wg-defcon-panel-bias-apply = Apply
wg-defcon-panel-evaluate = Score now
wg-defcon-panel-no-threats = No threats reported.
wg-defcon-panel-reason = {$score}: {$reason}
