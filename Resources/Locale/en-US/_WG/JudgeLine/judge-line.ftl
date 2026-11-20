wg-judgeline-program-name = Judge Line
wg-encounter-name-judge-responder = Judge responder {$designation}

wg-judgeline-kind-underfire = Under fire
wg-judgeline-kind-boarded = Boarded
wg-judgeline-kind-medical = Medical
wg-judgeline-kind-stranded = Stranded
wg-judgeline-kind-adrift = Adrift
wg-judgeline-kind-assault = Assault
wg-judgeline-kind-underfire-hint = Another ship is shooting at yours.
wg-judgeline-kind-boarded-hint = Hostiles are aboard your ship.
wg-judgeline-kind-medical-hint = You or your crew are badly hurt or dead.
wg-judgeline-kind-stranded-hint = Your ship has no thrust, fuel or power.
wg-judgeline-kind-adrift-hint = You're in space with no ship.
wg-judgeline-kind-assault-hint = You're being attacked in person.

wg-judgeline-app-title = Judge Line
wg-judgeline-app-intro = Call a Caelestinus judge ship to an emergency. Vagrants only, Colossus sector only. Costs {$fee} spesos. If the judges find no emergency, you lose the fee and can't call again for a while.
wg-judgeline-app-cancel = Cancel call
wg-judgeline-app-queued = Waiting for a free ship (place {$place})
wg-judgeline-app-dispatched = Judges arriving in {$seconds}s
wg-judgeline-app-on-scene = Judges on scene
wg-judgeline-app-inbound = Judge ship inbound
wg-judgeline-app-open = [bold]{$kind}[/bold]: {$phase}
    {$status}

wg-judgeline-refuse-offline = The Judge Line is closed.
wg-judgeline-refuse-kind = That's not an emergency the judges handle.
wg-judgeline-refuse-not-vagrant = The Judge Line only takes calls from vagrants.
wg-judgeline-refuse-open = You already have a call open.
wg-judgeline-refuse-sector = The judges only answer in the Colossus sector.
wg-judgeline-refuse-station = You're on a station. Get help there.
wg-judgeline-refuse-barred = Too many false calls. The Judge Line won't take yours this shift.
wg-judgeline-refuse-cooldown = You can call again in {$minutes} min.
wg-judgeline-refuse-warrant = You owe the Bench. Pay your tickets first.
wg-judgeline-refuse-funds = A call costs {$fee} spesos.

wg-judgeline-chat = [color=#37946e][bold]Judge Line:[/bold][/color] {$message}
wg-judgeline-status-received = Call received: {$kind}. Waiting for a free ship.
wg-judgeline-status-dispatched = A judge ship is on its way. ETA {$seconds} seconds.
wg-judgeline-status-inbound = The judge ship is out of FTL and closing on you.
wg-judgeline-status-on-scene = The judges are on scene.
wg-judgeline-status-switched = The judges found a different emergency and are dealing with it: {$kind}.
wg-judgeline-status-aboard = You're aboard. The judges are taking you to {$station}.
wg-judgeline-status-diverted = {$refused} won't take the judge ship. Heading to {$station} instead.
wg-judgeline-status-hauled = The judges pulled you aboard.
wg-judgeline-status-disembark = Docked. Please get off the judge ship.
wg-judgeline-status-relief = The judges left you a relief crate with fuel and repair supplies. Their engineer is coming aboard to fuel your generator.
wg-judgeline-status-refuelled = The judges fuelled your generator.
wg-judgeline-status-resolved = Emergency over. The judges are leaving. Stay safe, vagrant.
wg-judgeline-status-timeout = The judges can't stay any longer and are leaving.
wg-judgeline-status-hoax = The judges found no emergency. Your fee is kept. False calls: {$strikes} of {$limit}.
wg-judgeline-status-betrayed = Your ship fired on the judges. The call is void.
wg-judgeline-status-cancelled = Call cancelled.
wg-judgeline-status-left = You left the sector. The call is void.
wg-judgeline-status-lost = The judge ship was lost.
wg-judgeline-status-unavailable = No judge ship could reach you.
wg-judgeline-status-refunded = Your {$fee} spesos are refunded.
wg-judgeline-status-refund-failed = Your {$fee} spesos couldn't be refunded. Ask an admin.

wg-judgeline-radio-inbound = Judge responder out of FTL, inbound to your position.
wg-judgeline-radio-underfire = Judges on scene. Hostile ship, break off now or be fired on.
wg-judgeline-radio-boarded = Judges docking. Boarding party, stand down.
wg-judgeline-radio-medical = Judges docking with a paramedic aboard.
wg-judgeline-radio-stranded = Judges docking with relief supplies.
wg-judgeline-radio-adrift = Judges alongside. Get to our airlock, we'll take you in.
wg-judgeline-radio-assault = Judges on scene. Whoever's attacking that vagrant, back off.
wg-judgeline-radio-hoax = No emergency here. Don't waste the judges' time.
wg-judgeline-radio-leaving = Judges leaving. Fly safe.

wg-judgeline-verb-category = Judge Line
wg-judgeline-verb-cancel = Cancel call
wg-judgeline-verb-confirm = Call the judges for {$fee} spesos?

cmd-wg_judgeline-desc = Lists Judge Line calls, places a free one, or ends one.
cmd-wg_judgeline-help = Usage: {$command} list | call <kind> [player] | end <id>
cmd-wg_judgeline-row = #{$id} {$caller}: {$kind}, {$phase}
cmd-wg_judgeline-no-call = No such call.
cmd-wg_judgeline-bad-kind = Kinds: UnderFire, Boarded, Medical, Stranded, Adrift, Assault.
cmd-wg_judgeline-no-caller = No caller: attach to an entity or name a player.
cmd-wg_judgeline-placed = Placed call #{$id}.

ent-WGJudgeLineCartridge = judge line cartridge
    .desc = A PDA program that calls the Caelestinus judges to an emergency, for a fee.
ent-WGJudgeLineReliefCrate = judge relief crate
    .desc = Fuel, plating and air, courtesy of the Caelestinus judges.
