# Wolfgate admin: ask players to be test subjects

## testdummies command
cmd-testdummies-desc = Opens the test dummy window, which asks players whether they will be tested on and lists who said yes. "testdummies list" prints the list.
cmd-testdummies-help = Usage: testdummies [list]
cmd-testdummies-hint = [list]

## Admin menu
wf-test-dummy-button = Test Dummies

## Admin window
wf-test-dummy-title = Test Dummies
wf-test-dummy-section-request = Request
wf-test-dummy-description-placeholder = What you want to test (optional, shown to players)
wf-test-dummy-duration = Prompt stays for
wf-test-dummy-duration-hint = seconds (0 = until answered)
wf-test-dummy-section-audience = Ask players who...
wf-test-dummy-audience-owns-ship = Own a ship
wf-test-dummy-audience-in-space = Are in space (not on a planet, in FTL, on an expedition or in a vehicle)
wf-test-dummy-audience-in-body = Are playing a body (not ghosts or in the lobby)
wf-test-dummy-audience-skip-admins = Are not admins
wf-test-dummy-send = Ask
wf-test-dummy-end = End call
wf-test-dummy-section-list = Answers
wf-test-dummy-summary = {$in} opted in, {$out} opted out, {$pending} {$active ->
    [true] still deciding
   *[false] never answered
}
wf-test-dummy-list-empty = Nobody has been asked yet.
wf-test-dummy-row-as = as {$character}
wf-test-dummy-row-ship = ({$ship})
wf-test-dummy-row-disconnected = - disconnected

wf-test-dummy-answer-pending = No answer
wf-test-dummy-answer-opted-in = Opted in
wf-test-dummy-answer-opted-out = Opted out

## Status line and console list
wf-test-dummy-status-sent = Asked {$count} {$count ->
    [one] player
   *[other] players
}.
wf-test-dummy-status-ended = Call ended.
wf-test-dummy-error-too-long = The description is too long.
wf-test-dummy-error-bad-duration = The duration must be between 0 and 1800 seconds.
wf-test-dummy-error-nobody = No connected player matches that audience.
wf-test-dummy-list-line = [{$answer}] {$entry}

## Admin chat
wf-test-dummy-opted-in = {$player} opted in as a test dummy.
wf-test-dummy-opted-out = {$player} opted out of being a test dummy.

## Player prompt
wf-test-dummy-chip-title = Test subjects wanted
wf-test-dummy-chip-countdown = {$seconds}s
wf-test-dummy-chip-text = An admin wants to test something on willing players: [bold]{$description}[/bold]. Are you in?
wf-test-dummy-chip-text-plain = An admin wants to test something on willing players. Are you in?
wf-test-dummy-chip-disclaimer = [color=#c08a2a]Things may break, die or vanish. No refunds or compensation will be given.[/color]
wf-test-dummy-chip-yes = Yes, test on me
wf-test-dummy-chip-no = No thanks
