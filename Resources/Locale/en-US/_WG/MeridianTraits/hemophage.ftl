## Traits

trait-wg-hemophagia-name = Hemophagia
trait-wg-hemophagia-desc = A tumor has replaced your heart. You don't eat or breathe anymore, but you're always losing blood and have to keep drinking it to stay alive, from a blood pack or from someone you're pulling. Your body throws up anything that isn't blood.
trait-wg-hemophagia-masquerade-name = Hemophagia (Masquerade)
trait-wg-hemophagia-masquerade-desc = Same as Hemophagia, but your tumor puts up with normal food and drink, so you can eat with everyone else without throwing up.

wg-hemophage-spawn-text = You are a [color=#c00000]Hemophage[/color]. You're always slowly losing blood. Drink blood, or pull someone and use Drain Victim to feed on them. The blood alert shows how much you have left. Click it to see the exact amount.

## Organs

ent-WGOrganHemophageTumor = pulsating tumor
    .desc = It almost looks like a heart, except it's black as coal and grown into a shape no heart should have. Someone was alive because of this thing.

ent-WGOrganHemophageTongue = tongue
    .desc = A fleshy muscle, mostly used for lying. This one is used for drinking.

wg-tumor-corrupted-organ = corrupted { $organ }
wg-tumor-atrophied-organ = atrophied { $organ }
wg-tumor-corrupted-organ-desc = It's shaped like a normal organ, but it's swollen with black, pulsing tissue.

## Actions

ent-WGActionHemophageDormant = Enter Dormant State
    .desc = Put your tumor to sleep. It barely uses any blood and you can eat normal food, but you can't use your other powers, you move slowly and you take a lot more damage. Waking it up again takes a while.
wg-hemophage-action-dormant-enter = Enter Dormant State
wg-hemophage-action-dormant-enter-desc = Put your tumor to sleep. It barely uses any blood and you can eat normal food, but you can't use your other powers, you move slowly and you take a lot more damage. Waking it up again takes a while.
wg-hemophage-action-dormant-exit = Exit Dormant State
wg-hemophage-action-dormant-exit-desc = Wake your tumor back up. Once it's awake, you'll have to wait a while before you can put it to sleep again.

ent-WGActionHemophageRegen = Hemokinetic Regeneration
    .desc = Slowly heal your bruises, cuts and burns using your blood. Shuts off if you're too badly hurt.
ent-WGActionHemophageClot = Hemokinetic Clot
    .desc = Spend some blood to stop your worst bleeding wound. It's only a quick fix, and the wound opens up again if that limb gets hurt.
ent-WGActionHemophageMaster = Master of the House
    .desc = Take your lungs back from the tumor. You'll need to breathe again, and you'll be slower with half your stamina, but your blood slowly builds back up.
ent-WGActionHemophageDrain = Drain Victim
    .desc = Drink blood from the person you're pulling. They can't fight back while you feed.

## Alerts

wg-alert-hemophage-blood-name = Blood Reserves
wg-alert-hemophage-blood-desc = How much blood you have left. Click to see the exact amount.
wg-alert-hemophage-satiated-name = Thirst Satiated
wg-alert-hemophage-satiated-desc = You fed on a real person, and nothing else comes close. Your tumor uses blood more slowly for a while.
wg-alert-hemophage-regen-name = Hemokinetic Regeneration
wg-alert-hemophage-regen-desc = Your wounds are slowly closing. It's costing you blood.
wg-alert-hemophage-master-name = Master of the House
wg-alert-hemophage-master-desc = You've taken your lungs back from the tumor. You need air again and you're slower, but your blood is slowly coming back.
wg-alert-hemophage-slave-name = Slave to the Tumor
wg-alert-hemophage-slave-desc = You gave your lungs back to the tumor. It needs a little time to fix them up.

## Messages

wg-hemophage-ran-out = You ran out of blood!
wg-hemophage-convulse = { CAPITALIZE(THE($target)) } convulses as black veins bulge across { POSS-ADJ($target) } skin!
wg-hemophage-blood-level = Blood: { $current }u of { $max }u.
wg-hemophage-blood-too-low = You don't have enough blood for that!

wg-hemophage-dormant-entering = You start to calm your tumor...
wg-hemophage-dormant-leaving = You start to wake your tumor...
wg-hemophage-dormant-cancelled = You lose focus.
wg-hemophage-dormant-start = Your tumor's pulse slows to a crawl. You feel weak, and every step takes effort.
wg-hemophage-dormant-end = Your tumor starts pumping again and blood rushes through your veins. You feel strong again.

wg-hemophage-regen-on = You start pushing blood into your wounds.
wg-hemophage-regen-off = You stop healing yourself.
wg-hemophage-regen-too-damaged = You're too hurt to heal yourself this way!

wg-hemophage-clot-nothing = You aren't bleeding anywhere!
wg-hemophage-clot-done = The blood in your { $part } thickens and the bleeding stops.
wg-hemophage-clot-reopened = The clot on your { $part } tears open!
wg-hemophage-blood-warmth = Warmth spreads through you as the fresh blood sinks in. Your wounds feel a little better.

wg-hemophage-master-recovering = Your lungs haven't recovered yet.
wg-hemophage-master-on = You take your lungs back from the tumor. You'll have to breathe again.
wg-hemophage-master-off = You let the tumor have your lungs back.
wg-hemophage-slave-start = Relief washes over you as the tumor takes over again.

wg-hemophage-tasted-awful = That tasted awful...
wg-hemophage-nausea = Your stomach turns.

wg-hemophage-drain-no-target = You need to be pulling someone to feed on them!
wg-hemophage-drain-no-consent = {CAPITALIZE(THE($target))} can't be fed on.
wg-hemophage-drain-full = You're already full!
wg-hemophage-drain-dead = You can't feed on the dead!
wg-hemophage-drain-unsuitable = You can't drink { THE($target) }'s blood!
wg-hemophage-drain-holy-victim = { CAPITALIZE(THE($user)) } goes to bite you, then flinches away!
wg-hemophage-drain-holy = { CAPITALIZE(THE($target)) } is blessed! You pull back before you burn yourself.
wg-hemophage-drain-garlic-victim = { CAPITALIZE(THE($user)) } goes to bite you, then recoils in disgust!
wg-hemophage-drain-garlic = { CAPITALIZE(THE($target)) } reeks of garlic! You can't bring yourself to bite.
wg-hemophage-drain-animal-full = Blood like that won't fill you up any more than this.
wg-hemophage-drain-stopped = You stop feeding.
wg-hemophage-drain-held = { CAPITALIZE(THE($user)) } sinks { POSS-ADJ($user) } teeth into you! You can't break free!
wg-hemophage-drain-victim = { CAPITALIZE(THE($user)) } drinks your blood!
wg-hemophage-drain-animal = You choke down a mouthful of { THE($target) }'s blood. It's thin and tastes vile.
wg-hemophage-drain-foreign = You force down some of { THE($target) }'s blood. It tastes wrong, and your tumor hates it.
wg-hemophage-drain-person = You gulp down warm blood from { THE($target) }'s veins!
wg-hemophage-drain-player = You gulp down warm blood from { THE($target) }'s veins. It's the best thing you've tasted in ages!
wg-hemophage-drain-slaked = You're full. You can't drink any more.
wg-hemophage-drain-pale = They're starting to look pale...
wg-hemophage-drain-pale-victim = You feel cold, clammy and very tired...
wg-hemophage-drain-dry = { CAPITALIZE(THE($target)) } is almost bled dry.
wg-hemophage-drain-hesitate = If you take much more from { THE($target) }, it'll kill { OBJECT($target) }. Part of you doesn't care.

## Food and drink

ent-WGFoodBloodRiceRaw = uncooked blood rice
    .desc = A clump of raw rice, drenched in blood.
ent-WGFoodBloodRicePearl = kessen shinju
    .desc = Little balls of sticky rice rolled in fresh blood. Best eaten hot.
ent-WGFoodBloodNoodles = boiled blood noodles
    .desc = Plain noodles boiled in blood. Could use a few more ingredients.
ent-WGFoodBoatNoodles = boat noodles
    .desc = Curly noodles and blood curds in a dark, spiced blood broth. Smells strongly of iron.
ent-WGFoodBloodCurd = blood curd
    .desc = Cooked, set blood, sometimes called blood tofu. Soft, smooth and a little chewy.
ent-WGFoodBloodCake = ti hoeh koe
    .desc = Steamed sticky rice and pig's blood, rolled in sugar. This one is heavy on the blood.
ent-WGFoodBloodSoup = dinuguan
    .desc = A rich, spicy stew of blood curds, vinegar, chili and garlic.

wg-recipe-blood-rice-pearl = kessen shinju recipe
wg-recipe-blood-curd = blood curd recipe
wg-recipe-blood-noodles = blood noodles recipe
wg-recipe-boat-noodles = boat noodles recipe
wg-recipe-blood-cake = ti hoeh koe recipe
wg-recipe-blood-soup = dinuguan recipe

wg-flavor-complex-congealed-blood = like congealed blood
wg-flavor-complex-bloodshot = like thick, rich blood
wg-flavor-complex-bloodshot-base = like a protein shake with a shot in it

reagent-name-wg-bloodshot-base = bloodshot base
reagent-desc-wg-bloodshot-base = The nutrient and alcohol mix that goes into a Bloodshot. Not great on its own.
reagent-name-wg-bloodshot = bloodshot
reagent-desc-wg-bloodshot = A blood cocktail made for hemophages. Tastes like a hangover cure and refills lost blood fast.
reagent-name-wg-taste-suppressor = taste suppressor
reagent-desc-wg-taste-suppressor = A clear medicine that numbs your sense of taste. While it's in you, a hemophage's tumor won't reject food or drink.

wg-reagent-effect-guidebook-suppress-taste =
    { $chance ->
        [1] Dulls
       *[other] chance to dull
    } the sense of taste, so a hemophage's tumor accepts any food
