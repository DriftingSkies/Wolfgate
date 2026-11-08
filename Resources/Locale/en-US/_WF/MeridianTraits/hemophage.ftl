## Traits

trait-wf-hemophagia-name = Hemophagia
trait-wf-hemophagia-desc = A tumor has replaced your heart. You don't eat or breathe anymore, but you're always losing blood and have to keep drinking it to stay alive, from a blood pack or from someone you're pulling. Your body throws up anything that isn't blood.
trait-wf-hemophagia-masquerade-name = Hemophagia (Masquerade)
trait-wf-hemophagia-masquerade-desc = Same as Hemophagia, but your tumor puts up with normal food and drink, so you can eat with everyone else without throwing up.

wf-hemophage-spawn-text = You are a [color=#c00000]Hemophage[/color]. You're always slowly losing blood. Drink blood, or pull someone and use Drain Victim to feed on them. The blood alert shows how much you have left. Click it to see the exact amount.

## Organs

ent-WFOrganHemophageTumor = pulsating tumor
    .desc = It almost looks like a heart, except it's black as coal and grown into a shape no heart should have. Someone was alive because of this thing.

ent-WFOrganHemophageTongue = tongue
    .desc = A fleshy muscle, mostly used for lying. This one is used for drinking.

wf-tumor-corrupted-organ = corrupted { $organ }
wf-tumor-atrophied-organ = atrophied { $organ }
wf-tumor-corrupted-organ-desc = It's shaped like a normal organ, but it's swollen with black, pulsing tissue.

## Actions

ent-WFActionHemophageDormant = Enter Dormant State
    .desc = Put your tumor to sleep. It barely uses any blood and you can eat normal food, but you can't use your other powers, you move slowly and you take a lot more damage. Waking it up again takes a while.
wf-hemophage-action-dormant-enter = Enter Dormant State
wf-hemophage-action-dormant-enter-desc = Put your tumor to sleep. It barely uses any blood and you can eat normal food, but you can't use your other powers, you move slowly and you take a lot more damage. Waking it up again takes a while.
wf-hemophage-action-dormant-exit = Exit Dormant State
wf-hemophage-action-dormant-exit-desc = Wake your tumor back up. Once it's awake, you'll have to wait a while before you can put it to sleep again.

ent-WFActionHemophageRegen = Hemokinetic Regeneration
    .desc = Slowly heal your bruises, cuts and burns using your blood. Shuts off if you're too badly hurt.
ent-WFActionHemophageClot = Hemokinetic Clot
    .desc = Spend some blood to stop your worst bleeding wound. It's only a quick fix, and the wound opens up again if that limb gets hurt.
ent-WFActionHemophageMaster = Master of the House
    .desc = Take your lungs back from the tumor. You'll need to breathe again, and you'll be slower with half your stamina, but your blood slowly builds back up.
ent-WFActionHemophageDrain = Drain Victim
    .desc = Drink blood from the person you're pulling. They can't fight back while you feed.

## Alerts

wf-alert-hemophage-blood-name = Blood Reserves
wf-alert-hemophage-blood-desc = How much blood you have left. Click to see the exact amount.
wf-alert-hemophage-satiated-name = Thirst Satiated
wf-alert-hemophage-satiated-desc = You fed on a real person, and nothing else comes close. Your tumor uses blood more slowly for a while.
wf-alert-hemophage-regen-name = Hemokinetic Regeneration
wf-alert-hemophage-regen-desc = Your wounds are slowly closing. It's costing you blood.
wf-alert-hemophage-master-name = Master of the House
wf-alert-hemophage-master-desc = You've taken your lungs back from the tumor. You need air again and you're slower, but your blood is slowly coming back.
wf-alert-hemophage-slave-name = Slave to the Tumor
wf-alert-hemophage-slave-desc = You gave your lungs back to the tumor. It needs a little time to fix them up.

## Messages

wf-hemophage-ran-out = You ran out of blood!
wf-hemophage-convulse = { CAPITALIZE(THE($target)) } convulses as black veins bulge across { POSS-ADJ($target) } skin!
wf-hemophage-blood-level = Blood: { $current }u of { $max }u.
wf-hemophage-blood-too-low = You don't have enough blood for that!

wf-hemophage-dormant-entering = You start to calm your tumor...
wf-hemophage-dormant-leaving = You start to wake your tumor...
wf-hemophage-dormant-cancelled = You lose focus.
wf-hemophage-dormant-start = Your tumor's pulse slows to a crawl. You feel weak, and every step takes effort.
wf-hemophage-dormant-end = Your tumor starts pumping again and blood rushes through your veins. You feel strong again.

wf-hemophage-regen-on = You start pushing blood into your wounds.
wf-hemophage-regen-off = You stop healing yourself.
wf-hemophage-regen-too-damaged = You're too hurt to heal yourself this way!

wf-hemophage-clot-nothing = You aren't bleeding anywhere!
wf-hemophage-clot-done = The blood in your { $part } thickens and the bleeding stops.
wf-hemophage-clot-reopened = The clot on your { $part } tears open!
wf-hemophage-blood-warmth = Warmth spreads through you as the fresh blood sinks in. Your wounds feel a little better.

wf-hemophage-master-recovering = Your lungs haven't recovered yet.
wf-hemophage-master-on = You take your lungs back from the tumor. You'll have to breathe again.
wf-hemophage-master-off = You let the tumor have your lungs back.
wf-hemophage-slave-start = Relief washes over you as the tumor takes over again.

wf-hemophage-tasted-awful = That tasted awful...
wf-hemophage-nausea = Your stomach turns.

wf-hemophage-drain-no-target = You need to be pulling someone to feed on them!
wf-hemophage-drain-no-consent = {CAPITALIZE(THE($target))} can't be fed on.
wf-hemophage-drain-full = You're already full!
wf-hemophage-drain-dead = You can't feed on the dead!
wf-hemophage-drain-unsuitable = You can't drink { THE($target) }'s blood!
wf-hemophage-drain-holy-victim = { CAPITALIZE(THE($user)) } goes to bite you, then flinches away!
wf-hemophage-drain-holy = { CAPITALIZE(THE($target)) } is blessed! You pull back before you burn yourself.
wf-hemophage-drain-garlic-victim = { CAPITALIZE(THE($user)) } goes to bite you, then recoils in disgust!
wf-hemophage-drain-garlic = { CAPITALIZE(THE($target)) } reeks of garlic! You can't bring yourself to bite.
wf-hemophage-drain-animal-full = Blood like that won't fill you up any more than this.
wf-hemophage-drain-stopped = You stop feeding.
wf-hemophage-drain-held = { CAPITALIZE(THE($user)) } sinks { POSS-ADJ($user) } teeth into you! You can't break free!
wf-hemophage-drain-victim = { CAPITALIZE(THE($user)) } drinks your blood!
wf-hemophage-drain-animal = You choke down a mouthful of { THE($target) }'s blood. It's thin and tastes vile.
wf-hemophage-drain-foreign = You force down some of { THE($target) }'s blood. It tastes wrong, and your tumor hates it.
wf-hemophage-drain-person = You gulp down warm blood from { THE($target) }'s veins!
wf-hemophage-drain-player = You gulp down warm blood from { THE($target) }'s veins. It's the best thing you've tasted in ages!
wf-hemophage-drain-slaked = You're full. You can't drink any more.
wf-hemophage-drain-pale = They're starting to look pale...
wf-hemophage-drain-pale-victim = You feel cold, clammy and very tired...
wf-hemophage-drain-dry = { CAPITALIZE(THE($target)) } is almost bled dry.
wf-hemophage-drain-hesitate = If you take much more from { THE($target) }, it'll kill { OBJECT($target) }. Part of you doesn't care.

## Food and drink

ent-WFFoodBloodRiceRaw = uncooked blood rice
    .desc = A clump of raw rice, drenched in blood.
ent-WFFoodBloodRicePearl = kessen shinju
    .desc = Little balls of sticky rice rolled in fresh blood. Best eaten hot.
ent-WFFoodBloodNoodles = boiled blood noodles
    .desc = Plain noodles boiled in blood. Could use a few more ingredients.
ent-WFFoodBoatNoodles = boat noodles
    .desc = Curly noodles and blood curds in a dark, spiced blood broth. Smells strongly of iron.
ent-WFFoodBloodCurd = blood curd
    .desc = Cooked, set blood, sometimes called blood tofu. Soft, smooth and a little chewy.
ent-WFFoodBloodCake = ti hoeh koe
    .desc = Steamed sticky rice and pig's blood, rolled in sugar. This one is heavy on the blood.
ent-WFFoodBloodSoup = dinuguan
    .desc = A rich, spicy stew of blood curds, vinegar, chili and garlic.

wf-recipe-blood-rice-pearl = kessen shinju recipe
wf-recipe-blood-curd = blood curd recipe
wf-recipe-blood-noodles = blood noodles recipe
wf-recipe-boat-noodles = boat noodles recipe
wf-recipe-blood-cake = ti hoeh koe recipe
wf-recipe-blood-soup = dinuguan recipe

wf-flavor-complex-congealed-blood = like congealed blood
wf-flavor-complex-bloodshot = like thick, rich blood
wf-flavor-complex-bloodshot-base = like a protein shake with a shot in it

reagent-name-wf-bloodshot-base = bloodshot base
reagent-desc-wf-bloodshot-base = The nutrient and alcohol mix that goes into a Bloodshot. Not great on its own.
reagent-name-wf-bloodshot = bloodshot
reagent-desc-wf-bloodshot = A blood cocktail made for hemophages. Tastes like a hangover cure and refills lost blood fast.
reagent-name-wf-taste-suppressor = taste suppressor
reagent-desc-wf-taste-suppressor = A clear medicine that numbs your sense of taste. While it's in you, a hemophage's tumor won't reject food or drink.

wf-reagent-effect-guidebook-suppress-taste =
    { $chance ->
        [1] Dulls
       *[other] chance to dull
    } the sense of taste, so a hemophage's tumor accepts any food
