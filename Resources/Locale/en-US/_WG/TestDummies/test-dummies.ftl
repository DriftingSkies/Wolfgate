# Wolfgate admin: ask players to be test subjects

## testdummies command
cmd-testdummies-desc = Opens the test dummy window, which asks players whether they will be tested on and lists who said yes. "testdummies list" prints the list.
cmd-testdummies-help = Usage: testdummies [list]
cmd-testdummies-hint = [list]

## Admin menu
wg-test-dummy-button = Test Dummies

## Admin window
wg-test-dummy-title = Test Dummies
wg-test-dummy-section-request = Request
wg-test-dummy-description-placeholder = What you want to test (optional, shown to players)
wg-test-dummy-duration = Prompt stays for
wg-test-dummy-duration-hint = seconds (0 = until answered)
wg-test-dummy-section-audience = Ask players who...
wg-test-dummy-audience-owns-ship = Own a ship
wg-test-dummy-audience-in-space = Are in space (not on a planet, in FTL, on an expedition or in a vehicle)
wg-test-dummy-audience-in-body = Are playing a body (not ghosts or in the lobby)
wg-test-dummy-audience-skip-admins = Are not admins
wg-test-dummy-send = Ask
wg-test-dummy-end = End call
wg-test-dummy-section-list = Answers
wg-test-dummy-summary = {$in} opted in, {$out} opted out, {$pending} {$active ->
    [true] still deciding
   *[false] never answered
}
wg-test-dummy-list-empty = Nobody has been asked yet.
wg-test-dummy-row-as = as {$character}
wg-test-dummy-row-ship = ({$ship})
wg-test-dummy-row-disconnected = - disconnected

wg-test-dummy-answer-pending = No answer
wg-test-dummy-answer-opted-in = Opted in
wg-test-dummy-answer-opted-out = Opted out

## Status line and console list
wg-test-dummy-status-sent = Asked {$count} {$count ->
    [one] player
   *[other] players
}.
wg-test-dummy-status-ended = Call ended.
wg-test-dummy-error-too-long = The description is too long.
wg-test-dummy-error-bad-duration = The duration must be between 0 and 1800 seconds.
wg-test-dummy-error-nobody = No connected player matches that audience.
wg-test-dummy-list-line = [{$answer}] {$entry}

## Admin chat
wg-test-dummy-opted-in = {$player} opted in as a test dummy.
wg-test-dummy-opted-out = {$player} opted out of being a test dummy.

## Player prompt
wg-test-dummy-chip-title = Test subjects wanted
wg-test-dummy-chip-countdown = {$seconds}s
wg-test-dummy-chip-text = An admin wants to test something on willing players: [bold]{$description}[/bold]. Are you in?
wg-test-dummy-chip-text-plain = An admin wants to test something on willing players. Are you in?
wg-test-dummy-chip-disclaimer = [color=#c08a2a]Things may break, die or vanish. No refunds or compensation will be given.[/color]
wg-test-dummy-chip-yes = Yes, test on me
wg-test-dummy-chip-no = No thanks
