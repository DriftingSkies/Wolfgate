# NpcCrew

Crew Setup has a four-step creation flow (ship, roster, rules, review) and an active-crew sidebar. Ship, vessel and
equipment lists are searchable; post coordinates and equipment overrides expand per member. Selecting a live crew
opens its mission queue, settings and separate admin tools. Add-to-end preserves progress; explicitly editing a
draft enables reorder/remove/replace. Each crew keeps its draft across selection and automatic refresh. Refreshes
preserve dropdown buttons, filters and unfinished clicks; polling runs on display frames rather than prediction
replays. Security settings can be changed without replacing the queue;
unauthorized boarding and docking independently support Ignore, Warn and Hostile. Authorization uses company
membership where available, otherwise a shared NPC faction, unless the system commanding the crew's home grid
answers `WGCrewAuthorizeEvent` first (ShipCommand does, by the visitor's standing and zone, and may set the
response per visitor). Likewise `WGCrewForgiveEvent` on the home grid lets the ship forgive whoever hurts, shoves or
robs a crewman (ShipCommand forgives the Boss): the deed is then neither remembered, answered nor reported, upstream
`NPCRetaliationSystem.TryRetaliate` included. A crew's ordered docking destination is authorized
for that approach. Incoming ships do not cause the stationary crew to announce that it is docking. Radio officers
announce their own approach and completed docking, and warn unauthorized arrivals.

`WGCrewSparedSystem` marks player vessels a crew takes for its own side: a hull showing the crew's faction on its IFF,
or, running dark or under another flag, one with a living, uncuffed player of that faction aboard. They are never
warned, hunted or zoned, and their fire is answered only past `OwnFactionStrayHits` (40) hits in 30 s.

A profile can name datasets its crews' first and last names come from (`firstNames`, `lastNames`) and components every
crewman gets (`components`), so a faction's people sound like one: Union crews carry Russian names and a Russian accent,
Federation and Dynasty crews their own names, pirates nicknames and a pirate's drawl. Each navy's loadouts wear its own
headset, with the faction channel's key, and its own service ID cards (TSFMC, PDV and USSP ranks; pirate and mercenary
cards for those crews); every crew card carries the crewman's name and role title.
A profile can give its roles titles of its own (`titles`, by role: the ADS calls its pilot a Helm Unit, not a First
Officer), used on the crewman's name and card in place of the role's. A profile can also swap hull parts out (`hullSwaps`): the encounter system applies them to a ship's grid before anything
goes aboard, so pirates never fly durasteel (any durasteel plating becomes plain plastitanium in place,
anchored and turned as it was). Every profile that kits its crew as pirates (the Reavers, corsairs and fugitives) must
swap all of it; `PirateProfilesNeverFlyDurasteel` checks this.

An engineer (`WGCrewEngineer`) is posted beside the plant on every encounter ship whose roles allow one. The ship
arrives commissioned (generators full and lit, antimatter engine jarred and injecting, fission reactor fuelled with
cerenkite rods in the slots its prefab leaves for them, its coolant loop charged with nitrogen once its pipes appear
and its control rods half out, batteries charged) with fuel for the trip stowed in a `WGCrewFuelStores` crate in the
hold: stacks, jars, and a full change of fuel rods. On a ship with an engineer, from then on nothing tops the plant
up but him: a generator under
ten units of fuel, a reactor jar under 200 or a spent fuel rod sends him to the crate for a stack, a jar or a rod,
which he feeds in by hand (the spent rod comes out onto the deck), and a plant with fuel in it that is off he switches
on. From his post he also trims the fission reactor's control rods to its casing temperature, in when it runs hot
and out when it runs cold, and all the way in when the fuel is spent, the loop dry or the casing overheating; and
every second he trims the turbine's stator load onto its best speed (up while it runs fast, in big steps while it
overspeeds, down while it runs slow, halved when it stalls), its flow rate set at commissioning to the textbook
100 L/s per gas channel plus 200. No
stores, or no living engineer, and the ship goes dark; a ship stranded for fuel gets neither. A fighter whose roles
leave no room for an engineer tops its own generators up whenever they run low, as long as any of its crew live.
A persistent ship (a Reaver hub) is stocked to its prototype's `fuelStores` jars and stacks a generator (12 for the
hubs: about eight hours of an AME at 0.6 units a second), and every five minutes `RestockFuel` tops its crate back up to
two while its engineer lives. With no living engineer left it is refuelled like a fighter.
Crewmen carry no radiation receiver, so standing by a reactor costs them nothing.

Posts keep to breathable deck where a hull has any. A helm or gunnery console with no air beside it (an open
cockpit), or a hull with no air at all, is manned from open deck instead, and a crewman whose post has no safe air is
spawned in a void kit (`WGCrewVoidKit`, gear `WGCrewVoidKitGear`): a pressure suit, helmet and mask over whatever
his loadout wore there, and a tank that is topped up for as long as he wears it. Crew with `WGCrewSuitUp` (the pirate
profile) get the same kit when they first stand on a tile with bad air, and the pirate profile is also immune to
pressure and breathing. `WGCrewEvaSystem.IsImmune` treats such crew as always ready for EVA: they never shout "air",
drop a job over an empty tank, or sleep-wake for bad air.

An attacking ship presents its guns. Every 30 s `WGPilotDutySystem.GunBearings` counts, for 16 bearings round the
hull, the guns with a clear line of fire past their own ship (fire control's own test); inside 1.5 times its attack
orbit the pilot holds the best armed bearing on the target, taking the nearer of equal sides, instead of flying nose
along the orbit. A hull whose guns bear equally all round flies as before.

Conduct aboard (`WGCrewConductSystem`): a crew that does not already fight strangers and is not in a fight tells a
player it sees with a gun in hand to put it away, and turns on him if it is still out after 10 s; holstered in time,
nothing comes of it. Anyone the ship's command forgives (`WGCrewForgiveEvent`) or welcomes where he stands
(`WGCrewAuthorizeEvent`) may carry a gun aboard; a ship with no command keeps the rule for everyone. Items aboard when
an encounter ship is set up are marked `WGCrewPropertyComponent`; a player seen by one of its fighters picking one up
is fought at once, unless the ship forgives him. A pilot with `FleeAttackers` (traders) runs directly
away from the nearest attacking ship instead of circling it.

Stealth (`WGCrewStealthSystem`): crews do not see a mob whose stealth field is up (a stealth suit) or a ship whose
IFF is hidden (a cloak). `WGCrewWeaponSystem.CanSee` drops the mob, and the alert ship lists, zones, hunts and extort
marks drop the ship. A hostile act shows either to every crew for a while: a mob that attacks, strips or robs a crew
or fires a gun is seen for 60 s, and a ship reported attacking for 120 s. A theft in plain view is still noticed.

A Warn crew gives a stranger it notices aboard `WarnTime` (30 s) to leave; if he stays, or strikes any crewman, the
crew's fighters (on-sight crew, marines, the captain and radio officer; not the helm or the guns) take him on as
their own attacker, by the usual rules: in sight, and a hunt party sized by skill going after him unseen. A when-attacked hand answers only
his own attacker, and an attacker is remembered while he is aboard and seen or reported, then for 30 s more. Only a
blow to a crewman himself sends him straight after someone he cannot see. Crew who never fight, including the
cowards a profile's `cowards` chance makes (half the hauler deckhands), drop their work, run for the bridge and lie
on the deck there until the alarm is over. Off their post's grid crew answer an attacker in sight only when sent
there, on a job or a raid, or when he is their faction's enemy anyway (a raider carried off); a crewman who only
defends his ship goes home. Crew never show the SSD sleep icon, and spawn with their suit sensors off, so the crew
monitoring console lists players rather than every NPC in the sector; a player who takes the suit can turn them on.

Foot boarders must be seen within normal sight range through an unobstructed view before security reacts.
Personal AI devices, loose silicon brains and station AI cores/remote eyes are equipment, not boarders; physical
cyborg chassis and hostile NPC bodies still qualify. Crew relay sightings through their equipped Shortband
headsets; switched-off, removed or blocked radios do not share
contacts. Boarding alerts go to both the local and alert channels. Action chatter has a fifteen-second speaker
cooldown and a one-minute repeat cooldown. Deckhands and marines select safe deck patrol stops 45–90 seconds
apart, unless `WGCrewPatrolOrdersComponent` gives them a route to walk or, with none, keeps them at their post; other
roles keep their stations. A grid's `WGGunnerPolicyComponent` can hold its gunners to firing only once fired on, or
on whoever has fired on a ship docked to them (`WGCrewAlertSystem.Docked.cs`). Pilots and gunners maintain their
facing toward occupied consoles.
Pilots, gunners, captains and radio officers defend themselves only against personal attackers, even when their
engagement rule is On Sight. They actively return after displacement or combat; pilots retain their assigned helm.
A pilot leaves the controls to a player the crew authorize who works a helm of the ship, and his orders, dock timers
and waypoints included, wait until 3 s after they stop; a boarder at the helm only shares the input with him.
Injury and casualty reports require a witness or working headset reception on the same ship.

Crew use their equipped ammunition and weapons, then unarmed combat, without scavenging empty loose guns.
They stop pursuing hostiles that leave their ship. Assigned supply/salvage trips remain allowed, and displaced
crew return home when work ends. Departure waits for living autonomous crew to return aboard, then releases
the docks and backs clear before resuming the next flight order. Long docking approaches face the flight path;
the final docking corridor still uses the required port alignment and may involve lateral thrust.

Escort Grid assigns separate staggered formation slots behind and beside the leader, with spacing enlarged for
hull clearance. Slots move and rotate with the leader, and escorts match its heading. Captain reactions preserve
the slot when resuming an interrupted escort. Connected escorts and their protected ships share incoming hull
and shield attacks across crew groups and companies. This includes an uncrewed protected freighter and chained
escorts; temporary evasion and losing a captain or pilot do not break the assignment while other crew survive.
Explicit replacement orders, pausing, skipping or cancelling the escort end its membership. Formation ships
are excluded from gunner targets and protected from crew cannon fire, including shield capacity. Follow Grid remains the simpler
proximity-following order and does not join this defensive alliance. Weapon locks alone do not trigger hostility.

Circle Grid slowly orbits the destination's bounds center. Default cruise, circle and attack limits are 10, 4 and
6 m/s; attack defaults to at least 350 m. Pilots use gentle thrust and turning, full braking, hull clearance and
stopping distance. Hold retains a fixed anchor and brakes small drift instead of continually steering around it.
A pilot with `FaceThreats` (a mission setting) turns a holding hull in place so its best armed side faces the ship that
attacked it, or else the nearest zone intruder the crew was told of; it never flies at them.
Flight tuning in creation/live settings exposes 37 speed, handling, clearance and docking values. Reusable
`wgCrewNavigation` profiles supply scenario defaults; each mission gets an independent copy. Changes apply to
the current pilot without restarting the queue. A cruise, follow, attack or near-target speed of 0 sets no limit
(the `WGCrewNavigationHunter` profile): the pilot flies as fast as the hull's thrust allows, and travel budgets count
on its `BaseMaxLinearVelocity`. See `Docs/_WG/NpcCrew/navigation.md` for scenario configuration.

Autonomous crew projectiles pass through allies; allied damage is also stopped before wound routing, armor or
retaliation. Crews holster when no living hostile remains. Forward travel faces the destination, and pilots face
their console when taking the helm. Spawned crew register ordinary, revocable ID credentials even on map-loaded
ships that do not yet have ship access management.
A gunner's guns hold fire while any ship its crew does not count as hostile lies between them and the aim point
(`WGGunnerDutySystem.LineOfFire.cs`, a ray per gun every half second), so bystanders and friends are not shot through;
a company whose `wgStanding` has `recklessFire` (the Reavers) fires through anything.
Crew-operated cannon rounds also protect allies and allied hulls, retaining their firing origin after a gunner dies
or a player takes over. Explicit Attack orders, incoming fire and hostile docking permit retaliation against
the targeted ship, including a normally friendly company; escorts in the same formation remain protected. A cannon
round that passes an allied hull spares everyone aboard it too, attackers included, so nobody is shot through a
standing wall once their ship stops counting as hostile. Before a
hit from a vessel of another faction counts as an attack, the crew ask (`WGCrewFriendlyFireQueryEvent`) whether anyone
vouches for it as a friend of the ship; the encounter it is fighting in does, by alliance or by whom it has been
shooting, and a friend's fire is let pass outright.

Repair orders assign deckhands physical work through HTN navigation. Deckhands have a built-in unlimited SRD and
repair tool, disabled permanently on death; no unlimited item drops. The ship's existing SRD snapshot is preserved,
or initialized when its first crew member spawns. Repair restores missing snapshot tiles/structures and uses normal
timed repairs for repairable equipment. Resupply docks and retrieves loose ammunition and filled oxygen tanks;
Salvage docks and retrieves loose material stacks. Workers carry cargo back to their posts. Neither job fabricates
supplies, opens containers or dismantles structures. Workers bound for a docked grid walk to their own port, step
across and path on the other grid, and back the same way. Nothing is picked that has no clear deck beside it, and a
worker who gets no nearer for ten seconds (a railing in the way, or no path) gives that target up for the next.
Unreachable work times out visibly and can be retried with pause/resume. These jobs require a deckhand, a pilot for the objective queue, and suitable EVA equipment.
Repair skips snapshot entries their SRD cannot rebuild, respecting prototype and grid restrictions and tool modes.

Default crew loadouts include pressure suits, helmets, masks and finite oxygen tanks. Internals connect before
work and in unsafe air. Below the reserve threshold, crew replace the tank from a reachable loose spare or leave
work for safe air; once safe they can seek a filled tank on their home ship. Oxygen is not replenished magically.
Custom loadouts without suitable protection or air cannot start work orders.

Crew Setup also lists active crews and their remaining objectives, refreshing every two seconds. Select a crew
and build a queue of Hold, GoTo coordinates (repeat for patrol waypoints), Dock, Undock, Loiter or Circle at a grid, Escort,
Attack or Retreat to a grid. Append preserves the running task; Replace starts the edited queue from its first
entry. Edit, move up/down, remove, pause, resume and skip are available. Timed Hold/Loiter/Circle/Follow/Escort/Attack tasks use seconds;
zero means indefinite until skipped. Travel, retreat, dock and undock advance on arrival. Missing targets or a failed
dock pause the queue with a visible status; a replacement pilot can continue it. Immediate orders cancel the queue.
Queues last for the round and are scoped by ship and group. Attack explicitly assigns the gunner's target, including
a normally friendly grid. Incoming attacks temporarily override faction friendship for retaliation; vessel
threats expire after sixty seconds without further attacks.
An explicit hostile-docking response overrides faction friendship for that visitor. Captain interruptions during
automatic departure preserve the intended next order and its destination beyond the temporary undocking maneuver.

Crew AI stays awake away from player bodies (`KeepActive`, default true; opt out in VV for intentionally dormant
crew). Loaded guns close their bolt/chamber a round before fighting, and ammunition checks continue during combat.
Captains evade external ship threats using their profile's attack orbit and restore the interrupted course after all-clear;
onboard-only threats still cause a hold. Pilots use Mono projectile avoidance outside docking.
The final docking approach aligns the ship early and enters the prechecked port corridor without the destination's
collision-avoidance buffer pushing it away. Approach and settle have bounded retries. The physical Dredger/Drillsite
regression uses the real hulls and thrusters, with test power supplied, and never uses FTL.

Human-style NPC crew for ships: each NPC works a job at a post and drops it to fight, then goes back to work. Crew
carry real loadouts and keep the weapon holstered until there is a fight; a dead crewman's kit is the loot. Mappers
place crew with `WGCrewSpawnPoint*` markers; admins use `wg_crew` to plan a grid (`plan here`), spawn the plan
(`spawn here`), spawn one role where they stand (`spawnrole deckhand`), list, clear a group, change a duty, give a
pilot orders (`orders <mob> hold | goto <x> <y> ... | loiter <x> <y> <radius> | follow <grid|here> | dock <grid|here> |
undock`) or set what a radio officer calls the ship (`callsign <mob> <text...>`).

Crew planning and setup spawning require clear flooring with safe pressure, temperature, oxygen and low
contamination. Unsafe marker posts are omitted. Dock and Follow orders expose a Destination grid picker;
select another grid on the same map, then apply orders. Refresh preserves the destination selection.

Spawn `WGMobCrewGunner` from the entity menu for a ready-to-work gunner, or use `wg_crew spawnrole gunner`
to assign crew membership and ship credentials. Crew Setup and gunner markers use the same NPC. It carries
the officer loadout and operates a powered gunnery console against reported hostile ships.

Built so far: the crew core (`WGCrewSystem`, `WGCrewComponent`, the `WGCrewCompound` HTN root with fight, duty and
idle branches), weapons (`WGCrewWeaponSystem` with the draw and holster operators), the planner (`WGCrewPlannerSystem`:
markers win, else a pilot and radio officer beside the helm, a deckhand inside each airlock and the rest on open
deck), role prototypes (`wgCrewRole`) for deckhand, marine, pilot, radio operator, captain and gunner, the pilot duty and the
command. The pilot (`WGPilotDutySystem`, `WGPilotDutyComponent` on every officer) walks to the nearest powered helm,
takes it and flies its orders (Hold, GoTo waypoints, Loiter, Follow, Dock, Undock) with Mono's ship steering; the
orders advance in the system, so the ship keeps flying while the pilot's HTN sleeps, and the helm is let go when he
dies, is taken over, loses the console or leaves for a fight. Dock is flown by hand (`WGPilotDutySystem.Docking.cs`):
plan a free dock pair with a clear approach lane, fly to a standoff 40 m out from the target dock, settle, creep in and
connect the moment the docks line up, retrying up to three times and then holding (`WGPilotDockedEvent`,
`WGPilotDockFailedEvent`); `wg.crew.dock_ftl_fallback` docks by FTL instead of giving up. Undock releases every port
and backs off. The radio officer (`WGRadioOperatorSystem`, `WGRadioOperatorComponent`, added by his role's
`components`) stands guard at his post and reports what happens to his ship as himself: docking, undocking, jumps,
arrival, orders flown or docking aborted on Shortband; a mayday on the first hostile act of an attack (a crewman hurt
from outside the crew, or a hostile mob aboard), the boarding call, the captain or pilot going down and the all-clear
two minutes after the last hostile activity on Broadband (and a faction channel if set). Events only, no sitreps; he
goes quiet when he is down. Crew retaliation catches incoming damage before Wolfmed routes it through body parts, using upstream faction checks
and attack memory so officers fight back when hit. Captains evade external threats or hold for boarders, then resume the remaining
route afterward; newer orders, death, player takeover, or leaving the crew cancel that saved course.
Shared alerts (`WGCrewAlertSystem`) let living on-sight crew aboard the same ship and group share targets once a
second. Awareness expands across the ship until 60 seconds without a target; crew leaving the group or switching
to when-attacked engagement recover their original awareness. `ShareAlerts` on the crew component opts out.
Officers are excluded, and `WGCrewAlertEvent` / `WGCrewAlertClearedEvent` expose transitions for future duties.
Crew use normal access readers before trying to pry a door. On ShipAccess-managed grids, `SpawnCrewman` equips
and registers an ordinary ID card through `WGCrewAccessSystem`; removing the card or revoking its allow-list entry
removes that access. Movement and duty changes never enroll crew on another ship. Unmanaged doors use the crew's
configured access tags and cards. The access-aware steering partial lives in this module, with small marked hooks
in upstream navigation.
Crew (`NavShove`) plan through loose crates at a cost, so they go round one when they can; one on their path is
braced against for a second and shoved onto a free tile beside it, never ahead or back onto them and never into a
doorway (`NPCSteeringSystem.Shove.cs`). The pathfinder marks points held only by unanchored pullables as `Movable`.
Encounter cargo and raiders' stashes go on `CargoTiles`: open deck all round first, else a tile that keeps the deck
joined and blocks no door or machine.
Reloading uses real compatible magazines from equipment slots through normal item interaction. Spent guns are
holstered in favor of a loaded sidearm, melee weapon, or empty hands; no ammunition is created, unless the crewman has
`InfiniteAmmo` (a commanded ship's roster sets it), when his magazine refills in place. Two-handed guns are wielded
when drawn and after a reload, for their wielded spread.

Skill (`WGCrewSkills`) sets how crew fight as well as how well. The crew's skill is the baseline; a profile's
`skillOffsets` raise or lower it per loadout (reaver hands a tier below, officers a tier above), clamped to
Green..Elite. A crewman who regains sight of a target he saw within six seconds, or braced for, fires after his
re-acquire delay instead of the full shoot delay (`NPCCombatSystem.Crew.cs`). From Regular up, a crewman who knows of
a hostile aboard but may not hunt it braces: weapon drawn and loaded, facing its way, for up to 90 s without sight of
it (`WGCrewWeaponSystem.Brace.cs`). Regulars and up also throw a grenade they carry (any slot item with a use timer;
reaver riflemen carry one `ExGrenade`) at a known hostile out of sight for three seconds within nine tiles: shouted
first, cooked by skill but always with 1.5 s of fuse left on landing, never with a crewmate or bystander in the
blast, once per crew every 20 s, and nobody hunts into it until it has gone off (`WGCrewGrenadeSystem`). The cooldown
is checked again as he arms it, and the blast and flight path (anything a thrown item hits, loose crates included)
again as it leaves his hand: with someone or something now in the way he picks
another spot, or holds on while the fuse allows. Veterans and
elites hunt in pairs, sidestep between shots (upstream `NPCJukeComponent`) and, once a minute, fall back out of the
hostile's sight past 50 damage to patch up for up to eight seconds (`WGCrewFallBackSystem`).

Since the Lupercal's first playtest: a crewman who sees a trespasser on a Warn ship names him and the zone aloud,
points him out (`PointingSystem.TryNpcPoint`) and gives him his time, gives a last warning ten seconds before it runs
out and says he is engaging when the fighters turn (`WGCrewSecuritySystem.Challenge.cs`). A player seen pulling or
carrying one of the crew's downed or dead is told to put him down and has ten seconds (`WGCrewConductSystem.Bodies.cs`).
Crew out of a fight patch their own wounds, one item every eight seconds, and anyone hurt past 40 damage counts as
calling for a medic (`WGCrewMedicSystem.SelfCare.cs`). Crew out of a fight stop short of a burning tile rather than walk
through it (`NPCSteeringSystem.Fire.cs`). A downed crewman stops bumping doors open, a dead one's card comes off his ship's allow list and out of
its zone keys, and both come back on revival with the door lists it was on (`WGShipAccessServerSystem.SuspendEntry`); a card
swapped while he was down replaces the old one. Janitors (`WGCrewJanitor`, duty Janitor) mop puddles on their own ship
(`WGCrewShipPuddles`, scored by `WGTargetOnHomeGridCon`) and wash dirt off the floor across it with their own hands, as a cleanbot does, and wait at their post when it's clean.
Hull impacts from another ship's artillery raise crew alerts and a radio mayday through the existing projectile
handler. Gunners walk to an available powered console and use Mono's ship targeting and fire-control path only
against explicitly reported hostile vessels, excluding friendly factions. Console loss, incapacitation, player
takeover, leaving the post, or changing duty stops their targeting.
Admins open Crew Setup from the Wolfgate tab or a crew NPC's admin verb. Choose a ship (or spawn a vessel), plan and
edit posts, roles, loadouts and engagement, then set company, NPC faction, radio channels and pilot orders. Preview
shows posts only to the requesting admin for 30 seconds; clear affects only the selected grid and group.
`WGCrewSetupSystem.TrySpawn` is the validated encounter-facing roster and mission API;
`WGCrewAlertSystem.ReportShipThreat` supplies encounter vessel threats. RES itself remains a separate project.

`design.md` under `Docs/_WG/NpcCrew/` is the full brief: pilot duty and orders, docking by hand, the event-driven
radio officer, crew alerting, the access-door edit and the Crew Setup admin window.

Medics (`WGCrewMedicComponent`, roles `WGCrewParamedic` and `WGCrewSurgeon` in `medics.yml`) treat people with
`WGCrewMedicSystem`. Once a second a free medic takes the patient most in need on his home grid or a ship docked to
it: the dead who can be brought back and hearts in arrest, then the unconscious, the downed and bleeding, the downed,
anyone who called for help, and anyone lying in a bed that heals. A paramedic takes anyone not yet in a bed, and those
in beds with minor injuries; a surgeon works the beds. `WGCrewMayTreatEvent` on the home grid lets the ship refuse
someone (ShipCommand refuses the barred and the crew's attackers), and a medic never treats whoever attacked him.
Each step reads the patient as they are and is a do-after the medic stands still for: shock a heart that can be
restarted (`WolfmedRevivalSystem.TryDefibrillate`; a paramedic gives three shocks on the spot), CPR for an arrest
that can't, a tourniquet on a limb's undressable bleed, gauze on any other bleed, epinephrine for the unconscious and
dylovene above a toxin load (each at most once a minute), then brutepack, medicated suture and regenerative mesh, applied through
`WoundHealingSystem.TryApplyHealing` with healing items the medic keeps off the map, so his kit never runs out. A
paramedic patches minor injuries where the patient lies; anyone downed or with more damage than `SeriousDamage` he
drags to the nearest free medical bed (`WGMedicalBedComponent`, on the medical and stasis beds; any bed that heals on a
grid with none) (or the unrevivable dead to a morgue drawer) once the bleeding and the heart
are seen to, and the surgeon takes it from there. The shock count and the dose clocks are the patient's
(`WGCrewPatientCare`), whichever medic gave them, so letting a patient go and taking them again repeats nothing. With
no free bed or drawer a paramedic does what he can where the patient lies and then leaves them for 30 s, and a body
waiting so comes after every living patient. A medic who gets no nearer his patient, or their bed, for 30 s gives them
up and leaves them be for a minute. The HTN branch (`WGCrewMedicOperator`) sits after the fight and before other
work; `WGCrewTreatedEvent` is broadcast after each treatment.

The surgeon also runs the autodoc (`WGCrewMedicSystem.Surgery.cs`). A bed patient who lacks a limb or an organ, or who
is down, dead but revivable (or only lacking blood or a heart) or badly hurt with work on the pod's triage plan
(`AutodocSystem.Plan` with `preview`, which shocks and transfuses nobody), is taken off the bed to the nearest free,
powered autodoc on his home grid and put in it. Before each run he fills the pod's reservoirs (an empty slot gets the
bottle from `Reservoirs`, anything there is topped back up) and puts the species' own missing part or organ, from the
patient's body prototype, in its tray, which is what lets the pod plan the replacement; then it plans its whole triage
and he starts it. He answers it when it waits for a part, starts another run while it still finds work (up to
`OperationsPerPatient`), and then takes the patient out and back to a bed, where they rest a while before he looks at
them for the pod again. Brains are never replaced. Assisting is a later phase.

When every pod on his home grid is busy or there is none, he operates by hand (`WGCrewMedicSystem.Table.cs`): the same
patients, on the nearest free operating table. He works from `Instruments`, an off-map autodoc whose library and triage
are what he knows and whose tools, tray and reservoirs are his, answering `WolfmedSurgeryToolsEvent` while he is at the
table. Each planned procedure runs step by step, a do-after per step with the real Shitmed step behind it
(`SharedSurgerySystem.WolfmedPerformStep` with `byHand`); he puts the patient under by the pod's rules, takes off
whatever is in the way whole, and gives a shock or dresses bleeding on a part he hasn't opened before going on. A step
that changes nothing three times, or a procedure that can't go on, is given up; `TableProcedures` caps the procedures
per patient. Then the patient goes back to a bed. Paramedics and the surgeon no longer count an operating table as a
bed to carry someone to.

And he clones from the ship's genetic records (`WGCrewMedicSystem.Cloning.cs`). A living player can use the verb on
him to be put on file: he takes their genetic record into the genetic database on his home grid; the same verb takes
them off file, deleting the record. Records made at the body scanner count the same. When a player on file is
cloneable he starts a free cloning pod aboard: at once if their body is gone or past reviving, otherwise after
`CloneAfter` (two minutes) with no medic working on it. The cloner still asks the player before they wake in the new
body; a clone they turn down or leave unanswered is tried once more after ten minutes, then not again until they are
alive.

## Local playtesting

Run `Tools/_WG/NpcCrew/Start-TestPair.ps1` from PowerShell to build and open an isolated local pair on port 1221.
Server and Client use sibling directories `bin/NpcCrewPlaytest.Server` and `bin/NpcCrewPlaytest.Client` to preserve
their dependency versions and the engine's development resource paths. `-BuildName` changes their shared prefix.
It uses the optimized `Tools` configuration, preserving admin tools without Debug physics-ray broadcasts, and
disables simulated latency, loss and duplication on both endpoints. Client sandboxing and UI diagnostics remain
enabled; mob pushing is disabled. Logs go to timestamped `wgcrew-playtest-*` files in TEMP. Existing
`TEMP/wgcrew-navigation-server-data` is preserved. Use `-SkipBuild` for matching existing binaries or supply
`-Port` and `-DataDirectory` for a separate session. The launcher refuses occupied ports and live output/data
directories, builds serially, and waits up to 60 seconds for server readiness before opening the client.

Debug ray traffic can delay reliable menu and Ghost Warp replies behind NPC visibility checks while world state
continues updating. A live 2048-ray probe delayed a Ghost Warp reply by 5.37 seconds with simulated networking,
versus about 0.2 seconds with simulation disabled. Longer combat playtesting remains necessary.

A ship flown by the simulation raises `WGCrewVirtualDockedEvent` when it docks for a Dock order, the
simulated counterpart of `WGPilotDockedEvent`.

An encounter ship's own passengers (`WGEncounterPassengerComponent`) are authorised aboard it whatever their faction, so a
crew never turns on what it carries, such as the Remade aboard a flesh ship.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/NpcCrew/Commands/WGCrewCommand.Arena.cs`](Commands/WGCrewCommand.Arena.cs)
- [`Content.Server/_WG/NpcCrew/Commands/WGCrewCommand.cs`](Commands/WGCrewCommand.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCaptainComponent.cs`](Components/WGCaptainComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewArmedGrenadeComponent.cs`](Components/WGCrewArmedGrenadeComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewComponent.cs`](Components/WGCrewComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewEngineerComponent.cs`](Components/WGCrewEngineerComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewGrabberComponent.cs`](Components/WGCrewGrabberComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewHuntedHullComponent.cs`](Components/WGCrewHuntedHullComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewMedicComponent.cs`](Components/WGCrewMedicComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewNonLethalComponent.cs`](Components/WGCrewNonLethalComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewOnSightComponent.cs`](Components/WGCrewOnSightComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewPatrolOrdersComponent.cs`](Components/WGCrewPatrolOrdersComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewPropertyComponent.cs`](Components/WGCrewPropertyComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewRadioComponent.cs`](Components/WGCrewRadioComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewRepairComponent.cs`](Components/WGCrewRepairComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewSecurityComponent.cs`](Components/WGCrewSecurityComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewSeizedHullComponent.cs`](Components/WGCrewSeizedHullComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewShipFireComponent.cs`](Components/WGCrewShipFireComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewShipProfileComponent.cs`](Components/WGCrewShipProfileComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewSpawnPointComponent.cs`](Components/WGCrewSpawnPointComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewSuitUpComponent.cs`](Components/WGCrewSuitUpComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewSurrenderedComponent.cs`](Components/WGCrewSurrenderedComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewVirtualShipComponent.cs`](Components/WGCrewVirtualShipComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewVoidKitComponent.cs`](Components/WGCrewVoidKitComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGCrewWeaponComponent.cs`](Components/WGCrewWeaponComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGGunnerDutyComponent.cs`](Components/WGGunnerDutyComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGGunnerPolicyComponent.cs`](Components/WGGunnerPolicyComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGHoldFireComponent.cs`](Components/WGHoldFireComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGPilotDutyComponent.cs`](Components/WGPilotDutyComponent.cs)
- [`Content.Server/_WG/NpcCrew/Components/WGRadioOperatorComponent.cs`](Components/WGRadioOperatorComponent.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewBraceOperator.cs`](HTN/WGCrewBraceOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewFallBackOperator.cs`](HTN/WGCrewFallBackOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewGrabOperator.cs`](HTN/WGCrewGrabOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewGrenadeOperator.cs`](HTN/WGCrewGrenadeOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewLoadedGunPrecondition.cs`](HTN/WGCrewLoadedGunPrecondition.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewMayFightPrecondition.cs`](HTN/WGCrewMayFightPrecondition.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewMedicOperator.cs`](HTN/WGCrewMedicOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewReturnOperator.cs`](HTN/WGCrewReturnOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewSidestepOperator.cs`](HTN/WGCrewSidestepOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewTargetOperator.cs`](HTN/WGCrewTargetOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGCrewWorkOperator.cs`](HTN/WGCrewWorkOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGDrawWeaponOperator.cs`](HTN/WGDrawWeaponOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGHolsterWeaponOperator.cs`](HTN/WGHolsterWeaponOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGPickGunneryOperator.cs`](HTN/WGPickGunneryOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGPickHelmOperator.cs`](HTN/WGPickHelmOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGReloadOperator.cs`](HTN/WGReloadOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGTakeGunneryOperator.cs`](HTN/WGTakeGunneryOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGTakeHelmOperator.cs`](HTN/WGTakeHelmOperator.cs)
- [`Content.Server/_WG/NpcCrew/HTN/WGTargetOnHomeGridCon.cs`](HTN/WGTargetOnHomeGridCon.cs)
- [`Content.Server/_WG/NpcCrew/NPCSystem.Sleep.cs`](NPCSystem.Sleep.cs)
- [`Content.Server/_WG/NpcCrew/Systems/FireControlSystem.Crew.cs`](Systems/FireControlSystem.Crew.cs)
- [`Content.Server/_WG/NpcCrew/Systems/NPCCombatSystem.Crew.cs`](Systems/NPCCombatSystem.Crew.cs)
- [`Content.Server/_WG/NpcCrew/Systems/NPCRetaliationSystem.Crew.cs`](Systems/NPCRetaliationSystem.Crew.cs)
- [`Content.Server/_WG/NpcCrew/Systems/NPCSteeringSystem.Access.cs`](Systems/NPCSteeringSystem.Access.cs)
- [`Content.Server/_WG/NpcCrew/Systems/NPCSteeringSystem.Fire.cs`](Systems/NPCSteeringSystem.Fire.cs)
- [`Content.Server/_WG/NpcCrew/Systems/NPCSteeringSystem.Shove.cs`](Systems/NPCSteeringSystem.Shove.cs)
- [`Content.Server/_WG/NpcCrew/Systems/NuclearReactorSystem.NpcCrew.cs`](Systems/NuclearReactorSystem.NpcCrew.cs)
- [`Content.Server/_WG/NpcCrew/Systems/PathfindingSystem.Shove.cs`](Systems/PathfindingSystem.Shove.cs)
- [`Content.Server/_WG/NpcCrew/Systems/PointingSystem.NpcCrew.cs`](Systems/PointingSystem.NpcCrew.cs)
- [`Content.Server/_WG/NpcCrew/Systems/ShipSteeringSystem.Crew.cs`](Systems/ShipSteeringSystem.Crew.cs)
- [`Content.Server/_WG/NpcCrew/Systems/SpaceArtillerySystem.Crew.cs`](Systems/SpaceArtillerySystem.Crew.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCaptainSystem.cs`](Systems/WGCaptainSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewAccessSystem.cs`](Systems/WGCrewAccessSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewAlertSystem.cs`](Systems/WGCrewAlertSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewAlertSystem.Docked.cs`](Systems/WGCrewAlertSystem.Docked.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewAlertSystem.Mechs.cs`](Systems/WGCrewAlertSystem.Mechs.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewCommsSystem.cs`](Systems/WGCrewCommsSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewConductSystem.Bodies.cs`](Systems/WGCrewConductSystem.Bodies.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewConductSystem.cs`](Systems/WGCrewConductSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewEscortSystem.cs`](Systems/WGCrewEscortSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewEvaSystem.cs`](Systems/WGCrewEvaSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewFallBackSystem.cs`](Systems/WGCrewFallBackSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewFriendlyFireSystem.cs`](Systems/WGCrewFriendlyFireSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewGrabberSystem.cs`](Systems/WGCrewGrabberSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewGrenadeSystem.cs`](Systems/WGCrewGrenadeSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewMedicSystem.Cloning.cs`](Systems/WGCrewMedicSystem.Cloning.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewMedicSystem.cs`](Systems/WGCrewMedicSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewMedicSystem.SelfCare.cs`](Systems/WGCrewMedicSystem.SelfCare.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewMedicSystem.Surgery.cs`](Systems/WGCrewMedicSystem.Surgery.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewMedicSystem.Table.cs`](Systems/WGCrewMedicSystem.Table.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewObjectiveSystem.cs`](Systems/WGCrewObjectiveSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewObjectiveSystem.Virtual.cs`](Systems/WGCrewObjectiveSystem.Virtual.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewPlannerSystem.cs`](Systems/WGCrewPlannerSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewRammingSystem.cs`](Systems/WGCrewRammingSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewRoutineSystem.cs`](Systems/WGCrewRoutineSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewSecuritySystem.Challenge.cs`](Systems/WGCrewSecuritySystem.Challenge.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewSecuritySystem.cs`](Systems/WGCrewSecuritySystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewSetupSystem.cs`](Systems/WGCrewSetupSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewShelterSystem.cs`](Systems/WGCrewShelterSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewShieldProtectionSystem.cs`](Systems/WGCrewShieldProtectionSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewShipStatusSystem.cs`](Systems/WGCrewShipStatusSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewSleepSystem.cs`](Systems/WGCrewSleepSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewSparedSystem.cs`](Systems/WGCrewSparedSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewSpeechSystem.cs`](Systems/WGCrewSpeechSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewStealthSystem.cs`](Systems/WGCrewStealthSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewSystem.cs`](Systems/WGCrewSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewUiDiagnosticsSystem.cs`](Systems/WGCrewUiDiagnosticsSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewWeaponSystem.Brace.cs`](Systems/WGCrewWeaponSystem.Brace.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewWeaponSystem.cs`](Systems/WGCrewWeaponSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewWeaponSystem.Reload.cs`](Systems/WGCrewWeaponSystem.Reload.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewWorkSystem.cs`](Systems/WGCrewWorkSystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewWorkSystem.Engineer.cs`](Systems/WGCrewWorkSystem.Engineer.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGCrewWorkSystem.Repair.cs`](Systems/WGCrewWorkSystem.Repair.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGGunnerDutySystem.cs`](Systems/WGGunnerDutySystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGGunnerDutySystem.LineOfFire.cs`](Systems/WGGunnerDutySystem.LineOfFire.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGPilotDutySystem.cs`](Systems/WGPilotDutySystem.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGPilotDutySystem.Docking.cs`](Systems/WGPilotDutySystem.Docking.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGPilotDutySystem.Navigation.cs`](Systems/WGPilotDutySystem.Navigation.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGPilotDutySystem.Stance.cs`](Systems/WGPilotDutySystem.Stance.cs)
- [`Content.Server/_WG/NpcCrew/Systems/WGRadioOperatorSystem.cs`](Systems/WGRadioOperatorSystem.cs)
- [`Content.Server/_WG/NpcCrew/WGCrewEvents.cs`](WGCrewEvents.cs)
- [`Content.Server/_WG/NpcCrew/WGCrewSkills.cs`](WGCrewSkills.cs)

### Shared

- [`Content.Shared/_WG/NpcCrew/WGBodyToolSystem.cs`](../../../Content.Shared/_WG/NpcCrew/WGBodyToolSystem.cs)
- [`Content.Shared/_WG/NpcCrew/WGCompanyFamily.cs`](../../../Content.Shared/_WG/NpcCrew/WGCompanyFamily.cs)
- [`Content.Shared/_WG/NpcCrew/WGCrewEngagement.cs`](../../../Content.Shared/_WG/NpcCrew/WGCrewEngagement.cs)
- [`Content.Shared/_WG/NpcCrew/WGCrewLimits.cs`](../../../Content.Shared/_WG/NpcCrew/WGCrewLimits.cs)
- [`Content.Shared/_WG/NpcCrew/WGCrewMedicDoAfterEvent.cs`](../../../Content.Shared/_WG/NpcCrew/WGCrewMedicDoAfterEvent.cs)
- [`Content.Shared/_WG/NpcCrew/WGCrewNavigationProfilePrototype.cs`](../../../Content.Shared/_WG/NpcCrew/WGCrewNavigationProfilePrototype.cs)
- [`Content.Shared/_WG/NpcCrew/WGCrewNavigationSettings.cs`](../../../Content.Shared/_WG/NpcCrew/WGCrewNavigationSettings.cs)
- [`Content.Shared/_WG/NpcCrew/WGCrewProfilePrototype.cs`](../../../Content.Shared/_WG/NpcCrew/WGCrewProfilePrototype.cs)
- [`Content.Shared/_WG/NpcCrew/WGCrewRolePrototype.cs`](../../../Content.Shared/_WG/NpcCrew/WGCrewRolePrototype.cs)
- [`Content.Shared/_WG/NpcCrew/WGCrewSetupMessages.cs`](../../../Content.Shared/_WG/NpcCrew/WGCrewSetupMessages.cs)
- [`Content.Shared/_WG/NpcCrew/WGGunnerPolicy.cs`](../../../Content.Shared/_WG/NpcCrew/WGGunnerPolicy.cs)
- [`Content.Shared/_WG/NpcCrew/WGMedicalBedComponent.cs`](../../../Content.Shared/_WG/NpcCrew/WGMedicalBedComponent.cs)
- [`Content.Shared/_WG/NpcCrew/WGPilotOrder.cs`](../../../Content.Shared/_WG/NpcCrew/WGPilotOrder.cs)
- [`Content.Shared/_WG/NpcCrew/WGStripAttemptEvent.cs`](../../../Content.Shared/_WG/NpcCrew/WGStripAttemptEvent.cs)

### Client

- [`Content.Client/_WG/NpcCrew/ShuttleNavControl.NpcCrew.cs`](../../../Content.Client/_WG/NpcCrew/ShuttleNavControl.NpcCrew.cs)
- [`Content.Client/_WG/NpcCrew/WGCrewSetupClientSystem.cs`](../../../Content.Client/_WG/NpcCrew/WGCrewSetupClientSystem.cs)
- [`Content.Client/_WG/NpcCrew/WGCrewSetupOverlay.cs`](../../../Content.Client/_WG/NpcCrew/WGCrewSetupOverlay.cs)
- [`Content.Client/_WG/NpcCrew/WGCrewSetupWindow.Creation.cs`](../../../Content.Client/_WG/NpcCrew/WGCrewSetupWindow.Creation.cs)
- [`Content.Client/_WG/NpcCrew/WGCrewSetupWindow.cs`](../../../Content.Client/_WG/NpcCrew/WGCrewSetupWindow.cs)
- [`Content.Client/_WG/NpcCrew/WGCrewSetupWindow.Navigation.cs`](../../../Content.Client/_WG/NpcCrew/WGCrewSetupWindow.Navigation.cs)
- [`Content.Client/_WG/NpcCrew/WGCrewSetupWindow.Objectives.cs`](../../../Content.Client/_WG/NpcCrew/WGCrewSetupWindow.Objectives.cs)
- [`Content.Client/_WG/NpcCrew/WGCrewSetupWindow.Refresh.cs`](../../../Content.Client/_WG/NpcCrew/WGCrewSetupWindow.Refresh.cs)
- [`Content.Client/_WG/NpcCrew/WGCrewSetupWindow.Settings.cs`](../../../Content.Client/_WG/NpcCrew/WGCrewSetupWindow.Settings.cs)
- [`Content.Client/_WG/NpcCrew/WGCrewUiDiagnosticsSystem.cs`](../../../Content.Client/_WG/NpcCrew/WGCrewUiDiagnosticsSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGBodyToolTest.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGBodyToolTest.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewMedicTest.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewMedicTest.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewSuitHelmetTest.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewSuitHelmetTest.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewSurgeonTest.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewSurgeonTest.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Arena.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Arena.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.AuditCombat.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.AuditCombat.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.AuditNavigation.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.AuditNavigation.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.AuditRaid.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.AuditRaid.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.BatchCrew.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.BatchCrew.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.BoardingDevices.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.BoardingDevices.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.CaptainHold.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.CaptainHold.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.CaptainResume.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.CaptainResume.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Circle.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Circle.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.CombatFixes.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.CombatFixes.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Command.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Command.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.CrewFixes.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.CrewFixes.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Doors.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Doors.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.EscortHostility.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.EscortHostility.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.FireAndRepair.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.FireAndRepair.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Flight.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Flight.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.FlightFixes.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.FlightFixes.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.HoldFacing.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.HoldFacing.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.HullSwaps.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.HullSwaps.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.HunterSpeed.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.HunterSpeed.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.LineOfFire.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.LineOfFire.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Navigation.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Navigation.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.NavigationSettings.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.NavigationSettings.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Objectives.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Objectives.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.OnSight.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.OnSight.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.OwnKind.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.OwnKind.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.PirateAir.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.PirateAir.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.PryGiveUp.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.PryGiveUp.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.RadioSecurity.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.RadioSecurity.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Reload.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Reload.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Routines.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Routines.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SafetyTactics.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SafetyTactics.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Security.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Security.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Sensors.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Sensors.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.ServerNavigationSettings.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.ServerNavigationSettings.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Setup.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Setup.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SetupFixes.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SetupFixes.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SetupFocus.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SetupFocus.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SetupNestedMenus.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SetupNestedMenus.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SetupUi.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SetupUi.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SetupVerbMenu.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.SetupVerbMenu.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.ShieldAggro.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.ShieldAggro.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.ShipStatus.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.ShipStatus.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Shove.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Shove.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Skill.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Skill.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Sleep.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Sleep.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Spared.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Spared.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Stations.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Stations.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Tactics.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Tactics.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.TargetLifetime.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.TargetLifetime.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Titles.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Titles.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Work.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Work.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Wreck.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGCrewTest.Wreck.cs)
- [`Content.IntegrationTests/Tests/_WG/NpcCrew/WGNpcDownedShootingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/NpcCrew/WGNpcDownedShootingTest.cs)

### Unit tests

- [`Content.Tests/_WG/NpcCrew/WGTurbineTrimTest.cs`](../../../Content.Tests/_WG/NpcCrew/WGTurbineTrimTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/NpcCrew/ai_factions.yml`](../../../Resources/Prototypes/_WG/NpcCrew/ai_factions.yml)
- [`Resources/Prototypes/_WG/NpcCrew/companies.yml`](../../../Resources/Prototypes/_WG/NpcCrew/companies.yml)
- [`Resources/Prototypes/_WG/NpcCrew/gear.yml`](../../../Resources/Prototypes/_WG/NpcCrew/gear.yml)
- [`Resources/Prototypes/_WG/NpcCrew/htn.yml`](../../../Resources/Prototypes/_WG/NpcCrew/htn.yml)
- [`Resources/Prototypes/_WG/NpcCrew/markers.yml`](../../../Resources/Prototypes/_WG/NpcCrew/markers.yml)
- [`Resources/Prototypes/_WG/NpcCrew/medics.yml`](../../../Resources/Prototypes/_WG/NpcCrew/medics.yml)
- [`Resources/Prototypes/_WG/NpcCrew/mobs.yml`](../../../Resources/Prototypes/_WG/NpcCrew/mobs.yml)
- [`Resources/Prototypes/_WG/NpcCrew/names.yml`](../../../Resources/Prototypes/_WG/NpcCrew/names.yml)
- [`Resources/Prototypes/_WG/NpcCrew/navigation.yml`](../../../Resources/Prototypes/_WG/NpcCrew/navigation.yml)
- [`Resources/Prototypes/_WG/NpcCrew/profiles.yml`](../../../Resources/Prototypes/_WG/NpcCrew/profiles.yml)
- [`Resources/Prototypes/_WG/NpcCrew/roles.yml`](../../../Resources/Prototypes/_WG/NpcCrew/roles.yml)
- [`Resources/Prototypes/_WG/NpcCrew/stores.yml`](../../../Resources/Prototypes/_WG/NpcCrew/stores.yml)

### Localization

- [`Resources/Locale/en-US/_WG/NpcCrew/challenge.ftl`](../../../Resources/Locale/en-US/_WG/NpcCrew/challenge.ftl)
- [`Resources/Locale/en-US/_WG/NpcCrew/companies.ftl`](../../../Resources/Locale/en-US/_WG/NpcCrew/companies.ftl)
- [`Resources/Locale/en-US/_WG/NpcCrew/crew.ftl`](../../../Resources/Locale/en-US/_WG/NpcCrew/crew.ftl)
- [`Resources/Locale/en-US/_WG/NpcCrew/medics.ftl`](../../../Resources/Locale/en-US/_WG/NpcCrew/medics.ftl)
- [`Resources/Locale/en-US/_WG/NpcCrew/navigation.ftl`](../../../Resources/Locale/en-US/_WG/NpcCrew/navigation.ftl)
- [`Resources/Locale/en-US/_WG/NpcCrew/radio-reports.ftl`](../../../Resources/Locale/en-US/_WG/NpcCrew/radio-reports.ftl)
- [`Resources/Locale/en-US/_WG/NpcCrew/setup.ftl`](../../../Resources/Locale/en-US/_WG/NpcCrew/setup.ftl)

### Tools

- [`Tools/_WG/NpcCrew/Start-TestPair.ps1`](../../../Tools/_WG/NpcCrew/Start-TestPair.ps1)

### Docs

- [`Docs/_WG/NpcCrew/design.md`](../../../Docs/_WG/NpcCrew/design.md)
- [`Docs/_WG/NpcCrew/handoff.md`](../../../Docs/_WG/NpcCrew/handoff.md)
- [`Docs/_WG/NpcCrew/navigation.md`](../../../Docs/_WG/NpcCrew/navigation.md)

## Non-modular edits

- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs): Admin tags and battlegroup lines for NPC-crewed ships.
- [`Content.Client/Verbs/VerbSystem.cs`](../../../Content.Client/Verbs/VerbSystem.cs)
  - opt-in traces distinguish missing UI replies from stale player attachments.
  - trace the response after the active menu handles it.
- [`Content.Server/_Mono/FireControl/FireControlSystem.cs`](../../_Mono/FireControl/FireControlSystem.cs): distinguish NPC bursts from later manual fire.
- [`Content.Server/_Mono/NPC/HTN/ShipSteeringSystem.cs`](../../_Mono/NPC/HTN/ShipSteeringSystem.cs)
  - Escorts hold the leader's heading instead of the bearing to a nearby slot.
  - Keep destination-hull avoidance scoped to crew pilots.
  - Crewed ships avoid their orbit and escort targets as physical obstacles.
  - Identifies crew steering without changing other NPC navigation.
- [`Content.Server/_Mono/NPC/HTN/ShipTargetingSystem.cs`](../../_Mono/NPC/HTN/ShipTargetingSystem.cs)
  - preserve the controller for autonomous cannon damage.
  - carry the NPC controlling this burst; public so a gunner can lay part of the guns on a second target.
  - attribute crew bursts without changing the muzzle.
- [`Content.Server/_Mono/SpaceArtillery/SpaceArtillerySystem.cs`](../../_Mono/SpaceArtillery/SpaceArtillerySystem.cs): notify crew of damaging impacts from other ships.
- [`Content.Server/NPC/Pathfinding/PathfindingSystem.Common.cs`](../../NPC/Pathfinding/PathfindingSystem.Common.cs)
  - access-aware NPCs may plan through readers and check permission at the door.
  - a loose crate costs a shove, so crew go round it when they can.
- [`Content.Server/NPC/Pathfinding/PathfindingSystem.cs`](../../NPC/Pathfinding/PathfindingSystem.cs)
  - opt-in NPCs try their access before prying doors.
  - opt-in NPCs shove loose crates off their path.
- [`Content.Server/NPC/Pathfinding/PathfindingSystem.Grid.cs`](../../NPC/Pathfinding/PathfindingSystem.Grid.cs)
  - whether only loose crates block this point
  - one solid thing that is not a loose crate rules out shoving
  - mark points held only by loose crates; flags carry over between points, this must not
  - per-point flags
- [`Content.Server/NPC/Pathfinding/PathFlags.cs`](../../NPC/Pathfinding/PathFlags.cs): can we shove a loose crate out of the way.
- [`Content.Server/NPC/Systems/NPCCombatSystem.Ranged.cs`](../../NPC/Systems/NPCCombatSystem.Ranged.cs)
  - no shooting while Downed, crit, stunned or asleep
  - crew fire sooner on a target they saw moments ago or braced for
  - Crew miss by their skill level.
  - crew pull a semi-automatic's trigger at a person's pace
- [`Content.Server/NPC/Systems/NPCRetaliationSystem.cs`](../../NPC/Systems/NPCRetaliationSystem.cs): a crewman's ship may let an attacker off, such as its Boss
- [`Content.Server/NPC/Systems/NPCSteeringSystem.Context.cs`](../../NPC/Systems/NPCSteeringSystem.Context.cs)
  - a loose crate on the node is shoved, not walked into
  - compare both bodies for every NPC; the engine helper reads the first fixtures twice.
  - crew out of a fight hold short of a burning tile rather than walk through the fire
  - stop blended input while access-aware crew settle at an obstacle.
- [`Content.Server/NPC/Systems/NPCSteeringSystem.Obstacles.cs`](../../NPC/Systems/NPCSteeringSystem.Obstacles.cs)
  - crew shove a loose crate off their path
  - open authorized doors through normal interaction before considering prying.
- [`Content.Server/NPC/Systems/NPCSystem.cs`](../../NPC/Systems/NPCSystem.cs)
  - a mob someone plays, such as a possessing admin, has no AI to wake
  - ship crews with something to do must work even without nearby player bodies.
- [`Content.Server/NPC/Systems/NPCUtilitySystem.cs`](../../NPC/Systems/NPCUtilitySystem.cs): crew keep their chores on the ship they serve
- [`Content.Server/Shuttles/Systems/ShuttleSystem.Impact.cs`](../../Shuttles/Systems/ShuttleSystem.Impact.cs): crews take a ramming for an attack
- [`Content.Server/Verbs/VerbSystem.cs`](../../Verbs/VerbSystem.cs): opt-in tracing for missing menu replies after ghosting.
- [`Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs`](../../../Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs): Lets crew damage scaling skip the routed inner passes.
- [`Content.Shared/NPC/PathfindingBreadcrumb.cs`](../../../Content.Shared/NPC/PathfindingBreadcrumb.cs): nothing blocks it but loose crates that can be shoved aside.
- [`Content.Shared/Strip/SharedStrippableSystem.cs`](../../../Content.Shared/Strip/SharedStrippableSystem.cs): the one being stripped learns who is doing it
- [`Resources/Prototypes/Entities/Structures/Furniture/beds.yml`](../../../Resources/Prototypes/Entities/Structures/Furniture/beds.yml): NPC paramedics take patients to medical beds only
- [`Resources/Prototypes/Entities/Structures/Machines/stasisbed.yml`](../../../Resources/Prototypes/Entities/Structures/Machines/stasisbed.yml): NPC paramedics take patients to medical beds only

<!-- WOLFGATE-GENERATED END -->
