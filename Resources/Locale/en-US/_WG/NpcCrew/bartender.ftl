wg-crew-role-bartender = Bartender

ent-WGMobCrewBartender = bartender
    .desc = Polishes the same glass whenever you look at him.

## The menu. Keywords are comma-separated and match whole words; the longest match wins.

wg-bartender-drink-beer = beer
wg-bartender-drink-beer-keywords = beer, beers, a pint, pint, lager
wg-bartender-drink-ale = ale
wg-bartender-drink-ale-keywords = ale, ales, a pint of ale, pint of ale
wg-bartender-drink-wine = wine
wg-bartender-drink-wine-keywords = wine, glass of wine, red wine, vino
wg-bartender-drink-whiskey = whiskey
wg-bartender-drink-whiskey-keywords = whiskey, whisky, whiskeys, scotch, bourbon
wg-bartender-drink-gin-tonic = gin and tonic
wg-bartender-drink-gin-tonic-keywords = gin and tonic, gin & tonic, gin tonic, g&t, g & t, gin
wg-bartender-drink-whiskey-cola = whiskey cola
wg-bartender-drink-whiskey-cola-keywords = whiskey cola, whisky cola, whiskey and cola, whisky and cola, whiskey coke, jack and coke
wg-bartender-drink-cuba-libre = cuba libre
wg-bartender-drink-cuba-libre-keywords = cuba libre, rum and cola, rum and coke, rum cola, rum
wg-bartender-drink-tequila-sunrise = tequila sunrise
wg-bartender-drink-tequila-sunrise-keywords = tequila sunrise, tequila
wg-bartender-drink-black-russian = black russian
wg-bartender-drink-black-russian-keywords = black russian
wg-bartender-drink-cola = cola
wg-bartender-drink-cola-keywords = cola, coke, soda, something soft

## What a customer says. Comma-separated, matched as whole words.

wg-bartender-order-cues = please, can i get, could i get, can i have, could i have, i'll have, i'll take, i'd like, i would like, give me, pour me, get me, i want, i'll get, another, one more, make me
wg-bartender-address-cues = bartender, barkeep, barman, barmaid, barkeeper, a drink, something to drink
wg-bartender-menu-cues = menu, what do you have, what have you got, what've you got, what you got, what do you serve, what can you make, what's on tap, what's on, drinks list, what drinks, any drinks

## What he says.

wg-bartender-ack-1 = One {$drink}, coming up.
wg-bartender-ack-2 = Right away: one {$drink}.
wg-bartender-ack-3 = Sure thing. One {$drink}.
wg-bartender-ack-queued = One {$drink}, {$name}. {$ahead ->
    [one] One ahead of you.
   *[other] {$ahead} ahead of you.
}
wg-bartender-serve-1 = Here's your {$drink}, {$name}.
wg-bartender-serve-2 = One {$drink}. Enjoy.
wg-bartender-serve-3 = There you go, {$name}: one {$drink}. Mind the glass.
wg-bartender-menu = Tonight I'm pouring {$menu}.
wg-bartender-menu-separator = {", "}
wg-bartender-unknown = Can't do that one, sorry. I can pour {$menu}.
wg-bartender-out = No {$drink} tonight, sorry. We're out.
wg-bartender-ran-out = The bottle ran dry halfway through that {$drink}. Sorry.
wg-bartender-no-glass = No clean glasses right now. Give me a minute.
wg-bartender-busy = Bit of a queue, {$name}. Give me a moment.
wg-bartender-one-at-a-time = Finish what you've got coming first, {$name}.
wg-bartender-refuse = Not serving you.
wg-bartender-give-up = Couldn't get that {$drink} to you. Ask again.
wg-bartender-spilled = Lost your {$drink}, sorry. Ask again.

## His verbs.

wg-bartender-verb-order = Order a drink
wg-bartender-verb-menu = Ask for the menu
