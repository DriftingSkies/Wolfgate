## Health analyzer Conditions tab: drug levels, held effects, crashes and dependence, with what clears each.

wg-analyzer-conditions-tab = Conditions
wg-analyzer-conditions-unavailable = Conditions unavailable.
wg-analyzer-conditions-none = Nothing active.
wg-analyzer-time = { $minutes }:{ $seconds }

wg-analyzer-drugs-title = Drug levels
wg-analyzer-drug = { $family }: { $phase }
wg-analyzer-phase-none = trace, no effect
wg-analyzer-phase-light = light
wg-analyzer-phase-heavy = heavy
wg-analyzer-phase-overdose = OVERDOSE
wg-analyzer-drug-no-cause = Nothing in the blood is raising it; it fades on its own.
wg-analyzer-antidote = Brought down by { $reagents }.

wg-analyzer-cause = { $reagent }, { $quantity }u: clears in about { $time }
wg-analyzer-cause-stalled = { $reagent }, { $quantity }u: not clearing
wg-analyzer-cause-stomach = { $reagent }, { $quantity }u in the stomach
wg-analyzer-removed-by = Removed faster by { $reagents }.

wg-analyzer-held-title = Held effects
wg-analyzer-held = { $effect }: { $time } left
wg-analyzer-held-fading = Nothing is holding it; it wears off on its own.
wg-analyzer-source-high = Held by the high on { $family }.
wg-analyzer-source-crash = Held by the crash from { $family }.
wg-analyzer-source-withdrawal = Held by withdrawal from { $family }.

wg-analyzer-crashes-title = Crashes
wg-analyzer-crash = Crash from { $family }: { $time } left
wg-analyzer-crash-hint = Wears off on its own; another dose only pauses it.

wg-analyzer-habits-title = Dependence
wg-analyzer-habit = Dependence on { $family }: { $meter }%
wg-analyzer-withdrawal = WITHDRAWAL, stage { $stage }: a dose of { $family } ends it.
wg-analyzer-withdrawal-in = Withdrawal in { $time } without { $family }.
wg-analyzer-clean-in = Out of withdrawal risk after { $time } clean.
wg-analyzer-clean-never = A trait keeps it at { $floor }% or more; the withdrawal risk never fades.
wg-analyzer-tolerance = Tolerance { $tolerance }%: doses hit { $weaker }% weaker.
