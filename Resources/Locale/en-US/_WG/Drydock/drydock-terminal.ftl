drydock-port-name-berth-sender = Drydock berth link
drydock-port-description-berth-sender = Links the berth consoles this terminal works.
drydock-port-name-berth-receiver = Drydock terminal link
drydock-port-description-berth-receiver = Links the drydock terminal that works this berth.

drydock-console-locked = This berth is locked. It is worked from the drydock terminal.
drydock-console-locked-notice = Berth locked: worked from the drydock terminal.

drydock-terminal-title = Drydock terminal
drydock-terminal-berth-title = Berth { $berth }
drydock-terminal-header = { $count ->
    [0] No berths linked
    [one] 1 berth linked
   *[other] { $count } berths linked
}
drydock-terminal-refresh = Refresh
drydock-terminal-no-berths = Link berth consoles to this terminal with a multitool.
drydock-terminal-open = Open
drydock-terminal-locked = Locked
drydock-terminal-lock = Lock
drydock-terminal-unlock = Unlock
drydock-terminal-all-berths = All berths
drydock-terminal-lock-all = Lock all
drydock-terminal-unlock-all = Unlock all
drydock-terminal-price-now = Drydock price: x{ $multiplier } the usual price
drydock-terminal-price-usual = Drydock price: the usual price
drydock-terminal-price-placeholder = x1.0
drydock-terminal-price-set = Set
drydock-terminal-price-clear = Usual price
drydock-terminal-footer = Work started here is paid for as at the berth console. Locked berths take no walk-in work or bookings.
drydock-terminal-footer-commanded = Work started here is paid for at the ship's prices. Locked berths take no walk-in work or bookings.
drydock-terminal-footer-not-cleared = You aren't cleared to work this ship's drydock.

drydock-terminal-status-no-cranes = No cranes linked
drydock-terminal-status-empty = Empty
drydock-terminal-status-berthed = { $ship }: { $work } to repair
drydock-terminal-status-sound = { $ship }: nothing to repair
drydock-terminal-status-working = { $ship }: repairing, { $percent }%, about { $minutes } min left
drydock-terminal-status-breaking = { $ship }: breaking, { $percent }%, about { $minutes } min left
drydock-terminal-unknown-ship = Unknown ship

drydock-terminal-not-cleared = You aren't cleared to do that from this terminal.
drydock-terminal-no-berth = That berth isn't linked to this terminal.
drydock-terminal-bad-price = Set a price multiplier from 0 to { $max }, aboard a commanded ship.
