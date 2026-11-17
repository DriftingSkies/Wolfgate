## The Bench app and terminal

wg-bench-program-name = Bench
wg-bench-terminal-title = Bench Terminal
wg-bench-stamp = Bench of Caelestinus

wg-bench-tab-ticket = Ticket
wg-bench-tab-ledger = Ledger
wg-bench-tab-laws = Laws

wg-bench-field-offender = Offender
wg-bench-field-ship = Vessel
wg-bench-field-ship-placeholder = Optional
wg-bench-field-notes = Notes
wg-bench-field-notes-placeholder = Optional
wg-bench-no-one-near = No one close enough
wg-bench-heading-ordinances = Posted ordinances
wg-bench-heading-custom = Other charges
wg-bench-none = None.
wg-bench-custom-title = Charge
wg-bench-custom-fine = Fine
wg-bench-custom-add = Add
wg-bench-remove = Remove
wg-bench-law-range = { $level }, { $min }-{ $max }
wg-bench-charge-line = { $title } ({ $level }): { $fine }
wg-bench-summary-empty = Tick the charges.
wg-bench-summary = { $count ->
    [one] 1 charge
   *[other] { $count } charges
}: { $total } thlr, { $level }
wg-bench-summary-capped = {" "}(capped)
wg-bench-issue = Issue ticket
wg-bench-issued = Ticket #{ $number } issued.

wg-bench-ledger-empty = No tickets this round.
wg-bench-ledger-pick = Pick a ticket.
wg-bench-ledger-row = #{ $number } { $name }: { $total } thlr, { $status }{ $owed ->
    [0] {""}
   *[other] , { $owed } owed
}
wg-bench-ledger-head = [bold]Ticket #{ $number }[/bold], { $name }, by { $judge }
wg-bench-ledger-ship = Vessel: { $ship }
wg-bench-ledger-money = Fine { $total }, taken { $paid }, owed { $owed }. { $status }.
wg-bench-ledger-deadline = Wanted in { $minutes } min if still owed.
wg-bench-ledger-notes = Notes: { $notes }
wg-bench-pardon = Pardon
wg-bench-pardon-confirm = Confirm pardon
wg-bench-pardon-done = Ticket #{ $number } pardoned.

wg-bench-status-paid = Paid
wg-bench-status-owing = Owing
wg-bench-status-overdue = Overdue
wg-bench-status-outlawed = Outlawed
wg-bench-status-pardoned = Pardoned

wg-bench-laws-preamble = [bold]The Common Writ of the Bench[/bold]
wg-bench-laws-preamble-text = [italic]By the hand of the Bench of Caelestinus. Binding on every soul within the anchors and beyond them. Ignorance of the Writ is no defence.[/italic]
wg-bench-law-entry = [bold]{ $name }[/bold] ({ $penalty }): { $text }
wg-bench-ordinance-entry = [bold]{ $name }[/bold] ({ $penalty }): { $text } [color=gray]Posted by { $by }.[/color]
wg-bench-penalty-fine = { $level }, { $fine }
wg-bench-post-heading = Post an ordinance
wg-bench-post-title = Title
wg-bench-post-text = What it forbids
wg-bench-post = Post

wg-bench-error-not-judge = Only the Bench can do that.
wg-bench-error-range = They need to be right in front of you.
wg-bench-error-self = You can't ticket yourself.
wg-bench-error-no-account = They have no account to fine.
wg-bench-error-no-charges = Tick at least one charge.

## The ticket and what the offender is told

wg-bench-ticket-head = [head=2]Bench of Caelestinus[/head]
wg-bench-ticket-number = [bold]Ticket #{ $number }[/bold]
wg-bench-ticket-offender = Offender: { $name }
wg-bench-ticket-ship = Vessel: { $ship }
wg-bench-ticket-judge = Judge: { $name }, at { $time }
wg-bench-ticket-charge = - { $title } ({ $level }): { $fine } thlr
wg-bench-ticket-total = [bold]Fine: { $total } thlr. Penalty: { $level }.[/bold]
wg-bench-ticket-capped = The charges add up to { $uncapped } thlr. The Bench's limit applies.
wg-bench-ticket-paid = Taken from your account: { $paid } thlr. Owed: { $owed } thlr.
wg-bench-ticket-debt = What you owe is taken from your earnings. Still owed after { $minutes } minutes, or over the Bench's limit, you are Wanted.
wg-bench-ticket-outlawry = You are outlawed. Any citizen may bring you before the Bench.
wg-bench-ticket-notes = Notes: { $notes }

wg-bench-notify = Bench ticket #{ $number }: { $paid } thlr taken from your account, { $owed } owed.
wg-bench-notify-outlawry = Bench ticket #{ $number }: you are outlawed.
wg-bench-garnished = { $amount } thlr of your earnings went to your Bench debt. { $owed } still owed.
wg-bench-now-wanted = The Bench has marked you Wanted.
wg-bench-pardoned = Bench ticket #{ $number } pardoned. { $amount } thlr returned to you.

wg-bench-history-ticket = Bench ticket #{ $number }: { $charges }. Fine { $total }, { $paid } taken.
wg-bench-history-pardon = Bench ticket #{ $number } pardoned.
wg-bench-wanted-debt = Owes the Bench { $owed } thlr.
wg-bench-wanted-outlaw = Outlawed by Bench ticket #{ $number }.

wg-bench-warrant-author = Bench of Caelestinus
wg-bench-warrant-description = { $name } is outlawed by the Bench: { $charges }.
wg-bench-warrant-paid = Bench warrant on { $name } claimed. { $amount } thlr paid to you.

guide-entry-wg-common-writ = The Common Writ
