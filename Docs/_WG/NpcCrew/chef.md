# Crew chef

A crew role (`WGCrewChef`, duty `Chef`, `engagement: WhenAttacked`) that keeps a small menu stocked on a galley's
serving counter. Commanded ships staff it through the `WGShipCrewPostChef` roster post; `wg_crew spawnrole chef`
and Crew Setup work as for any role.

## What he does

`WGCrewChefComponent.Menu` lists the dishes, each a `microwaveMealRecipe` and how many to keep out (`stock`). Every
two seconds an idle chef (`WGCrewChefSystem`) counts each dish on the serving spots and in any smart fridge within
`KitchenRange` (10 m) of his post, adds the dishes already on the way, and takes the one most short that he can make:

1. **Fetch.** He takes the recipe's solid ingredients from the stores in reach: loose on the deck or a table, or in an
   unlocked, unwelded crate, locker or smart fridge. Never from anyone's hands or pockets, from a machine, or off the
   counter. As many as his hands hold per trip.
2. **Load.** He drops them into the nearest free appliance that cooks that recipe type (a `Microwave` component whose
   `validRecipeTypes` match: the microwave for microwave recipes, the electric range's oven for oven ones). Free means
   powered, not broken, not running and empty.
3. **Cook.** With everything in, he sets the appliance's timer to the recipe's cook time and starts it through
   `MicrowaveSystem.Wzhzhzh`, the same call as the appliance's Start button, and waits beside it.
4. **Collect and serve.** He picks up what came out and puts it on the serving spot with the fewest dishes.

Without a serving spot marker (`WGCrewChefServeSpot`) the counter is every anchored table within `CounterRange`
(3 m) of his post that no powered machine stands on. A dish of his left lying by an appliance (he was pulled off it) is
served before anything new is cooked. He calls out what's on to a player who comes within 3 m (once per player every
10 minutes, at most every 3 minutes) and calls each dish he puts out (at most every 2 minutes).

A step that takes over a minute while he is awake (or a cook past its time plus 30 s) is given up: what he holds goes
down, ingredients in an appliance that never started come back out, and he waits 30 s. A fight, a shelter alarm,
death, a player taking him over, or a change of duty (a relieved roster post becomes Guard) ends the dish the same way.

## Cooking approach

Real mechanics throughout. The ingredients are real items, the appliance is the real machine, and the recipe match,
cook time, power check, result and leftovers all come from `MicrowaveSystem`. Driving the appliance by its container
and its start call is not fragile: it needs no UI, and the only states that matter (unpowered, broken, busy, full) are
checked before he commits and again when he loads.

Recipes with reagent ingredients (dough from flour and water, for instance) are skipped with a warning: pouring
reagents means beakers, a dispenser or a grinder, which is far more to drive for little gain. The default menu is solid
recipes only: plain burger, hotdog and buttered toast from the range's oven, and the loaded baked potato from the
microwave.

## Not food from nothing

He never spawns a dish. Every one costs a full set of real ingredients and an appliance's real cook time, so with the
stores empty he stops. The only new ingredients are a pantry's restock: every `RestockInterval` (4 minutes) while a
chef is alive and at work aboard, the first `WGCrewChefPantry` crate on the grid gets one dish's worth for the dish it
holds fewest sets of, up to `PantrySets` (2) of each, sharing out ingredients two dishes use. Each chef's clock is his
own, so new ingredients come aboard at about 15 dishes' worth an hour per chef at most, though never past the pantry's
cap (what is already in the stores is extra), and nothing comes aboard with no pantry, a stolen pantry or a dead
chef. `WGCrewChefPantryFilled` comes stocked with two of each default dish.

## Placing one

- A `WGShipCrewPostChef` inside the galley, beside the counter, facing it.
- A powered microwave and, for oven recipes, an electric range within 10 m of the post.
- A `WGCrewChefPantryFilled` within 10 m of the post, on a tile he can reach.
- `WGCrewChefServeSpot` markers on the counter tiles players reach from the other side, or a counter table within 3 m
  of the post.
