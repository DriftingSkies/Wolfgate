# NPC bartender

The owner asked for a bartender who accepts orders. `WGCrewBartender` is an NpcCrew role (duty `Bartender`,
engagement `WhenAttacked`) that a commanded ship staffs through the `WGShipCrewPostBartender` roster post.

## Existing work

Nothing upstream or in the fork layers (`_NF`, `_Mono`, `_DV`, `_CE`, `_Goobstation`, ...) has NPC bartending or
drink ordering. The closest patterns are Wolfgate's own: station traders (`Traders`) take requests from a dialogue
menu and pay out across a counter, and the ship squatters (`ShipSquatters`) answer what players say nearby through
`ListenEvent`. Traders are rooted and godmoded (`TraderSystem.MakeInert`), so a bartender who walks to the
dispensers can't be one; he is a crewman instead, and borrows the trader's counter idea (the drink goes on the table
in front of the customer) and the squatter's hearing.

## Ordering

Two ways in, both ending in `WGCrewBartenderSystem.TryOrder`:

- **Speech.** The bartender has an `ActiveListener` (range 6). What a player says is lower-cased and stripped of
  punctuation, then matched against each menu drink's Fluent keywords as whole words, longest first, so "whiskey
  cola" is not also a "whiskey" and one sentence can order several drinks. A message of five words or fewer that
  names a drink is an order; a longer one also needs a request phrase ("can I get", "please") or the bartender named
  ("bartender", "barkeep"), so talk about beer at the bar doesn't pour one. Menu phrases ("what do you have", "menu")
  get the menu; a request he can't place gets a polite no and the menu. He only hears unoccluded speakers.
- **Verbs.** An "Order a drink" verb category lists the menu and "Ask for the menu", for players who don't want to
  guess the words, and for anyone whose accent garbles them.

Speech is the natural one; the verbs are the robust one. Every keyword list is in
`Resources/Locale/en-US/_WG/NpcCrew/bartender.ftl`, so the matching is localised with the rest.

An order is refused when the ship turns the customer away (`WGCrewMayTreatEvent`, the medics' check: ShipCommand
refuses the barred and the crew's attackers), the customer struck him, the queue holds eight, the customer already
has two waiting, nothing behind the bar can pour the drink, or there's no clean glass for it.

## Serving

Orders are served first come, first served. For the order under way he takes a glass from his rack (twelve, one back
from the wash every two minutes while short), walks to the nearest dispenser holding a bottle with enough of an
ingredient the glass still lacks, and pours for `PourTime` (2 s, a do-after he stands still for). The reagents are
split out of the dispenser's real stock bottles into the glass, so mixes react there (gin and tonic water make gin
and tonic) and the bottles run dry; then the next dispenser, until the drink is complete. He sets the glass on the
counter nearest the customer, or hands it over where there is no counter, and says so. A step that takes longer than
30 s, or an order older than three minutes, is given up with an apology and the glass poured away.

Idle, he collects empty glasses of his menu's kinds lying on a counter within four tiles of his post back onto his
rack.

Drinks are free. Charging would mean either making him a trader (who can't walk) or duplicating the trader's payment
code; the finite bottles already keep free drinks from minting money.

## Fighting

Engagement `WhenAttacked`: he fights only whoever strikes him, with the pistol on his belt, and goes back to the bar
afterwards. Orders are not heard while he has a live threat.

## Placing him

`WGShipCrewPostBartender` goes on the floor behind the counter, with the booze and soda dispensers within ten tiles
of it (closer is better: every walk counts against the 30 s step limit) and the counter tables (`TableCounterWood`,
anything with `PlaceableSurface`) between him and the stools. He must be able to walk from the post to both
dispensers and to the counter.

## Files

- `Content.Server/_WG/NpcCrew/Systems/WGCrewBartenderSystem.cs`: the order loop, pouring, serving and collecting.
- `Content.Server/_WG/NpcCrew/Systems/WGCrewBartenderSystem.Orders.cs`: hearing, the verbs and the keyword matching.
- `Content.Server/_WG/NpcCrew/Components/WGCrewBartenderComponent.cs`, `HTN/WGCrewBartenderOperator.cs`.
- `Content.Shared/_WG/NpcCrew/WGCrewDrinkPrototype.cs`: the `wgCrewDrink` prototype and the pour do-after event.
- `Resources/Prototypes/_WG/NpcCrew/bartender.yml`: the role, mob, gear, HTN branch and the ten drinks.
