# NPC crew: behaviour review and expansion proposal

Status: proposal for the owner to review. Nothing here is built.

## How this was made

- Tree: worktree `Wolfgate-npc-crew`, branch `wg-npc-crew-life`, head `de8017c625`, read on 2026-11-24 with other
  agents' uncommitted work in it (EVA refill, pest hunting, janitor claims, surgeon clothes, fauna rule).
- Read in full by the author: `htn.yml`, `roles.yml`, `mobs.yml`, `gear.yml`, `medics.yml`, the NpcCrew README, and
  the crew core, alert, security, challenge, conduct, rally, weapon, reload, brace, shelter, comms, speech, sleep,
  routine, EVA, work, grabber and self-care systems, plus the roster's staffing code.
- Read by delegated agents and marked † below: the pilot, gunner, captain, radio, objective, escort and ship status
  systems; ShipCommand, ShipPa, NpcDialogue, NpcIntents, CrewBarks, Cowardice, NpcNav, NpcPry, NpcVehicleCrew,
  Dormancy, Cleaning, Traders; and the Lupercal's map contents. † items carry the delegate's line numbers and were
  not re-read, except where this document says so.
- Skimmed only: the engineer partial past its first 220 lines, muster, grenade and fall-back (taken from the README,
  which their tests cover), and the medic system (another agent is auditing it).
- Nothing was built, run or tested. Every "bug" below is from reading. Confidence is stated per item.

Paths are relative to the repository root. `Systems/` means `Content.Server/_WG/NpcCrew/Systems/`, `HTN/` means
`Content.Server/_WG/NpcCrew/HTN/`, `SC/` means `Content.Server/_WG/ShipCommand/`.

## Known and in flight

These are being fixed on this branch by others and are not proposed again:

crew refilling air tanks and taking internals off in safe air; the surgeon dropping a patient's clothes instead of
deleting them; the paramedic letting patients leave beds; paramedic medipens; a second paramedic and surgeon; crew
seeking snacks, drinks and relaxation; crew answering general quarters and alert codes; a Chef and a Bartender NPC;
random-event critters as intruders and janitors hunting mice; cortical borers always hostile; several janitors
going to the same mess; core interlocked doors not cycling; holodoor visuals sticking.

Adjacent things those fixes will not cover, each detailed in section 2:

| In-flight item | What it leaves open | Gap |
|---|---|---|
| Air tanks and internals | Crew with no suit at all (janitor, paramedic, surgeon) never leave bad air. A worker indoors still breathes his tank for the whole job. | G6, G8 |
| Second paramedic and surgeon | Revived before his post is restaffed, a crewman keeps it (fixed in this PR). Revived after the replacement came, he does not get the post back and stays aboard outside the roster, so there are then three. | G4 |
| Paramedic fixes | A medic with a patient sleeps when no living player is near him, and a downed player does not count as one. | G5 |
| Critters as intruders | A hostile-faction animal on a Warn ship is neither warned nor fought until it bites; only ships with `WGCrewOnSight` shoot it on sight. | G21 |
| General quarters and alert codes | The reverse link is missing: nothing the crew detects raises the code, the PA or the distress marker. | G17 |
| Snacks, drinks, relaxation | No watch system decides who may leave a post, and nothing lets crew sit, so "relaxing" has no pose. | G1, P2 |
| Janitor dispatch | `WGCrewClaimSystem` claims by blackboard key; work orders (`_jobs`) and medics (`_claims`) keep separate claim tables. | P3 |
| Chef and Bartender | Counter staff are static godmode traders. If the new NPCs follow that pattern they ignore alarms too. | G10 |

## 1. Inventory

"README" is `Content.Server/_WG/NpcCrew/README.md`. The last column says whether the README's claim matches the code:
`ok`, `differs` (explained in section 2), or `n/a` where the README makes no claim.

### 1.1 Core loop

| Behaviour | What it does | Where | README |
|---|---|---|---|
| One HTN root | Branch order: grab, fall back, grenade, fight, muster, brace, medic, work, return to ship, duty (Pilot, Gunnery, Janitor, Guard), idle. Replans every half second; an earlier branch pre-empts. | `Resources/Prototypes/_WG/NpcCrew/htn.yml:6-133` | ok |
| Post and return | Walks to the post when past `PostRange` 1.5, and once there only when past `PostLeaveRange` 2.5. | `Systems/WGCrewSystem.cs:73-89`, `htn.yml:164-182`, `Systems/WGCrewRoutineSystem.cs:44` | ok |
| Idle | At the post: wait a random time, repeat. Nothing else. | `htn.yml:277-301` | n/a |
| Return to ship | Off his post's grid with no job, walks home through the docking ports. | `HTN/WGCrewReturnOperator.cs`, `Systems/WGCrewWorkSystem.cs:534-571` | ok |
| Sleep | With `wg.crew.sleep_idle` (default true) a crewman at his post with no alert, memory, job or bad air sleeps unless a living player is within 64 tiles. | `Systems/WGCrewSleepSystem.cs:40-79`, `NPCSystem.Sleep.cs:99-147` | differs (README:176 says crew stay awake) |
| Titles, names, cards | Role title in front of the name; profile names, accents and service ID cards. | `Systems/WGCrewSystem.cs:548-568`, `Systems/WGCrewAccessSystem.cs` | ok |
| Housekeeping on spawn | No SSD icon, no radiation receiver, suit sensors off, kill tracker on. | `Systems/WGCrewSystem.cs:511-527` | ok |

### 1.2 Combat and engagement

| Behaviour | What it does | Where | README |
|---|---|---|---|
| Engagement rule | Never, WhenAttacked (fights only while he remembers an attack) or OnSight. | `HTN/WGCrewMayFightPrecondition.cs`, `Systems/WGCrewWeaponSystem.Brace.cs:79-101` | ok |
| Target choice | Nearby faction hostiles, personal hostiles, the crew's shared hostiles, radio sightings; each passed through `Assess`. | `Systems/WGCrewWeaponSystem.Reload.cs:510-554` | ok |
| Assess | Engage in sight; out of sight engage only if freshly struck or the hunt party has room; else brace or leave. Never off his own ship unless on an errand. Never out of good air into bad. | `Reload.cs:223-275` | ok |
| Sight | 10 tiles, blocked only by anchored opaque things; all round, light or dark. | `Reload.cs:495-498` | ok |
| Draw, holster, wield | Best slot weapon to hand at a fight; two-handed guns wielded; holstered when no threat. | `Systems/WGCrewWeaponSystem.cs:63-154` | ok |
| Reload and fallback weapon | Magazines from slots then webbing; `InfiniteAmmo` refills in place; empty gun swapped for sidearm, melee or fists. | `Reload.cs:635-724` | ok |
| Retaliation | Damage origin resolved to the wielder; attacker remembered; the whole crew told (`AnswerAttack`). | `Systems/WGCrewSystem.cs:148-254` | ok |
| Pickpocketing | A noticed strip attempt is an attack. | `Systems/WGCrewSystem.cs:189-202` | ok |
| Attack memory upkeep | Memory kept alive while the attacker is aboard and seen or reported, then 30 s. | `Systems/WGCrewSystem.cs:309-365` | ok |
| Shared alert | On-sight crew on one ship share targets once a second; vision widened to the hull's size; 60 s decay. | `Systems/WGCrewAlertSystem.cs:497-600` | ok |
| Hunt party | One or two (by skill) go after an unseen hostile on a 15 s lease; the rest brace. | `Reload.cs:319-430` | ok |
| Brace | Regular and up: weapon up, facing the hostile's way, up to 90 s unseen. | `Systems/WGCrewWeaponSystem.Brace.cs`, `HTN/WGCrewBraceOperator.cs` | ok |
| Grenades | Regular and up throw a carried grenade round a corner, with a shout and blast checks. | `Systems/WGCrewGrenadeSystem.cs` | ok (not re-read) |
| Sidestep, fall back | Veterans sidestep between shots and break contact past 50 damage to patch up. | `HTN/WGCrewSidestepOperator.cs`, `Systems/WGCrewFallBackSystem.cs` | ok (not re-read) |
| Muster | A crew losing a boarding fight pulls back to a post, holds, then pushes together. | `Systems/WGCrewMusterSystem.cs` | ok (not re-read) |
| Rally | A crewman downed, or the ship tampered with, turns everyone who may fight on the culprit, helm and guns included. | `Systems/WGCrewRallySystem.cs:54-164` | ok |
| Friendly fire | Crew rounds pass through allies; allied damage is cancelled; skill scales damage dealt and taken. | `Systems/WGCrewFriendlyFireSystem.cs`† | ok |
| Skill tiers | Green to Elite set aim error, gunnery, handling and which tactics are used. | `Content.Server/_WG/NpcCrew/WGCrewSkills.cs` | ok |
| Stealth | Stealth-suited mobs and cloaked ships are unseen until they act. | `Systems/WGCrewStealthSystem.cs` | ok (not re-read) |
| Take alive | Baton, cuff, drag to a chair, buckle. Used only by abduction encounters. | `Systems/WGCrewGrabberSystem.cs`, caller `Content.Server/_WG/Encounters/Systems/WGEncounterSystem.Abduct.cs:601,618` | n/a |
| Intents | Pirate boarders roll an endpoint (hold up, mug, beat down, harvest, warn off, lethal). | `Content.Server/_WG/NpcIntents`† | n/a |
| Cowards and shelter | `Engagement: Never` crew drop work, cower where they stand, run from any stranger within 7 tiles, cry out, and return a minute after the alarm. | `Systems/WGCrewShelterSystem.cs` | differs (G16) |

### 1.3 Security and boarding

| Behaviour | What it does | Where | README |
|---|---|---|---|
| Authorisation | Ship command's answer first, then encounter passengers, then company family, then shared NPC faction. | `Systems/WGCrewSecuritySystem.cs:190-235` | ok |
| Boarding sweep | Once a second each guard checks every living mob on his grid; sight needed to react. | `WGCrewSecuritySystem.cs:257-349` | ok |
| Ignore, Warn, Hostile | Per boarding and per docking; ShipCommand may answer per visitor and zone. | `WGCrewSecuritySystem.cs:114-144, 221-226`, `SC/WGShipCommandSystem.Crew.cs:65-146`† | ok |
| Challenge | Names the trespasser and the zone, points, gives 30 s, last warning at 10 s, announces engaging. | `Systems/WGCrewSecuritySystem.Challenge.cs` | ok |
| Turn on overstayers | After the deadline `AnswerAttack` makes him the crew's attacker, renewed while seen. | `WGCrewSecuritySystem.cs:355-378` | ok |
| Docking response | An unauthorised dock raises an incident; Hostile makes the visitor a gunnery target for 60 s. | `WGCrewSecuritySystem.cs:237-255, 380-391` | ok |
| Intercom relay | Sightings go over the crew intercom to headsets that are on; recipients know the contact for 30 s. | `Systems/WGCrewCommsSystem.cs` | ok |
| Brandished gun | A player seen holding a gun is told to put it away and fought after 10 s. | `Systems/WGCrewConductSystem.cs:80-165` | ok |
| Theft | A player seen by a fighter picking up marked ship property is fought at once. Property is marked only by encounters. | `WGCrewConductSystem.cs:55-75, 168-188`; only caller `Encounters/Systems/WGEncounterSystem.Build.cs:518` | differs (G18) |
| Hauling the fallen | A player seen dragging or carrying a downed crewman gets 10 s to put him down. | `Systems/WGCrewConductSystem.Bodies.cs` | ok |
| Tampering | A player cutting into or unanchoring part of the ship is rallied against. | `Systems/WGCrewRallySystem.cs:69-124` | ok |
| Fauna | Non-humanoid, non-hostile-faction animals are let be (in flight). | `WGCrewSecuritySystem.cs:85-96` | n/a |
| Spared vessels | Own-faction player ships are not warned, hunted or zoned. | `Systems/WGCrewSparedSystem.cs`† | ok |
| Sentries | Automated turrets take the crew's attackers as targets. | `SC/WGShipSentrySystem.cs:33-80`† | n/a |
| Zone keys | Living crew whose home grid is the ship open Staff and Locked zone doors. | `SC/WGShipCommandSystem.Zones.cs:271-281`† | n/a |

### 1.4 Duties by role

| Role | Duty and engagement | What he does beyond standing at his post | Where |
|---|---|---|---|
| Marine | Guard, OnSight | Patrols: a random deck post every 45-90 s, or a set route (Rounds), or stays (Posts). | `Systems/WGCrewRoutineSystem.cs:45-95` |
| Deckhand | Guard, OnSight | Same random patrol, always (no standing order reaches him). Works Repair, Resupply, Salvage and Loot orders. | `WGCrewRoutineSystem.cs:47-48`, `Systems/WGCrewWorkSystem.cs` |
| Engineer | Guard, WhenAttacked | Fuels and starts generators, the AME and the fission reactor from loose stores; trims control rods and turbine load; joins Repair. | `Systems/WGCrewWorkSystem.Engineer.cs:112-216` |
| Janitor | Janitor, WhenAttacked | Mops puddles on his ship, washes dirty tiles within 30, hunts mice and roaches (in flight). | `htn.yml:184-244`, `Content.Server/_WG/Cleaning` |
| Paramedic, Surgeon | Guard, WhenAttacked | Section 1.9. | `Systems/WGCrewMedicSystem*.cs` |
| Pilot | Pilot, WhenAttacked | Section 1.5. | `Systems/WGPilotDutySystem*.cs` |
| Gunner | Gunnery, WhenAttacked | Section 1.5. | `Systems/WGGunnerDutySystem*.cs` |
| Radio operator | Guard, WhenAttacked | Section 1.5. | `Systems/WGRadioOperatorSystem.cs` |
| Captain | Guard, WhenAttacked | Section 1.5. No roster post exists for him. | `Systems/WGCaptainSystem.cs` |
| Counter staff | Not crew | Static, godmode traders: dialogue, shop, barks, refuse the barred. The dockmaster answers hails after 8 s. | `Content.Server/_WG/Traders`†, `Resources/Prototypes/_WG/AdminVessels/crew.yml:55-63` |

### 1.5 Pilot, gunner, captain, radio (†)

| Behaviour | What it does | Where |
|---|---|---|
| Take and keep the helm | Picks a square helm, walks, takes it, faces it; releases on incapacity, possession, power loss or a fight. | `Systems/WGPilotDutySystem.cs:340-519`, `htn.yml:246-275` |
| Orders | Hold, GoTo, Loiter (Loiter, Circle, Attack), Follow, Escort, Dock, Undock. Docked ships undock first. | `WGPilotDutySystem.cs:204-326`, `WGPilotDutySystem.Navigation.cs:68-118` |
| Docking by hand | Plan a port pair, stand off, settle, creep, connect; three tries; optional FTL fallback. | `WGPilotDutySystem.Docking.cs:54-375` |
| Departure | Waits up to 60 s for absent crew, releases ports, backs off. | `WGPilotDutySystem.Docking.cs:486-529` |
| Yield to a player | An authorised player at the helm has it; orders resume 3 s after. | `WGPilotDutySystem.Navigation.cs:88-109` |
| Present the guns | Holds the best armed bearing inside 1.5 times the attack orbit; `FaceThreats` turns a holding hull. | `WGPilotDutySystem.Stance.cs:38-72` |
| Gunnery | Takes a console, picks targets at 4 Hz, splits guns over up to four, holds fire through non-hostile hulls, fires warning shots wide. | `Systems/WGGunnerDutySystem.cs:90-446`, `WGGunnerDutySystem.LineOfFire.cs` |
| Gunner policy | Hostiles, DefendDocked, WhenFiredOn. | `WGGunnerDutySystem.cs:404-431`, `Systems/WGCrewAlertSystem.Docked.cs` |
| Captain | A flag. With `HeaveTo` he has pilots evade (orbit the attacker, or flee with `FleeAttackers`) and restores the course on all-clear. | `Systems/WGCaptainSystem.cs:118-370` |
| Radio reports | Docking, undocking, jumping, arriving, on station, dock aborted, approach, dock and board warnings, mayday, boarded, captain down, helm down, all clear. | `Systems/WGRadioOperatorSystem.cs:138-453` |
| Objective queue | Fourteen kinds; pause, skip, reorder; promotes a new pilot when all are down, while a queue runs. | `Systems/WGCrewObjectiveSystem.cs:187-457` |
| Escort and battlegroups | Formation slots; shared threat; no fire on partners. | `Systems/WGCrewEscortSystem.cs` |
| Ship status | Abandoned, disarmed, crippled, wrecked; Disable, Destroy, Deter. | `Systems/WGCrewShipStatusSystem.cs` |
| Ramming | A closing speed of 8 m/s counts as an attack. | `Systems/WGCrewRammingSystem.cs:33-49` |

### 1.6 EVA and survival

| Behaviour | What it does | Where | README |
|---|---|---|---|
| Void kit | A post without air is manned in a suit with a tank that is topped up; `WGCrewSuitUp` crew dress when the air goes bad. | `Systems/WGCrewEvaSystem.cs:74-143, 306-308` | ok |
| Internals on | Suit, helmet and a tank above 300 kPa: connects before work and in bad air. | `WGCrewEvaSystem.cs:149-166` | ok |
| Low tank | Swaps to a loose spare within reach, else walks to safe air or to a spare. | `WGCrewEvaSystem.cs:195-278, 311-332` | ok |
| Internals off, canister refill | In flight. | `WGCrewEvaSystem.cs:341-351, 377-447` | n/a |
| Fire | Out of a fight, stops short of a burning tile. No other fire behaviour. | `Systems/NPCSteeringSystem.Fire.cs` | ok |
| Slips, crates, sticking | Walks past slippery puddles, shoves loose crates aside, steps aside when stuck. | `Systems/NPCSteeringSystem.Walk.cs`, `.Shove.cs`, `.Unstick.cs` | ok |
| Doors | Uses his card, then pries; three pry starts in 30 s then gives up for 60 s. | `Systems/NPCSteeringSystem.Access.cs`, `Content.Server/_WG/NpcPry`† | ok |
| Self-care | Out of a fight, one healing item every 8 s; past 40 damage he counts as calling a medic. | `Systems/WGCrewMedicSystem.SelfCare.cs` | ok |
| Body dormancy | Mindless bodies on unwatched planet ground skip breath, blood and metabolism. Off-planet only by cvar. | `Content.Server/_WG/Dormancy`† | n/a |

### 1.7 Work orders

| Behaviour | What it does | Where | README |
|---|---|---|---|
| Repair | One hand at a time rebuilds missing snapshot tiles and structures with the built-in SRD, and mends repairables. | `Systems/WGCrewWorkSystem.cs:162-272`, `.Repair.cs` | ok, but see G8 |
| Resupply, Salvage | Fetches loose ammunition and filled tanks, or material stacks, from a docked grid to his post. | `WGCrewWorkSystem.cs:289-313` | ok |
| Loot | Raiders take up to four of the dearest reachable things, from unlocked lockers too, to a stash. | `WGCrewWorkSystem.cs:179-288, 742-793` | ok |
| Fuel runs | Engineer jobs share the same job table. | `WGCrewWorkSystem.Engineer.cs:112-152` | ok |
| Give up | No progress for 10 s, 120 s per leg, or stranded 10 s: the target is skipped. | `WGCrewWorkSystem.cs:393-445` | ok |
| Auto repair | A commanded ship queues a Repair order every minute when its queue is empty. | `SC/WGShipRosterSystem.cs:406-417` | n/a |

### 1.8 Social, idle, comms, barks

| Behaviour | What it does | Where | README |
|---|---|---|---|
| Action chatter | Twelve call sites ("patrol", "engage", "reload", "helm", "air"...). Switched off: `Enabled` is false and nothing sets it. | `Systems/WGCrewSpeechSystem.cs:17-25` | differs (G2) |
| Spoken lines that do run | Challenge, conduct, coward cries, grenade and muster calls, medic and cloning lines, radio reports. | `TrySendInGameICMessage` callers in `Systems/` | ok |
| CrewBarks | On ships that opt in: struck, attacked, four greets with a salute for the Boss, idle every 3-8 min near a player, healed, relieved, reporting, fight, jump, arrived. | `Content.Server/_WG/CrewBarks`† | n/a |
| Ship-to-ship dialogue | Mood lines, final warning, surrender. Encounter ships only. | `Content.Server/_WG/NpcDialogue`† | n/a |
| Idle behaviour | None beyond barks: no sitting, facing, eating, talking to each other or using anything. | `htn.yml:277-301` | n/a |

### 1.9 Medical (brief; see the medical audit)

Paramedic triage and treatment where the patient lies, carry to a medical bed or morgue drawer, the surgeon's
autodoc runs and table surgery, cloning from records, and `WGCrewMayTreatEvent` for refusals:
`Systems/WGCrewMedicSystem.cs`, `.Carry.cs`, `.Surgery.cs`, `.Table.cs`, `.Cloning.cs`; README:299-346. Not reviewed
here except where it meets the crew core (G3, G4, G5, G6).

### 1.10 Ship-command integration (†)

| Behaviour | What it does | Where |
|---|---|---|
| Roster | Post markers staff nine roles; mission is group `command`, Hold, boarding Warn, docking Ignore. | `SC/WGShipRosterSystem.cs:49-74, 280-341` |
| Restaff | A post frees on death or deletion; refilled by hand or after `RestaffDelay`, arriving at the crew bay. | `WGShipRosterSystem.cs:343-398` |
| Relieve and stand down | The crewman loses his duties, walks to the bay and is deleted. | `WGShipRosterSystem.cs:233-278` |
| Player jobs | A player who takes a job relieves the NPC at that post. | `SC/WGShipJobsSystem.cs` |
| Standing orders | Marines: Patrol, Rounds, Posts. Gunners: Hostiles, DefendDocked, WhenFiredOn. Pilot: Hold, Evade, Run, FaceThreats. Callsign. | `SC/WGShipBehaviourSystem.cs:63-102` |
| Authorise, forgive, may-treat | By standing, zone mode and the barred list; the Boss is forgiven. | `SC/WGShipCommandSystem.Crew.cs:28-146` |
| Alert codes, GQ, distress, PA | State on the grid and events raised. No subscriber anywhere; crew do nothing with them yet (in flight). | `Content.Server/_WG/ShipPa`† |
| Vehicle crews | Crewmen drive and gun planet vehicles with their HTN asleep; a trooper takes a dead driver's seat. | `Content.Server/_WG/NpcVehicleCrew`† |

## 2. Gaps

Ranked by how much a player aboard the Lupercal, or boarding an encounter ship, would notice. Size is the fix alone.

### G1. Crew at a post do nothing but stand (high visibility; by design so far)

The idle branch is "walk to post, wait" (`htn.yml:277-301`). No NpcCrew code buckles a crewman into a seat: the only
buckle calls are the grabber's prisoner and the medic's patient. The Lupercal posts its pilot and both gunners on
`ChairPilotSeat`s and its radio operator on an office chair, so all four stand on their chairs. The engineer stands
between the chairs at three consoles. Nobody faces anything except a held helm or gunnery console
(`Systems/WGCrewRoutineSystem.cs:39-43`). The Lupercal has about 250 seats, 9 coffee machines, a bar, a lounge with
an arcade, piano and chess board, three break rooms and a sauna; crew touch none of it. The in-flight needs work
covers going to food and drink. It does not give a pose at the post or a rule for who may leave it. See P1 and P2.

### G2. Action chatter is switched off, and the README says otherwise (high; confident)

`WGCrewSpeechSystem.Enabled` is a plain field that defaults to false, with a TODO saying crew say nothing until
voicelines exist (`Systems/WGCrewSpeechSystem.cs:17-25`). No server code sets it. All twelve `Say` call sites are
silent, and the Fluent lines exist (`Resources/Locale/en-US/_WG/NpcCrew/crew.ftl:253-264`). README:94-95 describes
the cooldowns as live. On a ship without CrewBarks (every encounter ship) a crewman on foot speaks only to challenge,
warn about conduct, cry out as a coward, or call a grenade or a muster. On the Lupercal CrewBarks fills part of this,
but it has no line for patrol, repair, reload, low air or taking the helm. Decision needed: Q2.

### G3. Medics, the janitor and the engineer are drafted into every fight (high; confident)

`IsFighter` is "not Never, not at the helm or guns" (`Systems/WGCrewSystem.cs:221-225`). `AnswerAttack` gives every
fighter the attacker's memory (`:236-244`), and it runs for an overstaying trespasser, a brandished gun, a theft, a
hauled body and any blow to any crewman. The janitor, engineer, paramedic and surgeon are WhenAttacked Guards, so
they qualify. The fight branch sits above the medic and work branches, and the draw operator always succeeds with
bare hands (`HTN/WGDrawWeaponOperator.cs`). The medics carry no weapon (`Resources/Prototypes/_WG/NpcCrew/medics.yml:55-72`).
Result: when a marine warns a trespasser who stays, the surgeon leaves his patient and goes to punch an armed
intruder, and may join the hunt for him. Only `Engagement: Never` crew shelter (`Systems/WGCrewShelterSystem.cs:84`).
README:79-82 lists the fighters as on-sight crew, marines, captain and radio officer, which is not what the code
does. Fix: a per-role `combatant` flag read by `IsFighter`; non-combatants defend themselves only when struck
(`crew.Struck`) and otherwise keep working or shelter. Size S.

### G4. A revived crewman is replaced anyway and then there are two (high on the Lupercal; confident, re-read)

**Partly fixed in this PR.** Revived before his post is restaffed, a crewman keeps it: `OnMemberState` now clears
`slot.Down` when the occupant comes back from Dead, so no replacement is sent. Revived after the replacement came,
he does not get the post back: `SpawnInto` has already taken his roster membership and given the post to the
newcomer, and he stays aboard outside the roster, as below. That second case is still open. The text below describes
the code as reviewed, before the fix.

`OnMemberState` handles only `MobState.Dead` and sets `slot.Down` (`SC/WGShipRosterSystem.cs:343-363`). Nothing
clears `slot.Down` on revival; only `Relieve` and `SpawnInto` do. NPC medics defibrillate. Two minutes after the
death, auto-restaff spawns a replacement and strips the first man's roster membership (`:389-392`, `:313-315`).
The first stays aboard with the same group and post: two surgeons at one post, one of them outside relief,
stand-down and standing orders. The Lupercal restaffs up to 30 an hour. Fix: clear `slot.Down` when the occupant
returns to Alive. Size S. Related (†, not re-read): a player job-holder who dies keeps his post, so no NPC is sent
(`SC/WGShipJobsSystem.cs:39-40`).

### G5. A medic with a patient sleeps when no living player is near him (high; medium-high confidence)

`StaysAwake` has clauses for alerts, attack memory, work jobs and bad air, and none for a medic's patient
(`Systems/WGCrewSleepSystem.cs:40-79`). The sleep pass counts only living, non-ghost players as presence
(`Content.Server/_WG/NpcCrew/NPCSystem.Sleep.cs:42-47`). The medic system assigns a patient once a second without
waking the HTN (`Systems/WGCrewMedicSystem.cs:171-202`). So when a player goes down and no other living player is
within 64 tiles of the medic, the medic is assigned and never moves; after 30 s without progress he gives the
patient up for a minute, and it repeats. A player alone aboard is the plain case. The same holds for a downed crewman nobody is near, which matters less. Fix:
`StaysAwake` returns true while `WGCrewMedicComponent.Patient` is set or a treatable patient exists on his grid.
Size S. For the medical audit to confirm in a real run.

### G6. Crew without a suit never leave bad air (high when it happens; confident)

The janitor, paramedic and surgeon have no suit, mask or tank (`Resources/Prototypes/_WG/NpcCrew/gear.yml:24-32`,
`medics.yml:55-72`). `FindSafety` is reached only when a worn tank is low (`Systems/WGCrewEvaSystem.cs:311-332`);
with no tank, `low` is false and the only other step is `ConnectAnyTank`, which needs a mask. The sleep system keeps
such a crewman awake "to walk out" (`Systems/WGCrewSleepSystem.cs:72-78`), but nothing walks him out. In a breach or
a gas leak they stand at their posts until they drop. Counter staff are godmode, so they are unaffected. Fix: any
crewman in unsafe air who cannot become `Ready` gets a safety destination. Better: he takes a kit from an emergency
closet first (the Lupercal has 8 emergency and 9 fire closets, 11 EVA suit storages). Size S for the walk, M for
the closet.

### G7. No fire response (high when it happens; confident)

No NpcCrew code references extinguishers, `Flammable` or firelocks. A burning crewman does not put himself out. The
only fire behaviour is stopping short of a burning tile while out of a fight. The Lupercal's prototype says so and
works round it with `WGNoFireSpread` (`Resources/Prototypes/_WG/AdminVessels/lupercal.yml:29-30`), while carrying
101 extinguisher cabinets and 98 fire alarms. See P3.

### G8. Repairs are done by one hand at a time, in a sealed suit (medium; confident)

`Advance` returns "working" as long as any job of the crew is live (`Systems/WGCrewWorkSystem.cs:164-176`), so the
Lupercal's four deckhands and engineer repair one target at a time. A job needs a hand `Ready` for EVA whatever the
air (`:215-228`, `:244-245`, `:266-267`): a broken window in a pressurised corridor waits for a suit and a tank above
300 kPa, and the outbound leg re-checks it every plan (`:582`), which connects internals and holds them on for five
seconds at a time (`Systems/WGCrewEvaSystem.cs:159-163`). The in-flight internals fix exempts a crewman "about to go
out", so a worker indoors still breathes his tank for the whole job. Fix: one job per free worker with claims;
require EVA readiness only when the target or the path is in bad air. Size M.

### G9. Trespass has two outcomes: left alone or shot (medium-high; by design so far)

Warn is 30 s, then every fighter turns lethal (`Systems/WGCrewSecuritySystem.cs:355-378`). There is no escort out,
no detention and no non-lethal first step, though the grabber already does baton, cuff, drag and buckle
(`Systems/WGCrewGrabberSystem.cs`) and is used only by abduction encounters. ShipCommand's README:53 notes the
missing escort (†). The Lupercal has cuffs, zipties and flashbangs in its armory and no brig. See P4.

### G10. Counter staff are props (medium; confident, †)

`TraderSystem.MakeInert` gives non-killable traders godmode, removes their movers and makes them static
(`Content.Server/_WG/Traders/TraderSystem.cs:136-187`). They have no HTN and no faction logic. Shooting one raises
a bark and nothing else: no `WGCrewAttackAnsweredEvent`, so marines and sentries standing beside him do not react.
They keep trading through a boarding fight. NpcCrew has no reference to `TraderComponent`. See P4.4.

### G11. There is no captain, and where there is one he is a flag (medium; confident, †)

No `WGShipCrewPost` exists for a captain; the Lupercal's bridge has a gold throne beside the command console and
nobody in it. On encounter ships `WGCaptainComponent` has no HTN branch (`Content.Server/_WG/NpcCrew/Components/WGCaptainComponent.cs:8-9`):
his only effect is gating whether pilots evade. A captain with `HeaveTo` false also silences `ReactToAttacks` pilots
(`Systems/WGCaptainSystem.cs:125-138`), which looks unintended. See P5.

### G12. Succession works only in one case (medium; †)

- The helm: `Promote` runs only while an objective queue is running (`Systems/WGCrewObjectiveSystem.cs:209, 225`).
  A ship on manual orders, or with a finished or paused queue, never replaces a downed pilot. On a commanded ship
  restaffing covers it after two minutes.
- A promoted pilot keeps his old `Role`, and Helm Down and the spokesman test check `Role`, not duty
  (`Systems/WGRadioOperatorSystem.cs:313-316, 589`), so his fall is never announced.
- Nobody replaces a downed gunner or radio officer. A second gunner on a grid idles, since one gunner per grid
  drives (`Systems/WGGunnerDutySystem.cs:210, 264-268`); the Lupercal has two.
- The saved course is dropped when the pilot is briefly ineligible and is not passed to a successor
  (`Systems/WGCaptainSystem.cs:89-93`).

### G13. Standing orders reach four roles (medium; confident)

Orders exist for marines, gunners, the pilot and the radio callsign. Deckhands always wander, whatever the marines
are told: the routine filter is role Deckhand or Marine with no order component on deckhands
(`Systems/WGCrewRoutineSystem.cs:47-48`, `SC/WGShipBehaviourSystem.cs:78-102`†). The engineer, janitor and medics
have no order. There is no order to send crew somewhere, recall them, hold fire on boarders or change the rules of
engagement. `InfiniteAmmo`, `Skill`, `AutoRepair` and the boarding rule are prototype-only, and the first two reach
crew only at spawn (`SC/WGShipRosterSystem.cs:306-307`). Changing any order resets every marine's round (†,
`WGShipBehaviourSystem.cs:86-101`). See P7.

### G14. Patrols look at nothing (medium; confident)

A patrol is a change of post every 45-90 s (`Systems/WGCrewRoutineSystem.cs:45-58`). The patrolling crewman does not
react to a body, a dropped weapon, a breach, a fire, an open locker or a spill. A crewmate found down is reported
only by whoever witnessed it happen. Nobody investigates a gunshot or an alarm he did not see; out-of-sight hostiles
are known only through an intercom report from a witness. See P4.3 and P6.2.

### G15. The brandish rule sees only guns, and only players (medium; confident)

`HoldsGun` checks `GunComponent` (`Systems/WGCrewConductSystem.cs:209-218`). A drawn energy sword, a chainsaw, a
live grenade or a stun baton in hand passes. The sweep enumerates `ActorComponent` only (`:94-103`), which is
deliberate for NPC visitors. Size S to widen to wielded melee weapons and primed explosives.

### G16. Shelter does not do what the README says (low visibility; confident)

README:83-85 says non-fighters "run for the bridge and lie on the deck there". The code has them get down where they
stand and run to the tile farthest from the nearest stranger (`Systems/WGCrewShelterSystem.cs:98-102, 188-221`). The
summaries at `:73` and `:238` still say bridge. On the Lupercal no roster role is `Never`, so nobody shelters at all
(see G3). Decide which behaviour is wanted (Q4) and fix the other.

### G17. Crew detections do not reach the ship's alarm (medium; †)

A mayday, a boarding call or a hostile docking never sets `ShipAlertComponent.Code`, general quarters or the
distress marker, and never uses the PA; NpcCrew has no reference to ShipPa. The in-flight work makes crew react to
the alarm. It does not make them raise it. See P5.4.

### G18. Theft is noticed only on encounter ships (medium on the Lupercal; confident)

`MarkProperty` has one caller, the encounter builder (`Content.Server/_WG/Encounters/Systems/WGEncounterSystem.Build.cs:518`).
A commanded ship marks nothing, so a guest can empty a rack in front of a marine. Zones answer trespass only. Fix:
mark on roster map init, or better, treat taking an item out of a Staff or Locked zone's storage as theft. Size S-M.

### G19. Radio officer details (low-medium; †)

- Undocking is announced by both ships: no outbound filter, unlike docking (`Systems/WGRadioOperatorSystem.cs:399-408`).
- No `ActorComponent` guard: a possessed officer or captain keeps transmitting on his own (`:459-518, 574-603`).
- All-clear runs on his own 120 s timer, not on `WGCrewAlertClearedEvent` (`:162`); a spokesman change mid-fight sends
  a second mayday.
- With a faction channel set, routine lines go only there; README:212-214 says "also" (`:503-508`).
- He never reports a gunner down, the helm retaken, guns or thrust lost, or that the ship is adrift.
  `WGHelmTakenEvent` and `WGHelmReleasedEvent` have no subscribers.
- He does not take hails. On the Lupercal the dockmaster's `HailResponder` does, after an 8 s delay left "for the
  bridge" (`Resources/Prototypes/_WG/AdminVessels/crew.yml:57-63`), and no NPC on the bridge uses it.

### G20. Gunnery details (low-medium; †)

- Aim point is the target grid's origin plus error, not its centre (`Systems/WGGunnerDutySystem.cs:232, 257`); the
  captain and the queue use `LocalAABB.Center`. A hull with an off-centre origin is shot beside.
- Any non-hostile grid inside the 40 m splash box blocks every gun (`WGGunnerDutySystem.LineOfFire.cs:100-108`), so a
  hostile docked to a neutral station cannot be fired on. May be intended.

### G21. Hostile animals on a Warn ship (low-medium; confident)

A non-humanoid of a faction hostile to the crew is not fauna, so it is a boarder; under Warn nothing is said to it
and no deadline starts, since both are limited to actors and humanoids (`Systems/WGCrewSecuritySystem.cs:131-136`).
On-sight fire needs `WGCrewOnSightComponent` on the grid (`Systems/WGCrewWeaponSystem.Reload.cs:291-300`), which only
the Reaver campaign and the doomed scenario add. On the Lupercal a carp is watched until it bites someone. The
in-flight critter work should cover this; listed so it is checked.

### G22. Smaller items

| Item | Evidence | Confidence |
|---|---|---|
| The engineer on a commanded ship has little to do: stores are stocked and the plant commissioned only by encounters, and the Lupercal's fusion reactor and zero-point generator are not plant he knows. | `Encounters/Systems/WGEncounterSystem.Build.cs:495-497`; `Systems/WGCrewWorkSystem.Engineer.cs:155-216` | confident |
| The captain and radio officer are given the attacker's memory by `AnswerAttack` and can then join a hunt for someone they cannot see; README:100-101 says they answer personal attackers only, README:81 says they are fighters. | `Systems/WGCrewSystem.cs:236-244`; `Reload.cs:248-274` | medium |
| The janitor has no bark voice and speaks common lines only. | CrewBarks locale† | confident |
| Both janitor posts are on one tile. | Lupercal map† | confident |
| The dead keep their kit and bodies while replacements arrive, up to 30 an hour: a loot source, and 11 morgue drawers. | `SC/WGShipRosterSystem.cs:313`; lupercal.yml:41-48 | confident |
| Sight is 10 tiles all round with no facing or light; sneaking past a guard's back is impossible without a stealth suit. | `Reload.cs:495-498` | confident |
| `Relieve` leaves the radio, engineer, repair, patrol and barker components on a relieved crewman. | `SC/WGShipRosterSystem.cs:262-273`† | low impact |
| A flee leg can complete as "on station" and cancel its own saved course when the last objective's range exceeds 600. | `Systems/WGCaptainSystem.cs:33, 109-116`; `WGPilotDutySystem.cs:592, 632`† | medium |
| README:176 says crew AI stays awake away from players; with `wg.crew.sleep_idle` true they sleep when idle. | `Content.Shared/_WG/CCVar/NpcCrewCVars.cs:24-25` | confident |
| README:165 omits Follow, Repair, Resupply, Salvage and Loot from the queue kinds; README:179 says pilots dodge fire, which needs Regular skill and is off during Hold. | `WGCrewSkills.cs:71`; `WGPilotDutySystem.cs:700-701`† | confident |
| `WGCrewSurrenderedComponent` (grid) and `WGSurrenderedCrewComponent` (mob) differ by word order. | NpcCrew, NpcDialogue† | naming only |
| ShipCommand's hooks trust `HomeGrid`, which falls back to the grid stood on for a crewman with no post. | `Systems/WGCrewSystem.cs:108-113`; `SC/WGShipCommandSystem.Crew.cs:28-32`† | medium, impact unverified |

## 3. Expansion proposal

Each feature gives what the player sees, the hooks, a size (S: a day or two and one system; M: about a week, several
files and tests; L: several weeks or new UI) and the risks. Phases are ordered so each one stands on the last. P0 is
fixes and should go first whatever else is chosen.

Rules applied throughout:

- Extend the existing systems and the one HTN root. A new behaviour is a new branch or a new job kind, not a second
  planner.
- No new directed subscription on an upstream component without a grep. Pairs already held that these proposals come
  near: `WGCrewComponent` with `MapInitEvent`, `MobStateChangedEvent`, `WGStripAttemptEvent`, `BeforeDamageChangedEvent`,
  `ComponentShutdown`, `EntityTerminatingEvent`, `GetVerbsEvent<Verb>`, `WolfmedConsciousnessChangedEvent`;
  `NPCRetaliationComponent` with `BeforeDamageChangedEvent`; `DamageableComponent` with `BeforeDamageChangedEvent`,
  `DamageModifyEvent`, `PreventCollideEvent`; `TraderComponent` with `BeforeDamageChangedEvent` and seven more;
  `BuckleComponent` with `UnbuckledEvent` (WG Explosion) and most buckle events upstream; `AirAlarmComponent` with
  `AtmosAlarmEvent`; `FlammableComponent` with seven events; `WGShipRosterMemberComponent` with
  `MobStateChangedEvent` and `EntityTerminatingEvent`. Where a pair is taken, poll component state, call the owning
  system's public method, or have the owner raise a new `WG*` event.
- Broadcast events that can be shared freely: `WGCrewAlertEvent`, `WGCrewAlertClearedEvent`,
  `WGCrewMemberDownEvent`, `WGCrewSecurityIncidentEvent`, `WGCrewAttackAnsweredEvent` (on the grid),
  `WGCrewHullHitEvent`, `WGCrewTreatedEvent`, `WGHelmTakenEvent`, `WGHelmReleasedEvent`, `WGShipPostStaffedEvent`,
  `ShipAlertCodeChangedEvent`, `ShipGeneralQuartersChangedEvent`.
- Every new behaviour that must run with no player near needs a clause in `WGCrewSleepSystem.StaysAwake`, and every
  one that should not needs none. G5 is what forgetting this looks like.
- Player-facing text through Fluent under `Resources/Locale/en-US/_WG`; new prototypes take the `WG` prefix; tests
  under `Content.IntegrationTests/Tests/_WG/NpcCrew` without `Destructive` pools.

### P0. Correctness (all S unless noted)

| # | Fix | Hook |
|---|---|---|
| 0.1 | Clear `slot.Down` when a roster occupant returns to Alive (G4); done in this PR for revival before restaffing. Still open: a crewman revived after his replacement came is outside the roster; send him off at the crew bay or make him a spare. | `WGShipRosterSystem.OnMemberState`; the pair is already this system's. |
| 0.2 | Medics stay awake while they have, or could have, a patient (G5). | `WGCrewSleepSystem.StaysAwake`; a public `HasWork(uid)` on `WGCrewMedicSystem`. |
| 0.3 | `combatant` on `wgCrewRole`, copied to `WGCrewComponent`; `IsFighter` reads it. Non-combatants answer only a blow to themselves (G3). | `WGCrewRolePrototype`, `WGCrewSystem.IsFighter`, `WGCrewRallySystem.Rally`, README. Check `IsFighter` callers in Conduct and Encounters. |
| 0.4 | Unsuited crew in bad air walk to safe air (G6). | `WGCrewEvaSystem.Update`: call `FindSafety(uid, crew, false)` when `!safe && !Ready && !immune`, whatever `low` says. Coordinate with the in-flight EVA edit to the same method. |
| 0.5 | Helm succession without a queue; announce by duty, not role (G12). | Move `Promote` out of the queue gate in `WGCrewObjectiveSystem`; `WGRadioOperatorSystem` tests `crew.Duty`. |
| 0.6 | Radio: outbound filter on undocking, actor guard, all-clear tied to `WGCrewAlertClearedEvent` (G19). | `WGRadioOperatorSystem`. |
| 0.7 | Gunner aims at `LocalAABB.Center` (G20). | `WGGunnerDutySystem.Lay` callers. |
| 0.8 | Brandish covers wielded melee weapons and primed explosives (G15). | `WGCrewConductSystem.HoldsGun`. Ask Q7 first. |
| 0.9 | Mark property on commanded ships (G18). | `WGShipRosterSystem` map init calls `MarkProperty`, or the zone rule in P4.5. |
| 0.10 | README and comment corrections: G2, G3, G16, G22. | README hand-written part; `modules.py --write` afterwards. |
| 0.11 | `Relieve` strips the remaining duty components. | `WGShipRosterSystem.Relieve`. |

Risk for P0 as a whole: 0.3 changes who fights on every encounter ship, so the muster and rally tests need a pass;
muster counts "fighters on their feet" and may be relying on drafted hands.

### P1. Posts with a posture

**1.1 Seats (M).** The player sees the pilot, gunners and radio operator sitting at their consoles, the engineer in
a chair at his monitors, and off-watch crew sitting in the lounge. Hook: a `WGCrewSeatOperator` as the first task of
`WGCrewIdleCompound` and after `WGTakeHelmOperator` and `WGTakeGunneryOperator`: find an unoccupied `StrapComponent`
within `PostRange` of the post and call `SharedBuckleSystem.TryBuckle`. Unbuckle in one place:
`WGCrewRoutineSystem.Update` unbuckles a crewman whose steering has a destination farther than the strap, before the
move starts. Risks: a buckled mob cannot move, so a missed unbuckle freezes him (the grabber already shows the
pattern at `Systems/WGCrewGrabberSystem.cs:187-189`); the pilot's reach test (`WGPilotDutySystem.cs:478-519`†) must
pass from the seat; `BuckleComponent` events are heavily subscribed, so call the system and subscribe to nothing.
Fall-back and shelter use lying down, which must unbuckle first.

**1.2 Facing (S).** A crewman at a post with no seat faces the way his post marker points, so door guards face
outward and counter-side crew face the counter. Hook: `WGShipRosterSystem` already keeps `slot.Rotation` for counter
posts (`SC/WGShipRosterSystem.cs:293`); pass it to a new `WGCrewComponent.PostFacing` and apply it in
`WGCrewRoutineSystem` beside `MarkPostReached`. Encounter posts get a facing from `WGCrewPlannerSystem` (toward the
door for dock posts, toward the console otherwise). Risk: patrol points need no facing; leave it null there.

**1.3 Post activities (M).** While idle at a post a crewman now and then does something that fits it: the engineer
steps to a console and reads it, a deck guard checks the door beside him, a medic tidies a bed, the radio operator
keys his set. Hook: `postActivities` on `wgCrewRole`, each an emote or a short walk to the nearest entity with a
named component within N tiles and back. One operator, `WGCrewPostActivityOperator`, replaces the bare wait in
`WGCrewIdleCompound`. Risk: low. Keep it to emotes and walking so it needs no interaction permissions; cap the
radius so nobody leaves his room.

**1.4 Voicelines decision (S).** Either turn `WGCrewSpeechSystem` on for crews without CrewBarks, or delete it and
route its twelve actions into CrewBarks as new kinds (`patrol`, `repair`, `reload`, `air`, `helm`, `gunnery`).
Recommended: route into CrewBarks' picker (`WGCrewBarkSystem.Bark`), keep `Say` as the call site, and let encounter
profiles opt in by voice. One speech path, one set of cooldowns. Depends on Q2.

### P2. The watch bill

The in-flight needs work gives crew reasons to leave a post. This phase decides who may, and when.

**2.1 Watches (M).** The player sees crew change over: a relief walks up, a line is exchanged, the relieved man goes
to eat or sit. Hook: `WGCrewWatchSystem` in ShipCommand or NpcCrew, reading the roster. Each slot is On, Off or
Break. Minimum manning per role comes from the roster prototype (`minOnWatch: { WGCrewMarine: 2, WGCrewGunner: 1 }`);
single-post roles (pilot, radio, surgeon) take short breaks only while the ship holds and no alert stands. An
off-watch crewman keeps his components and gets a temporary post in a rest area; the needs branch runs only for
Off and Break. `WGCrewAlertEvent`, a security incident or general quarters recalls everyone to their posts, and
`WGCrewAlertClearedEvent` lets the bill resume. Risks: this is the first thing to move posts on a timer besides
patrol and shelter; all three write `crew.Post`, so give posts a small owner stack (`Shelter` over `Recall` over
`Watch` over `Patrol`) inside `WGCrewSystem.SetPost` instead of each system saving and restoring the old one, as
`WGCrewShelterSystem._posts` does now. Sleep: off-watch crew may sleep by the normal rule.

**2.2 Rest areas (S, with 2.1).** Where off-watch crew go. Hook: a marker `WGShipCrewRestArea` with a `roles` filter,
or reuse ShipCommand's zones (`commons-staff`, `medical-staff`, `engineering-staff`, `cargo-staff` exist on the
Lupercal) and pick seats inside the zone. The Lupercal has three break rooms, the lounge, the bar and the sauna. It
has no bunks: Q5.

**2.3 The second gunner (S).** With a watch bill the second gunner is the relief, which answers G12's idle gunner.
Alternatively give him the second console and half the batteries: `WGGunnerDutySystem.Batteries` already splits guns
over targets. Q8.

**2.4 Handover lines (S).** "Relieving you." "I stand relieved." Hook: two CrewBarks kinds raised by 2.1; CrewBarks
already has `relieved` and `reporting` for player relief and restaffing.

### P3. Damage control

**3.1 Parallel work with claims (M).** The player sees all the deckhands turn to after a fight. Hook: lift the
one-job rule in `WGCrewWorkSystem.Advance` (`:164-176`): assign every free worker a distinct target, using the
existing `Claimed` check (`:727-736`) for entities and a coordinate set for SRD tiles. Unify with the in-flight
`WGCrewClaimSystem` so janitors, workers and medics ask one place. Require EVA readiness only when the target tile or
the standing tile lacks safe air (G8). Risks: the objective status string assumes one worker; `no-eva` and
`work-blocked` become per-worker. Tests in `WGCrewTest.Work.cs` and `.FireAndRepair.cs` encode the serial behaviour.

**3.2 Firefighting (M-L).** The player sees the nearest deckhands take an extinguisher from a wall cabinet, put a
fire out and put it back; a burning crewman drops and rolls; others keep clear. Hook: a job kind `Extinguish` in
`WGCrewWorkSystem` with the same fetch-then-use legs as the engineer's fuel run (`Deliver`). Detection by polling at
1 Hz, on awake ships only: tiles with a hotspot on the home grid through `AtmosphereSystem`, and
`FlammableComponent.OnFire` on crew for the self-rescue (call `FlammableSystem`'s resist path; do not subscribe).
Cabinets are item slots; take through `ItemSlotsSystem` as the reload code does. Risks: spraying is a ranged use
with a reagent tank, the first tool crew would aim rather than touch; atmos reads are not free, so scan only grids
with a fire alarm in alarm state (poll `AtmosAlarmableComponent.LastAlarmState`). When this is trusted,
`WGNoFireSpread` can come off the Lupercal: Q6.

**3.3 Breach response (M).** The player sees crew in the breached compartment leave or suit up, the firelocks hold,
a suited party go in and repair, and the room pressure come back. Hook: 0.4 gives the leaving. Add `Seal` ordering
to Repair: `MissingStructure` targets that border vacuum first. Repressurising is the air system's job; the engineer
checks the air alarm afterwards as a post activity. Emergency closets: a crewman with no suit who cannot reach safe
air takes one (`ClosetEmergencyFilledRandom`, `ClosetFireFilled`), reusing `GiveVoidKit`'s equip order with real
items from the closet. Risk: opening storage is a new permission for crew ("never open containers" is a stated rule
in `Docs/_WG/NpcCrew/handoff.md:92`); scope it to emergency closets by prototype or tag.

**3.4 The engineer's rounds (S-M).** The player sees the engineer walk a round of the plant every few minutes and
return, and answer a power problem. Hook: `WGCrewEngineerComponent` gets a route built from the grid's plant
(`Plant(grid)`, `:458`) plus SMES and substations; `WatchReactors` already runs from his post. On a commanded ship
call `Commission` and `StockFuel` at roster init when a new roster flag `commissionPlant` is set, so he has stores to
work from. Extend `FindPlantWork` to the Lupercal's own plant only if those machines consume anything: Q9. Risk: low.

**3.5 Reports (S).** "Fire in the mess." "Breach sealed, cargo bay." Hook: `WGRadioLine` gets Fire, Breach,
Sealed; raised from the work system through `WGRadioOperatorSystem.ReceiveIncident`; place names from
`WGCrewPlaceNameEvent`, which ShipCommand's zones already answer.

### P4. Security with more than two outcomes

**4.1 Rules of engagement (M).** A per-ship and per-zone setting: Lethal (today), Detain, Escort. Hook:
`WGCrewSecurityResponse` gains Detain and Escort; ShipCommand already returns a response per visitor through
`WGCrewAuthorizeEvent.Response`. `WGCrewSecuritySystem.CheckWarnings` branches where it now calls `AnswerAttack`.
A visitor who attacks anyone is Lethal whatever the setting.

**4.2 Escort out (M).** The player who overstays is walked to the nearest dock by one or two marines who stay
within a few tiles, repeat the order and stop when he stops; if he runs deeper in, it escalates. Hook: a
`WGCrewEscortOutOperator` high in the root, target and destination set by the security system; destination from the
ship's dock nearest the visitor (`DockingComponent` on the grid). Escalation reuses the warn deadline. Risks: "walk
behind and block" is the hard part with SS14 collision; start with follow-and-repeat and escalate on distance from
the exit increasing for N seconds.

**4.3 Detain (M-L).** The player is batoned or told to stop, cuffed, walked to a holding chair, held for a set time
or until the bridge releases him, then escorted off. Hook: `WGCrewGrabberSystem.Order(mob, target, chair)` exists
and is state-driven. Needs: a holding marker (`WGShipCrewHoldingChair`), non-lethal kit on marines (a baton and
cuffs in the loadout; `WGCrewNonLethalComponent` keeps the baton charged), a guard posted at the chair, a release
timer and a ShipCommand button. Risks: player experience (being held by NPCs is frustrating: keep it short and give
the bridge the override); the grabber teleports the prisoner onto the chair (`:187`), acceptable; the Lupercal has no
brig, which is a map change: Q3.

**4.4 Investigate (M).** The player fires a gun in an empty corridor and a marine arrives to look; a body or a
dropped rifle on a patrol route makes the patrolling crewman stop, report and call a medic. Hook: a per-ship list of
"things worth a look" fed by (a) `GunShotEvent`-class broadcasts filtered to the grid (check what the gun system
raises broadcast before adding anything), (b) `WGCrewMemberDownEvent` with no witness, (c) fire and air alarms from
3.2, (d) the patrol sweep seeing a non-alive humanoid or a loose gun within sight. One marine claims each and walks
there: this is the hunt party's lease mechanism (`Reload.cs:319-430`) with a place instead of a mob. Reports through
the intercom. Risk: noise sources are many; start with gunfire and bodies.

**4.5 Zone-aware theft and conduct (S-M).** Taking an item from storage inside a Staff or Locked zone, seen by a
fighter, is theft (0.9 done properly). Hook: `WGCrewConductSystem.OnPickedUp` asks ShipCommand for the zone at the
item's last position (`WGShipCommandSystem.TryGetZoneAt`†) instead of relying on a property mark; the forgiveness and
standing checks are already in place.

**4.6 Counter staff under threat (S-M).** A shot counter clerk is the crew's business; during an alert the clerk
ducks, closes the shop and says so; afterwards he reopens. Hook: `TraderFleeSystem.OnBeforeDamage` or
`TraderSystem`'s own `BeforeDamageChangedEvent` handler calls `WGCrewSystem.AnswerAttack(grid, group, attacker)` for
a trader with a roster membership (the pair `TraderComponent`/`BeforeDamageChangedEvent` is taken, so the call goes
inside the existing handler, marked). Closing: `TraderSystem.EndConversation` and a closed flag on
`WGCrewAlertEvent`/`WGCrewAlertClearedEvent` for the home grid. Applies to the Chef and Bartender if they are built
the same way.

**4.7 Door guards (S).** A marine at a gate post asks a guest his business and tells him where the public areas are,
once; with ID checking as a later step. Hook: CrewBarks greet kinds keyed by post (`greet-guest-gate`), using the
post's zone name. Mostly content.

### P5. A bridge crew that acts

**5.1 A captain's post (M).** The Lupercal gets a captain who sits in the chair beside the command console, walks
the bridge, and is the one who calls general quarters, the all-clear and the order to engage aloud before the crew
act on them. Hook: `WGShipCrewPostCaptain` (role `WGCrewCaptain` exists); a `WGLupercalCaptain` job so a player can
take it; the captain's lines are CrewBarks kinds raised from `WGCrewAlertEvent`, `ShipGeneralQuartersChangedEvent`
and standing-order changes. He carries `WGRadioOperator` already and speaks only if no radio officer can. Risk: on
encounter ships `HeaveTo` semantics change nothing; fix the `HeaveTo` false case first (G11). Q1.

**5.2 Succession by duty (M).** When the pilot, a gunner or the radio operator goes down, the next officer takes
the station and says so; the radio reports it. Hook: a roster-level or crew-level order of succession over crew with
`WGPilotDutyComponent` (every officer has it); `WGCrewSystem.SetDuty` already replans. Add radio lines for helm
retaken and gunner down, subscribing to `WGHelmTakenEvent` and `WGHelmReleasedEvent`, which nobody uses yet. Builds
on 0.5.

**5.3 The radio officer takes hails (S-M).** The bridge answers within the 8 s the dockmaster leaves it. Hook:
`HailResponderComponent` on the `WGCrewRadioOperator` role with a shorter `answerDelay`; `HailResponderSystem.FindConsole`
decides which console he can answer from, so verify it accepts `ComputerComms` or the helm beside him before
promising this. Greetings carry the callsign. Risk: two responders on one grid; the system answers per responder, so
the shorter delay wins, but confirm `AnswerAutomated` is idempotent.

**5.4 Crew raise the alarm (S).** A boarding call, a mayday or a fire raises the alert code, sounds the PA and sets
the distress marker; the all-clear lowers them. Hook: a small bridge system subscribing to `WGCrewAlertEvent`,
`WGCrewSecurityIncidentEvent` and `WGCrewAlertClearedEvent` and calling `ShipAlertSystem.SetCode`,
`SetGeneralQuarters`, `SetDistress` and `ShipPaSystem.Announce`†. A per-ship setting says whether the crew may do
this without a player on the bridge. Must be designed with the in-flight GQ work so the two do not loop (crew react
to GQ; GQ was raised by crew). Q10.

**5.5 Status reports on request (S-M).** A player with standing asks the radio officer for a sitrep and gets hull,
guns, crew on their feet, intruders known. Hook: a verb on the radio operator, like the surgeon's cloning verb;
data from `WGCrewShipStatusSystem` (`HullShare`, `GunCount`, `IsAdrift`) and the alert's hostile list. The
`GetVerbsEvent<Verb>` pair on `WGCrewComponent` is held by `WGCrewSetupSystem`; add to that handler or subscribe on
`WGRadioOperatorComponent`, which has no directed subscriptions.

**5.6 Pilot reacts to his own ship (M).** Adrift, guns gone or plant out: he says so and, by stance, runs or holds.
Hook: `WGCrewShipStatusSystem` polled from `WGCaptainSystem`; `IsAdrift` is computed and used only for travel
budgets.

### P6. Ship's company

**6.1 Conversations (M).** Two crewmen near each other and a player exchange two or three lines. Hook: CrewBarks
gets paired kinds (`chat-a-n`, `chat-b-n`) and a pair picker in its sweep; the area limiter already keeps others
quiet. Content-heavy, code-light. Add the janitor's voice file while there.

**6.2 Noticing and calling (S-M).** A crewman who sees a puddle or dirt calls the janitor; who sees someone hurt
calls a medic; who sees a body says so. Hook: the patrol sweep from 4.4; the call is an intercom line plus a hint to
the target system (`WGCrewMedicSystem.WantsMedic` already has the "calling for a medic" idea; give the janitor an
equivalent queue through the claim system).

**6.3 Janitor beyond mopping (M).** Picks up trash and bags it, replaces dead light tubes, puts a wet-floor sign by
a large spill and collects it after. The Lupercal's Housekeeping has the light replacer, trash bags and two signs.
Hook: more branches in `WGCrewJanitorCompound` after puddles; each a utility query plus an interaction, as the
puddle branch is. Risk: real items in hands conflict with the "mops with his own hands" kit; keep trash and lights
on the same built-in pattern or accept visible tools. Q11.

**6.4 Deckhands at the docks (M).** The dock-lobby deckhand walks to an arriving authorised ship's port, greets, and
goes back; on an order, carries crates between pallets and the warehouse. Hook: `DockEvent` is broadcast and already
used by security; cargo carrying is the Salvage job with an on-grid source and destination markers.

**6.5 Bodies (S-M).** The fallen are taken to the morgue (paramedics do this now), their weapons returned to the
armory rather than left as loot, and when the drawers are full the oldest are cremated or shipped off. Hook: a
Resupply-style job for loose crew weapons on the home grid with a rack as destination. Q12.

**6.6 Individual temperament (S).** Each crewman rolls a trait at spawn (talkative, curt, nervous, by-the-book)
that weights bark pools and idle activities. Hook: a field on `WGCrewBarkerComponent`; pure content after that.

### P7. Orders from the bridge

**7.1 Orders for every role (M).** Deckhands: Patrol, Dock posts, Damage control stations. Engineer: Rounds, Post.
Janitor: Clean, Stand by. Medics: Triage all, Staff only, Stand by. Hook: `WGShipBehaviourSystem` grows one enum per
role; consumers are `WGCrewRoutineSystem` (make the deckhand filter read orders), the janitor compound (a
precondition), and `WGCrewMayTreatEvent`. UI: the existing behaviour tab. Do not reset marine rounds on unrelated
changes (G13).

**7.2 Recall, send, hold (M).** "All hands to posts", "Marines to the docks", "Hold fire aboard". Hook: recall is
the watch bill's alert state on demand; send is a temporary post inside a named zone for a role; hold fire is
`SetEngagement(WhenAttacked)` for the on-sight roles until lifted. All three are ShipCommand console actions over
existing `WGCrewSystem` setters.

**7.3 Live crew settings (S).** Skill, infinite ammunition, auto repair and the boarding rule on the console, applied
to living crew at once. Hook: `WGShipConsoleSystem` plus a loop like `WGShipBehaviourSystem.ApplyAll`.

**7.4 Crew board (M-L).** A console page listing each post: who, where (zone), what he is doing, health. Hook: the
activity keys the admin Crew Setup window already shows (`crew.ftl:197-232`) and the roster slots; a new BUI state.
Client code: mind the sandbox rules.

**7.5 Orders in person (M).** The Boss uses a verb on a crewman: follow me, wait here, back to your post. Hook: the
escort operator from 4.2 with the roles swapped; standing check through `WGShipCommandSystem.IsBoss`. Risk: players
parking the pilot in a cupboard; exclude helm, guns and radio, or expire after a few minutes.

### P8. Losing, leaving and going elsewhere

**8.1 Individual surrender (M).** A lone crewman, hurt and outnumbered with no muster possible, drops his weapon and
kneels; a captor can cuff him or leave him. Hook: `WGCrewMusterSystem` already knows when a crew is losing and has
no leader; `WGShipSurrenderSystem` already pacifies a whole crew through `WGSurrenderedCrewComponent`†, so apply it
per mob. By skill and profile: Reavers never, hauler deckhands often.

**8.2 The citadel (M).** A commanded ship losing its fight pulls survivors behind the core gates and holds there
instead of dying in the corridors. Hook: muster's rally point chosen from a zone (`core`) and the standing order to
allow it. The Lupercal's eight sentries cover the gates already.

**8.3 Abandon ship (L).** On an encounter ship that is wrecked or dark with boarders aboard, survivors make for a
docked ship or pods and leave. Hook: `WGCrewShipStatusSystem.IsDisabled`, `WGCrewWorkSystem.PlantOut`, the return
operator's port walking. Interacts with encounter rewards and deeds; needs the Encounters owner's view.

**8.4 Boarding parties from a commanded ship (L).** The bridge orders marines across a dock to take a ship. Hook:
the raid machinery (`WGCrewObjectiveSystem.IsRaiding`, the Loot objective, `OnErrand` at `Reload.cs:310-317`) exposed
as a ShipCommand order. Largest balance question in this document: Q13.

### Suggested order

P0, then P1 (most visible change for the least risk), then P3.1 and P4.5 as stand-alone improvements, then P2 with
the needs work when it lands, then P5.4 with the GQ work. P4.2 and P4.3, P5.1 and P7 wait on the owner's answers.
P8 is last.

## 4. Open questions for the owner

1. **Captain.** Should the Lupercal have an NPC captain at all, given the owner or an admin is the Boss? If yes, is
   he a job players can take?
2. **Voicelines.** Is the `WGCrewSpeechSystem` TODO waiting for recorded voice, or can text lines go live? Should
   encounter crews talk on foot (patrol, reload, engaging), or stay as quiet as they are?
3. **Detention.** Is being cuffed and held by NPCs acceptable for players on the Lupercal? If so, where: a new brig
   room, or chairs in the mech bays or at the gates? How long at most?
4. **Shelter.** Should non-combatants run for the bridge (README) or cower and flee where they are (code)? On the
   Lupercal the bridge is behind the core gates.
5. **Rest.** The Lupercal has no bunks since the Quarters became a sauna. Do off-watch crew sleep anywhere, or only
   sit, eat and drink? Are crew allowed in the guest lounge and bar among players, or kept to staff break rooms?
6. **Fire.** Is the goal to take `WGNoFireSpread` off the Lupercal once crew can fight fires, or does it stay as a
   safety net?
7. **Weapons in hand.** Should the put-it-away rule cover melee weapons and tools that are weapons (a welder, a
   crowbar)? Guests carry tools aboard a working ship.
8. **Gunners.** Two gunners: one console each with the batteries split, or one on watch and one relief?
9. **The engineer.** Do the fusion reactor and the zero-point generator need anything from him, or is he there for
   the fission plant and for show? Should the Lupercal be commissioned and stocked like an encounter ship?
10. **Alarms.** May the crew raise general quarters and the alert code themselves, or only report and leave the
    alarm to a player on the bridge?
11. **Janitor's tools.** Built-in hands (as now) or real items he fetches from Housekeeping, which players could
    take?
12. **The dead.** The fallen keep their kit and are replaced up to 30 times an hour. Should replacements' kit be
    worthless, bodies be cleared, or is this an accepted loot source on an admin ship?
13. **Reach.** Is this work for the Lupercal and commanded ships only, or should encounter crews get the same life
    (seats, watches, damage control)? Encounter ships are short-lived and most of P1-P3 is wasted on them unless
    players board and linger.
14. **Counter staff.** Should they stay invulnerable fixtures, or become killable crew with a post, so that robbing
    the quartermaster is possible and has consequences?
15. **Performance budget.** Idle crew sleep when no living player is within 64 tiles. The Lupercal is probably wider
    than that (not measured here). Is it acceptable that the far side of the ship is frozen (no cleaning, patrols or
    watch changes) until someone walks over, or should a commanded ship with any player aboard keep its whole crew
    awake?

## Appendix: after this file lands

Adding this file changes the NpcCrew module's generated file list. Run `python Tools/_WG/Ci/modules.py --write` and
commit the README with it. That was not done here, because other agents have uncommitted work in the same tree.
