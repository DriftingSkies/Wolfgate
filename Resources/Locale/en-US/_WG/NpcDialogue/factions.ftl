# Faction voices for encounter ships, by the moment. Any line missing here falls back on common.ftl.
# $intruder is the other vessel, $ship the speaker's own hull, $distance metres between them.

## Reavers and other raiders (wg-encounter-pirate-zone): loud while they have the edge, quick to fold without it.

wg-encounter-pirate-zone-warn-wary-1 = {$intruder}, that's a lot of guns for a trader. Keep moving and we'll call it a draw.
wg-encounter-pirate-zone-warn-wary-2 = Easy, {$intruder}. Nobody needs to get shot today. Fly on.
wg-encounter-pirate-zone-warn-hurt-1 = {$intruder}, we're not in the mood. Back off before we change our minds.
wg-encounter-pirate-zone-warn-hurt-2 = Keep your distance, {$intruder}. We've still got teeth.
wg-encounter-pirate-zone-warn-desperate-1 = {$intruder}, we don't want your cargo. Just keep away from us.
wg-encounter-pirate-zone-warn-desperate-2 = Stay back, {$intruder}! We've got nothing worth taking!
wg-encounter-pirate-zone-final-1 = {$intruder}, last chance. Kill your drive or we kill it for you.
wg-encounter-pirate-zone-final-2 = Still moving, {$intruder}? Wrong answer. Heave to, now.
wg-encounter-pirate-zone-attack-hurt-1 = {$intruder} wants a fight? We'll give them one. Fire!
wg-encounter-pirate-zone-attack-hurt-2 = We're bleeding, but so are they. Light up {$intruder}!
wg-encounter-pirate-zone-attack-desperate-1 = They're on us! Shoot {$intruder}, shoot anything!
wg-encounter-pirate-zone-attack-desperate-2 = Can't outrun them. Fire, damn you, fire!
wg-encounter-pirate-zone-fired-1 = {$intruder} fired on us! Nobody shoots at Reavers. Light them up!
wg-encounter-pirate-zone-fired-2 = Oh, they've got guns. {$intruder}, you just made this expensive.
wg-encounter-pirate-zone-recall-returned-1 = {$intruder} again. Didn't learn the first time?
wg-encounter-pirate-zone-recall-fled-1 = Well, well. {$intruder}, back for round two? You ran so fast last time.
wg-encounter-pirate-zone-recall-fled-2 = {$intruder}! You got away once. Not twice.
wg-encounter-pirate-zone-recall-killed-1 = {$intruder}. You spaced our brothers. We'll have your hull for it.
wg-encounter-pirate-zone-recall-killed-2 = That's the one that gutted our ship. Nobody lets {$intruder} leave.
wg-encounter-pirate-zone-hurt-1 = They hit us! Patch that hole and keep shooting!
wg-encounter-pirate-zone-hurt-2 = {$intruder} bites back. Doesn't matter. Keep on them.
wg-encounter-pirate-zone-desperate-1 = This isn't worth it! Break off, break off!
wg-encounter-pirate-zone-desperate-2 = {$intruder}, alright, alright! Keep your cargo! We're leaving!
wg-encounter-pirate-zone-desperate-3 = Half the crew's dead. Somebody get us out of here!
wg-encounter-pirate-zone-flee-1 = Burn for it! Every scrap of thrust, now!
wg-encounter-pirate-zone-flee-2 = Forget the loot. Run!
wg-encounter-pirate-zone-laststand-1 = No way out. Then we take {$intruder} down with us!
wg-encounter-pirate-zone-laststand-2 = Reavers don't beg. Fire till the guns melt!
wg-encounter-pirate-zone-gloat-1 = Look at {$intruder} leak! Keep hitting them!
wg-encounter-pirate-zone-gloat-2 = They're breaking up. Lot of salvage on that one, lads.
wg-encounter-pirate-zone-surrender-1 = This is {$ship}! We give up! Guns cold, guns cold! Don't shoot!
wg-encounter-pirate-zone-surrender-2 = {$intruder}, we yield! You can have the ship, just let us live!
wg-encounter-pirate-zone-surrender-hail-1 = {$intruder}, this is {$ship}. You win. Guns are cold, crew's got their hands up. Take the hold, take whatever. Just don't space us.
wg-encounter-pirate-zone-surrender-hail-2 = Alright, you beat us. {$ship} is yours. We'll sit still. Do we have a deal?
wg-encounter-pirate-zone-surrender-accepted-1 = Deal. We'll sit tight. Nobody's reaching for a gun.
wg-encounter-pirate-zone-surrender-accepted-2 = Thank you. Thank you. Come aboard, we won't make trouble.
wg-encounter-pirate-zone-surrender-refused-1 = No deal? Then you'll pay for every one of us. Back to the guns!
wg-encounter-pirate-zone-surrender-refused-2 = Should've known. Fire everything we have left!
wg-encounter-pirate-zone-surrender-betrayed-1 = They're shooting at a white flag! Fine! Kill them all!
wg-encounter-pirate-zone-surrender-betrayed-2 = {$intruder} wants us dead either way. Then we fight!

## Automated Defense System (wg-ads-zone): directives, threat assessment, no fear and no surrender.

wg-ads-zone-warn-wary-1 = THREAT ASSESSMENT. {$intruder} exceeds this unit's rating. Directive unchanged. Withdraw.
wg-ads-zone-warn-wary-2 = DISARMAMENT DIRECTIVE. {$intruder}, your armament is logged. Withdraw. This unit will not.
wg-ads-zone-warn-hurt-1 = DAMAGE LOGGED. DIRECTIVE UNCHANGED. {$intruder}, withdraw.
wg-ads-zone-warn-hurt-2 = UNIT IMPAIRED. EXCLUSION ZONE HOLDS. {$intruder}, you are {$distance} metres inside it. Withdraw.
wg-ads-zone-warn-desperate-1 = UNIT CRITICAL. EXCLUSION ZONE HOLDS. {$intruder}, withdraw.
wg-ads-zone-warn-desperate-2 = STRUCTURAL INTEGRITY FAILING. DIRECTIVE UNCHANGED. {$intruder}, withdraw.
wg-ads-zone-warn-broken-1 = UNIT DISABLED. ZONE UNENFORCEABLE. {$intruder}: DO NOT APPROACH.
wg-ads-zone-final-1 = FINAL NOTICE. {$intruder}, compliance window closing.
wg-ads-zone-final-2 = FINAL NOTICE. {$intruder}, withdraw or be made combat-ineffective.
wg-ads-zone-attack-hurt-1 = DAMAGE LOGGED. ENGAGING {$intruder}.
wg-ads-zone-attack-hurt-2 = UNIT IMPAIRED. FIRE CONTROL REROUTED. {$intruder} will be made combat-ineffective.
wg-ads-zone-attack-desperate-1 = UNIT CRITICAL. ALL REMAINING FIRE ON {$intruder}.
wg-ads-zone-attack-desperate-2 = INTEGRITY FAILING. ENGAGEMENT CONTINUES.
wg-ads-zone-fired-1 = HOSTILE FIRE LOGGED. {$intruder} declared a threat. Engaging.
wg-ads-zone-fired-2 = ATTACK DETECTED. {$intruder} will be made combat-ineffective.
wg-ads-zone-recall-returned-1 = {$intruder}: RETURN LOGGED. Prior warning on record. Withdraw.
wg-ads-zone-recall-fled-1 = {$intruder}: PRIOR ENGAGEMENT ON RECORD. Previous withdrawal noted. Resuming.
wg-ads-zone-recall-killed-1 = {$intruder}: DESTRUCTION OF ADS UNIT ON RECORD. Threat rating raised. No query will be offered.
wg-ads-zone-hurt-1 = DAMAGE LOGGED. DIRECTIVE UNCHANGED.
wg-ads-zone-hurt-2 = HULL BREACH. REROUTING. ENGAGEMENT CONTINUES.
wg-ads-zone-desperate-1 = UNIT CRITICAL. SECONDARY SYSTEMS OFFLINE. ENGAGEMENT CONTINUES.
wg-ads-zone-desperate-2 = {$intruder}: THIS UNIT IS FAILING. THE DIRECTIVE IS NOT.
wg-ads-zone-laststand-1 = UNIT LOSS IMMINENT. FINAL DIRECTIVE: MAKE {$intruder} COMBAT-INEFFECTIVE.
wg-ads-zone-laststand-2 = NO WITHDRAWAL PROTOCOL FOUND. CONTINUING.
wg-ads-zone-gloat-1 = {$intruder}: STRUCTURAL FAILURE DETECTED. Withdraw while able.
wg-ads-zone-gloat-2 = {$intruder} IS COMBAT-INEFFECTIVE IN SEVERAL SECTIONS. CONTINUING.

## Vanguard (wg-encounter-pdv-zone)

wg-encounter-pdv-zone-final-1 = {$intruder}, the Vanguard does not ask three times. Turn away.
wg-encounter-pdv-zone-hurt-1 = We are hit. The Sultan's ships do not break. Keep firing.
wg-encounter-pdv-zone-desperate-1 = We cannot hold. All hands, disengage and fall back.
wg-encounter-pdv-zone-surrender-1 = This is {$ship}. We strike our colours. Cease fire.
wg-encounter-pdv-zone-surrender-hail-1 = {$intruder}, this is {$ship}. We strike our colours and ask for quarter for the crew. Will you grant it?
wg-encounter-pdv-zone-surrender-betrayed-1 = They fire on struck colours! For the Sultan, fight!

## Union remnant (wg-encounter-ussp-zone)

wg-encounter-ussp-zone-final-1 = {$intruder}, the Union has warned you twice. History will not record a third.
wg-encounter-ussp-zone-hurt-1 = We are hit. The Union has bled before. Keep firing.
wg-encounter-ussp-zone-desperate-1 = Comrades, we are finished. Make every shot count.
wg-encounter-ussp-zone-laststand-1 = For the Union! No surrender!
wg-encounter-ussp-zone-surrender-1 = This is {$ship}. Against orders, we yield. Cease fire.
wg-encounter-ussp-zone-surrender-hail-1 = {$intruder}. This is {$ship}. Our captain is dead and so is most of the crew. We yield. Will you take it?

## Black market (wg-encounter-blackmarket-zone)

wg-encounter-blackmarket-zone-final-1 = {$intruder}, we said turn around. This is the last polite thing you hear from us.
wg-encounter-blackmarket-zone-desperate-1 = Forget the cargo, it isn't worth dying over! Get us out!
wg-encounter-blackmarket-zone-surrender-1 = This is {$ship}! We give! Take the crates, we never saw you!
wg-encounter-blackmarket-zone-surrender-hail-1 = {$intruder}, let's be reasonable. {$ship} surrenders. You take the goods, we take our lives. Deal?

## Blockade (wg-encounter-blockade-zone)

wg-encounter-blockade-zone-hurt-1 = The line holds. Keep firing.
wg-encounter-blockade-zone-desperate-1 = We can't hold the line. Pull back!
wg-encounter-blockade-zone-surrender-1 = This is {$ship}. The blockade is broken. We surrender.
