# Faction voices for encounter ships, by the moment. Any line missing here falls back on common.ftl.
# $intruder is the other vessel, $ship the speaker's own hull, $distance metres between them.

## Reavers and other raiders (wf-encounter-pirate-zone): loud while they have the edge, quick to fold without it.

wf-encounter-pirate-zone-warn-wary-1 = {$intruder}, that's a lot of guns for a trader. Keep moving and we'll call it a draw.
wf-encounter-pirate-zone-warn-wary-2 = Easy, {$intruder}. Nobody needs to get shot today. Fly on.
wf-encounter-pirate-zone-warn-hurt-1 = {$intruder}, we're not in the mood. Back off before we change our minds.
wf-encounter-pirate-zone-warn-hurt-2 = Keep your distance, {$intruder}. We've still got teeth.
wf-encounter-pirate-zone-warn-desperate-1 = {$intruder}, we don't want your cargo. Just keep away from us.
wf-encounter-pirate-zone-warn-desperate-2 = Stay back, {$intruder}! We've got nothing worth taking!
wf-encounter-pirate-zone-final-1 = {$intruder}, last chance. Kill your drive or we kill it for you.
wf-encounter-pirate-zone-final-2 = Still moving, {$intruder}? Wrong answer. Heave to, now.
wf-encounter-pirate-zone-attack-hurt-1 = {$intruder} wants a fight? We'll give them one. Fire!
wf-encounter-pirate-zone-attack-hurt-2 = We're bleeding, but so are they. Light up {$intruder}!
wf-encounter-pirate-zone-attack-desperate-1 = They're on us! Shoot {$intruder}, shoot anything!
wf-encounter-pirate-zone-attack-desperate-2 = Can't outrun them. Fire, damn you, fire!
wf-encounter-pirate-zone-fired-1 = {$intruder} fired on us! Nobody shoots at Reavers. Light them up!
wf-encounter-pirate-zone-fired-2 = Oh, they've got guns. {$intruder}, you just made this expensive.
wf-encounter-pirate-zone-recall-returned-1 = {$intruder} again. Didn't learn the first time?
wf-encounter-pirate-zone-recall-fled-1 = Well, well. {$intruder}, back for round two? You ran so fast last time.
wf-encounter-pirate-zone-recall-fled-2 = {$intruder}! You got away once. Not twice.
wf-encounter-pirate-zone-recall-killed-1 = {$intruder}. You spaced our brothers. We'll have your hull for it.
wf-encounter-pirate-zone-recall-killed-2 = That's the one that gutted our ship. Nobody lets {$intruder} leave.
wf-encounter-pirate-zone-hurt-1 = They hit us! Patch that hole and keep shooting!
wf-encounter-pirate-zone-hurt-2 = {$intruder} bites back. Doesn't matter. Keep on them.
wf-encounter-pirate-zone-desperate-1 = This isn't worth it! Break off, break off!
wf-encounter-pirate-zone-desperate-2 = {$intruder}, alright, alright! Keep your cargo! We're leaving!
wf-encounter-pirate-zone-desperate-3 = Half the crew's dead. Somebody get us out of here!
wf-encounter-pirate-zone-flee-1 = Burn for it! Every scrap of thrust, now!
wf-encounter-pirate-zone-flee-2 = Forget the loot. Run!
wf-encounter-pirate-zone-laststand-1 = No way out. Then we take {$intruder} down with us!
wf-encounter-pirate-zone-laststand-2 = Reavers don't beg. Fire till the guns melt!
wf-encounter-pirate-zone-gloat-1 = Look at {$intruder} leak! Keep hitting them!
wf-encounter-pirate-zone-gloat-2 = They're breaking up. Lot of salvage on that one, lads.
wf-encounter-pirate-zone-surrender-1 = This is {$ship}! We give up! Guns cold, guns cold! Don't shoot!
wf-encounter-pirate-zone-surrender-2 = {$intruder}, we yield! You can have the ship, just let us live!
wf-encounter-pirate-zone-surrender-hail-1 = {$intruder}, this is {$ship}. You win. Guns are cold, crew's got their hands up. Take the hold, take whatever. Just don't space us.
wf-encounter-pirate-zone-surrender-hail-2 = Alright, you beat us. {$ship} is yours. We'll sit still. Do we have a deal?
wf-encounter-pirate-zone-surrender-accepted-1 = Deal. We'll sit tight. Nobody's reaching for a gun.
wf-encounter-pirate-zone-surrender-accepted-2 = Thank you. Thank you. Come aboard, we won't make trouble.
wf-encounter-pirate-zone-surrender-refused-1 = No deal? Then you'll pay for every one of us. Back to the guns!
wf-encounter-pirate-zone-surrender-refused-2 = Should've known. Fire everything we have left!
wf-encounter-pirate-zone-surrender-betrayed-1 = They're shooting at a white flag! Fine! Kill them all!
wf-encounter-pirate-zone-surrender-betrayed-2 = {$intruder} wants us dead either way. Then we fight!

## Automated Defense System (wf-ads-zone): directives, threat assessment, no fear and no surrender.

wf-ads-zone-warn-wary-1 = THREAT ASSESSMENT. {$intruder} exceeds this unit's rating. Directive unchanged. Withdraw.
wf-ads-zone-warn-wary-2 = DISARMAMENT DIRECTIVE. {$intruder}, your armament is logged. Withdraw. This unit will not.
wf-ads-zone-warn-hurt-1 = DAMAGE LOGGED. DIRECTIVE UNCHANGED. {$intruder}, withdraw.
wf-ads-zone-warn-hurt-2 = UNIT IMPAIRED. EXCLUSION ZONE HOLDS. {$intruder}, you are {$distance} metres inside it. Withdraw.
wf-ads-zone-warn-desperate-1 = UNIT CRITICAL. EXCLUSION ZONE HOLDS. {$intruder}, withdraw.
wf-ads-zone-warn-desperate-2 = STRUCTURAL INTEGRITY FAILING. DIRECTIVE UNCHANGED. {$intruder}, withdraw.
wf-ads-zone-warn-broken-1 = UNIT DISABLED. ZONE UNENFORCEABLE. {$intruder}: DO NOT APPROACH.
wf-ads-zone-final-1 = FINAL NOTICE. {$intruder}, compliance window closing.
wf-ads-zone-final-2 = FINAL NOTICE. {$intruder}, withdraw or be made combat-ineffective.
wf-ads-zone-attack-hurt-1 = DAMAGE LOGGED. ENGAGING {$intruder}.
wf-ads-zone-attack-hurt-2 = UNIT IMPAIRED. FIRE CONTROL REROUTED. {$intruder} will be made combat-ineffective.
wf-ads-zone-attack-desperate-1 = UNIT CRITICAL. ALL REMAINING FIRE ON {$intruder}.
wf-ads-zone-attack-desperate-2 = INTEGRITY FAILING. ENGAGEMENT CONTINUES.
wf-ads-zone-fired-1 = HOSTILE FIRE LOGGED. {$intruder} declared a threat. Engaging.
wf-ads-zone-fired-2 = ATTACK DETECTED. {$intruder} will be made combat-ineffective.
wf-ads-zone-recall-returned-1 = {$intruder}: RETURN LOGGED. Prior warning on record. Withdraw.
wf-ads-zone-recall-fled-1 = {$intruder}: PRIOR ENGAGEMENT ON RECORD. Previous withdrawal noted. Resuming.
wf-ads-zone-recall-killed-1 = {$intruder}: DESTRUCTION OF ADS UNIT ON RECORD. Threat rating raised. No query will be offered.
wf-ads-zone-hurt-1 = DAMAGE LOGGED. DIRECTIVE UNCHANGED.
wf-ads-zone-hurt-2 = HULL BREACH. REROUTING. ENGAGEMENT CONTINUES.
wf-ads-zone-desperate-1 = UNIT CRITICAL. SECONDARY SYSTEMS OFFLINE. ENGAGEMENT CONTINUES.
wf-ads-zone-desperate-2 = {$intruder}: THIS UNIT IS FAILING. THE DIRECTIVE IS NOT.
wf-ads-zone-laststand-1 = UNIT LOSS IMMINENT. FINAL DIRECTIVE: MAKE {$intruder} COMBAT-INEFFECTIVE.
wf-ads-zone-laststand-2 = NO WITHDRAWAL PROTOCOL FOUND. CONTINUING.
wf-ads-zone-gloat-1 = {$intruder}: STRUCTURAL FAILURE DETECTED. Withdraw while able.
wf-ads-zone-gloat-2 = {$intruder} IS COMBAT-INEFFECTIVE IN SEVERAL SECTIONS. CONTINUING.

## Vanguard (wf-encounter-pdv-zone)

wf-encounter-pdv-zone-final-1 = {$intruder}, the Vanguard does not ask three times. Turn away.
wf-encounter-pdv-zone-hurt-1 = We are hit. The Sultan's ships do not break. Keep firing.
wf-encounter-pdv-zone-desperate-1 = We cannot hold. All hands, disengage and fall back.
wf-encounter-pdv-zone-surrender-1 = This is {$ship}. We strike our colours. Cease fire.
wf-encounter-pdv-zone-surrender-hail-1 = {$intruder}, this is {$ship}. We strike our colours and ask for quarter for the crew. Will you grant it?
wf-encounter-pdv-zone-surrender-betrayed-1 = They fire on struck colours! For the Sultan, fight!

## Union remnant (wf-encounter-ussp-zone)

wf-encounter-ussp-zone-final-1 = {$intruder}, the Union has warned you twice. History will not record a third.
wf-encounter-ussp-zone-hurt-1 = We are hit. The Union has bled before. Keep firing.
wf-encounter-ussp-zone-desperate-1 = Comrades, we are finished. Make every shot count.
wf-encounter-ussp-zone-laststand-1 = For the Union! No surrender!
wf-encounter-ussp-zone-surrender-1 = This is {$ship}. Against orders, we yield. Cease fire.
wf-encounter-ussp-zone-surrender-hail-1 = {$intruder}. This is {$ship}. Our captain is dead and so is most of the crew. We yield. Will you take it?

## Black market (wf-encounter-blackmarket-zone)

wf-encounter-blackmarket-zone-final-1 = {$intruder}, we said turn around. This is the last polite thing you hear from us.
wf-encounter-blackmarket-zone-desperate-1 = Forget the cargo, it isn't worth dying over! Get us out!
wf-encounter-blackmarket-zone-surrender-1 = This is {$ship}! We give! Take the crates, we never saw you!
wf-encounter-blackmarket-zone-surrender-hail-1 = {$intruder}, let's be reasonable. {$ship} surrenders. You take the goods, we take our lives. Deal?

## Blockade (wf-encounter-blockade-zone)

wf-encounter-blockade-zone-hurt-1 = The line holds. Keep firing.
wf-encounter-blockade-zone-desperate-1 = We can't hold the line. Pull back!
wf-encounter-blockade-zone-surrender-1 = This is {$ship}. The blockade is broken. We surrender.
