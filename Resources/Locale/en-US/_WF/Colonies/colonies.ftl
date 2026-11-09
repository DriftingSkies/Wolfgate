# World development tiers.
wf-colony-tier-none = Wild
wf-colony-tier-settled = Settled
wf-colony-tier-colony = Colony
wf-colony-tier-charter = Charter
wf-colony-tier-dominion = Dominion

# Announcements and notices.
wf-colony-payout = Colony output on {$world}
wf-colony-import-landed = A colony import came down at your trade hub: {$crates ->
    [one] one crate
   *[other] {$crates} crates
} for the colony shelf.
wf-colony-blockaded = The colony on {$world} is blockaded. No imports will come down until the blockade is broken.
wf-colony-blockade-lifted = The blockade of {$world} is lifted. Colony imports resume.
wf-colony-run-launched = A supply run has set out for the colony on {$world} with {$crates} crates.
wf-colony-run-arrived = {$ship} is holding over {$world} and sending the colony's supplies down.
wf-colony-run-lost = The supply run for the colony on {$world} never arrived. The colony waits on the next.
wf-encounter-name-colony-supply = Colony supply run {$designation}
wf-colony-charter-granted = By writ of the Bench, {$company} holds the charter of {$world} and governs it under Article V.
wf-colony-charter-passed = The Bench has heard the challenge. The charter of {$world} passes to {$company}.
wf-colony-charter-lapsed = The Bench strikes {$company}'s charter of {$world} from the colonial register. The world has no governor.
wf-colony-charter-sender = The Bench of Caelestinus
wf-colony-registered = The Bench enters {$world} in the colonial register as a {$tier} world.
wf-colony-war-requisition = War is declared. The navies requisition from the chartered colonies: their shelves will run down faster.
wf-colony-permit-needed = {$world} is governed by {$company} under its charter. Newcomers need its permit to found an outpost here.
wf-colony-faction-company = {$company} (charter)
wf-colony-legend-world = {$world}: {$tier}
wf-colony-defcon-blockade = Colony on {$world} blockaded

wf-sector-faction-colonies = Colonies

# The seat: the site names a world's signal and map show.
wf-colony-seat-field = Colony landing field
wf-colony-seat-depot = Colony depot
wf-colony-seat-hall = Governor's hall
wf-colony-office-title = Charter office
wf-trade-industry-extraction = Extraction

# Development tab on the outpost console.
wf-outpost-console-tab-development = Development
wf-colony-ui-world = {$world}: {$tier} world
wf-colony-ui-next = {$points} of {$next} points to {$tier}
wf-colony-ui-top = {$points} points
wf-colony-ui-governor = Governed by {$company}
wf-colony-ui-no-governor = No charter
wf-colony-ui-people = {$people} colonists, housing for {$housing}
wf-colony-ui-blockaded = Blockaded: no imports come down.
wf-colony-ui-holders = Holders
wf-colony-ui-no-holders = Nobody has developed this world yet.
wf-colony-ui-governs = governor
wf-colony-ui-share = {$points} ({$share}%)
wf-colony-ui-output = Your output on the colony
wf-colony-ui-output-locked = The world becomes a colony station at Colony tier.
wf-colony-ui-no-output = Your buildings here make nothing for the colony yet.
wf-colony-ui-rate = {$rate} crates/h
wf-colony-ui-climate = The weather holds this world's farms to {$percent}% of what they could grow.
wf-colony-produce-sold = The exchange takes it for the colony's shelf. Paying {$amount}.
wf-colony-ui-keep = Keep
wf-colony-ui-charter = Charter
wf-colony-ui-defence = Batteries engage ships in orbit
wf-colony-ui-open = Open to all founders
wf-colony-ui-applicants = Asked to found here
wf-colony-ui-permits = Permits
wf-colony-ui-none = None.
wf-colony-ui-grant = Grant permit
wf-colony-ui-revoke = Revoke
wf-colony-ui-berths = Yard berths
wf-colony-ui-open-berth = Open berth
wf-colony-ui-close-berth = Close berth
wf-colony-ui-yard-kit = Request yard platform kit

# The orbital yard.
wf-yard-platform-name = {$world} orbital yard
wf-yard-frame-name = {$design} (on the stocks)
wf-yard-founded = An orbital yard now stands over {$world}.
wf-yard-kit-not-orbit = Deploy the yard platform in orbit of your world.
wf-yard-kit-not-governor = Only the company governing {$world} may deploy its yard.
wf-yard-kit-tier = The yard needs a Dominion world.
wf-yard-kit-exists = This world already has a yard.
wf-yard-kit-blocked = Something is in the way. Deploy it in open space.
wf-yard-kit-deployed = The yard platform unfolds.
wf-yard-chute-wrong = The yard chute takes only Ship Components and Metals crates.
wf-yard-chute-unneeded = No build on this yard needs that for its current stage.
wf-yard-chute-fed = {$good} delivered to the {$design} build.
wf-yard-ordered = {$company} laid down a {$design} at the yard over {$world}.
wf-yard-complete = The yard over {$world} finished a {$design} for {$company}.
wf-yard-licence = {$company} now holds a fleet licence for the {$design}.
wf-yard-reason-company = Only company members may lay down builds here.
wf-yard-reason-tier = The yard builds only while its world is at Dominion tier.
wf-yard-reason-berth = Your company has no berth on this yard. The governor opens berths.
wf-yard-reason-full = Every berth is taken.
wf-yard-reason-design = The yard can't build that.
wf-yard-stage-keel = Keel
wf-yard-stage-hull = Hull
wf-yard-stage-systems = Systems
wf-yard-stage-fitting = Fitting out

wf-yard-ui-title = Yard console
wf-yard-ui-world = Orbital yard over {$world}
wf-yard-ui-berths = Builds
wf-yard-ui-no-jobs = No builds are laid down.
wf-yard-ui-job = {$design} for {$company}
wf-yard-ui-scrap = Scrap
wf-yard-ui-stage = Stage {$stage} of {$stages}: {$name}
wf-yard-ui-crates = Ship Components {$components}/{$componentsNeeded}, Metals {$metals}/{$metalsNeeded}
wf-yard-ui-designs = Designs
wf-yard-ui-design-cost = {$stages} stages, {$components} Ship Components and {$metals} Metals crates in all
wf-yard-ui-licensed = Your company holds its licence.
wf-yard-ui-order = Lay down

# Colony contractors and their contracts.
wf-contractor-colony-supplies = Colony provisioners
wf-contractor-colony-supplies-desc = A colony running short of supplies, buying food and water off haulers.
wf-contractor-colony-supplies-employer = the colony quartermaster
wf-contractor-colony-medical = Colony infirmary
wf-contractor-colony-medical-desc = A colony running short of medicine, buying dressings and kits off haulers.
wf-contractor-colony-medical-employer = the colony infirmary
wf-contract-colony-provisions = Colony provisions
wf-contract-colony-provisions-brief = The colony's shelf is running dry. Bring food and water down to any of its outposts.
wf-contract-colony-medical = Colony medical relief
wf-contract-colony-medical-brief = The colony's infirmary is short. Bring dressings and medkits down to any of its outposts.

# Entities.
ent-WFColonyDepositUranium = uranium deposit
    .desc = A seam of uranium ore. An extractor standing on it works it for the colony.
ent-WFColonyDepositVolatile = volatile deposit
    .desc = A seam of plasma-bearing rock. An extractor standing on it works it for the colony.
ent-WFColonyDepositBiomass = flesh seep
    .desc = A seep where Carcinoma's flesh wells up through the ground. An extractor standing on it renders it down for the colony.
ent-WFColonyExtractorPack = extractor pack
    .desc = A packed colony extractor. Use it in hand facing your outpost's floor, over a deposit, to set it up there.
ent-WFColonyExtractor = colony extractor
    .desc = Works the deposit under it for the colony while powered and someone is home.
ent-WFColonyExchangeVendorPack = colony exchange vendor pack
    .desc = A packed colony exchange vendor. Use it in hand facing your outpost's floor to set it up there.
ent-WFColonyExchangeVendor = colony exchange vendor
    .desc = Sells off the world's colony shelf. Every exchange on the world shares the same stock.
ent-WFColonyExchangeChutePack = colony exchange chute pack
    .desc = A packed colony exchange chute. Use it in hand facing your outpost's floor to set it up there.
ent-WFColonyExchangeChute = colony exchange chute
    .desc = Buys trade crates for the world's colony shelf, and what a farm yields: crops, eggs and hides, and milk and wool out of a bucket.
ent-WFColonyDepotVendor = colony exchange vendor
    .desc = Sells off the world's colony shelf, from the depot's own stores.
ent-WFColonyDepotChute = colony exchange chute
    .desc = Buys trade crates for the world's colony shelf, and what a farm yields: crops, eggs and hides, and milk and wool out of a bucket.
ent-WFColonyOfficeTerminal = charter office terminal
    .desc = The colonial register for this world: its standing, its holders and its charter. The governing company works its charter from here.
ent-WFColonySeatBeacon = colony beacon
    .desc = A tall mast with a steady light over the colony's landing field.
ent-WFColonyHallBeacon = governor's beacon
    .desc = The mast before the governor's hall. Its light is the colour of the company that holds the charter.
ent-WFColonyColonist = colonist
    .desc = One of the people who came out to make a living on this world.
ent-WFColonyImportPod = import pod
    .desc = The empty capsule a colony import came down in.
ent-WFColonyYardKit = yard platform kit
    .desc = A packed orbital yard platform. The governor of a Dominion world uses it in hand in the world's orbit to deploy it.
ent-WFColonyYardConsole = yard console
    .desc = Lays down builds on the yard's berths and shows how they are coming along.
ent-WFColonyYardChute = yard chute
    .desc = Takes Ship Components and Metals crates for the builds on the yard.
