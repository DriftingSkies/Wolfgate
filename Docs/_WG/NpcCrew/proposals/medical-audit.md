# NPC medical crew: audit and expansion proposal

The owner's complaint: "Medical crew are shallow, and don't behave naturally in response to players." This is a read
of what the paramedic and the surgeon do today, what is wrong with it, and a phased plan to make them a medical
team a player can deal with.

Audited at `de8017c625` on `wg-npc-crew-life`. Nothing was built or run. Line numbers are for that commit. A claim
marked (inferred) follows from reading two or more systems together and wants a test before anyone fixes it;
everything else is read straight off the cited lines.

The working tree moved while this was written: other sessions were editing the medic system, the crew HTN,
CrewBarks, the roster and the autodoc, uncommitted. Section 2.1 lists what was seen there. Sections 1 and 2.2
describe the committed code, with a note wherever the working tree already changes the answer, and section 3 builds
on the in-flight systems instead of proposing them again.

Short names used below:

| Short | File |
|---|---|
| `Medic.cs` | `Content.Server/_WG/NpcCrew/Systems/WGCrewMedicSystem.cs` |
| `Medic.Surgery.cs`, `.Table.cs`, `.Cloning.cs`, `.Carry.cs`, `.SelfCare.cs` | its partials, same folder |
| `MedicComponent.cs` | `Content.Server/_WG/NpcCrew/Components/WGCrewMedicComponent.cs` |
| `medics.yml`, `htn.yml`, `mobs.yml` | `Resources/Prototypes/_WG/NpcCrew/` |
| `medics.ftl` | `Resources/Locale/en-US/_WG/NpcCrew/medics.ftl` |
| `Command.Crew.cs` | `Content.Server/_WG/ShipCommand/WGShipCommandSystem.Crew.cs` |
| `Security.cs` | `Content.Server/_WG/NpcCrew/Systems/WGCrewSecuritySystem.cs` |
| `Crew.cs` | `Content.Server/_WG/NpcCrew/Systems/WGCrewSystem.cs` |
| `Revival.cs` | `Content.Server/_WG/Wolfmed/Life/WolfmedRevivalSystem.cs` |
| Lupercal map | `Resources/SharedMaps/_WG/AdminVessels/lupercal.yml` |

## 0. Summary

The medics are a competent state machine with almost no surface. They find the worst-off body once a second, do
the right first aid in the right order, carry people to beds, run the autodoc, operate by hand and clone. All of
that is silent bookkeeping from the player's side: nine spoken lines for the paramedic, no way to ask for help
while standing, no explanation, no consent, no goodbye, no tools in anyone's hands, and whole classes of Wolfmed
injury that no medic looks at. Around that sit several interactions with other crew systems that make them act
against the patient: a guest carried into Surgery is a trespasser there, the medics count as fighters when the crew
is attacked, and they sleep when the only players near are unconscious or dead.

Phases proposed: 0 Repairs, 1 Bedside manner, 2 Triage and teamwork, 3 Real tools, 4 Patient flow, 5 Supply,
6 Life in the medbay, 7 Alert codes and mass casualties.

## 1. Current behaviour

### 1.1 How a medic picks and works a patient

Once a second (`Medic.cs:171-178`) the system forgets stale care records, runs crew self-care, and for each living
medic: a surgeon ticks cloning; a medic with a patient checks he should still have them and is not stuck; a free
medic looks for a patient and takes the best one (`Medic.cs:182-201`). This runs in the system, whether or not the
medic's HTN brain is awake.

Candidates are every `WoundHostComponent` body on the medic's home grid and on ships docked to it
(`Medic.cs:794-804`, `:820-823`), less himself, anyone another medic has claimed, anyone he gave up on in the last
minute, and anyone the ship refuses (`Medic.cs:823-825`). Each gets a priority (`Medic.cs:860-871`): 0 dead or in
cardiac arrest, 1 unconscious, 2 downed and bleeding, 3 downed, 4 flagged by Wolfmed's call for help or an NPC
crewman over 40 damage, 5 lying on a medical bed. Anyone else is not a patient. Lowest priority wins, then nearest
(`Medic.cs:844-850`).

Rank decides who may take whom (`Medic.cs:833-834`). A surgeon takes only patients lying on a medical bed. A
paramedic takes everyone else, and bed patients too unless they are "serious" (over 100 total damage,
`MedicComponent.cs:99-100`) while a surgeon is alive aboard. A candidate the medic has nothing to do for is skipped
(`Medic.cs:837-839`).

Walking is the HTN's: the medic branch (`htn.yml:59-72`) holsters, walks to the patient or to the bed with the
patient in tow (`Medic.cs:335-344`), then calls `Perform` every tick. The branch sits below the grab, fall-back,
grenade, fight, muster and brace branches (`htn.yml:9-58`) and above work, post and idle.

`Perform` (`Medic.cs:350-430`) looks once and does one thing: attend the pod, work the table, finish a carry, walk
closer, or pick the next treatment. The next treatment (`Medic.cs:436-491`) is, in order: shock a heart that can
be restarted (a paramedic gives three on the spot, then only once the patient is in a bed), CPR for an arrest that
can't be shocked, a tourniquet on a limb's arterial bleed, gauze on any other bleed, epinephrine for the
unconscious, dylovene above a toxin load of 10, and then, unless he is a paramedic with a serious patient,
brutepack, medicated suture and regenerative mesh. Shock, tourniquet and dressing are "urgent" and start at once
(`Medic.cs:400`). For anything else a paramedic first asks whether the patient needs taking in (downed or serious
and not in a bed, `Medic.cs:636-642`); if so he picks the nearest free medical bed to the patient, or a morgue
drawer for the unrevivable dead (`Medic.cs:648-698`), and carries them there before any further treatment
(`Medic.cs:401-409`). A surgeon checks the pod, then the table (`Medic.cs:416-420`). With nothing left the medic
lets go without a word (`Medic.cs:422-426`).

Each treatment is a do-after of 2 to 6 seconds that breaks if the medic moves or is hurt, or the patient gets more
than two tiles away, and needs no free hand (`Medic.cs:495-510`). Starting one says an "arrive" line if the medic
has not spoken for 20 seconds and shows a small popup on the patient (`Medic.cs:519-521`, `:955-963`).

### 1.2 What a player meets, case by case

**Walks into the ward hurt, on their feet.** Nothing happens. A conscious, standing player has no priority
(`Medic.cs:860-871`), and Wolfmed's call for help exists only while Downed
(`Content.Server/_WG/Wolfmed/Consciousness/WolfmedCallForHelpSystem.cs:70-76`). The paramedic may say the one guest
greeting, "Welcome aboard. The ward's that way if you need it."
(`Resources/Locale/en-US/_WG/CrewBarks/paramedic.ftl:10`), while standing in the ward. The paramedic has no verb.
The surgeon's only verb is the cloning record (`Medic.Cloning.cs:52-74`). The player has to work out that lying on
a medical bed is the trigger.

**Lies down on a medical bed.** Priority 5. Within a second the surgeon (any bed patient) or the paramedic (minor
cases, or all of them with no surgeon) claims them, walks over, says one of "Medic! Hold still.", "I've got you.
Stay with me." or "Easy now, let me see." (`medics.ftl:4-6`, the same three for both ranks), and runs three-second
passes of brutepack, suture and mesh until the items report nothing left (`Medic.cs:483-488`, `:627-633`). Then he
walks back to his post without a word. A player with nothing the items treat is ignored in silence. Pain, a
fracture, an infection, low blood, radiation and a lodged round are not looked at (see "other conditions").

**Asks for help.** Nobody is listening. No medic carries `ActiveListenerComponent`, and nothing in NpcCrew or
CrewBarks handles `ListenEvent` or `EntitySpokeEvent`. There is no treatment verb, no reaction to pointing or
emotes, and the medics do not hear the radio. The only request that works is the Downed call-for-help action, which
raises a priority-4 flag (`Medic.cs:868`).

**Collapses in a corridor, Downed but conscious.** Priority 3, or 2 if bleeding. Only a paramedic leaves the ward;
the surgeon never does (`Medic.cs:833`). The paramedic stops the bleeding first. Then, because the patient is down
and not in a bed, he picks them up at once, with no do-after and no weight rule
(`Medic.Carry.cs:15-21`, `Content.Server/_WG/NpcCrew/Systems/CarryingSystem.Crew.cs:9-16`), walks them to the bed,
snaps them onto it from up to a tile and a half away and buckles them (`Medic.cs:759-760`), says "There. The doctor
will see you." and leaves (`Medic.cs:763-764`). The take-in line ("Let's get you to Medical.") is asked for at
`Medic.cs:406` but shares the 20-second gap with the arrive line said moments earlier, so the pick-up is usually
silent. The patient is never asked.

**Bleeding.** A limb with an arterial bleed gets a tourniquet (`Medic.cs:452-460`), any other bleed gauze
(`Medic.cs:462-469`). An arterial bleed in the torso or head is bandaged once, which only slows it, and then waits
for the autodoc. No medic code ever loosens or tracks a tourniquet; it comes off only if something closes the wound
under it, and Wolfmed starts the limb dying at ten minutes
(`Content.Server/_WG/Wolfmed/Wounds/WolfmedNecrosisSystem.cs:146-166`).

**Critical or unconscious.** Priority 1. Bleeding first, then the carry to a bed, then epinephrine once a minute
from whichever medic holds them next (`Medic.cs:474-475`, `:574-576`). The drug is added straight to the
bloodstream (`Medic.cs:611-614`).

**In cardiac arrest.** Priority 0. If Wolfmed would allow a shock, the paramedic "puts the paddles on" for four
seconds, says the defibrillator's own result line in his voice (`Medic.cs:554-567`), and repeats every eight
seconds, three times where the patient lies and then on a bed. If the shock is refused (no blood, too cold, no
heart) the answer is CPR, which is not urgent, so the paramedic carries the patient to a bed first and starts
compressions there (`Medic.cs:400-409`). CPR never ends on its own (`Revival.cs:354-362` always succeeds) and no
medic can give blood: nothing in the medic code touches a blood pack or an IV. Only the autodoc's reservoir
transfuses (`Content.Server/_WG/Wolfmed/Autodoc/AutodocSystem.Triage.cs:76-82`).

**Dead.** Also priority 0. A revivable body is shocked as above, and on success the ghost is offered its body
(`Medic.cs:594-601`). A body that only lacks blood, a heart or warmth goes to a bed for the surgeon
(`Medic.cs:107-111`, `:662`). An unrevivable body goes to the nearest closed morgue drawer with room
(`Medic.cs:665-672`, `:745-755`). The paramedic says the same lines to a corpse as to the living: "You're coming
with me.", then at the drawer "Rest. We'll take it from here." (`Medic.cs:406`, `:763`). Nobody tells the dead
player's friends where the body went. A player on file is cloned by the surgeon after two minutes, or at once when
the body is gone or past saving (`Medic.Cloning.cs:233-245`).

**Poisoned.** Dylovene, 10 units once a minute while the toxin load is over 10 (`Medic.cs:477-478`), and only once
the player is a patient for some other reason. A poisoned player on their feet gets nothing.

**Irradiated.** Nothing. The medic code never reads radiation. A player who collapses from it is carried to a bed
and given epinephrine.

**Missing a limb or organ.** On their feet, nothing. On a bed, the surgeon says "You need the autodoc. Let's go.",
unbuckles and carries them to the pod, and puts a brand-new limb of the species' default kind in its tray,
spawned for the occasion (`Medic.Surgery.cs:189-220`, `:383-399`). He does not look for the limb that came off.
With the pod busy he does the same at a table by hand (`Medic.Table.cs:45-55`). Brains are never replaced
(`Medic.Surgery.cs:175`).

**Other Wolfmed conditions.** The medics treat seven things: arrest, bleeding, unconsciousness, toxins, brute,
cuts and burns. Wolfmed models many more, each with real items and a line on the analyzer
(`Resources/ServerInfo/_WG/Wolfmed/Guidebook/Medical/WoundTreatment.xml`). No medic acts on pain (no analgesic, so
a patient downed by pain stays down), fractures and dislocations (no splint, no relocation), infection and sepsis
(no antiseptic, no spaceacillin), embedded rounds and shrapnel, blood volume, burn fluid loss, hypothermia and heat
stroke, airway and lung failure, opiate overdose, necrosis or concussion. The autodoc covers some of them, but the
surgeon only pods a patient who is missing a part, or is dead, arrested, down or serious and has surgical work in
the pod's preview plan (`Medic.Surgery.cs:70-84`). The preview leaves out the pod's transfusion, shock and
antibiotic steps (`AutodocSystem.Triage.cs:49-51`, `:69-90`), so a patient who needs only blood or antibiotics is
never podded. "The awake and walking are left to the bed" (`Medic.Surgery.cs:44-48`).

**Refuses help.** There is no refusal. A conscious patient who walks more than two tiles away cancels the do-after;
the medic follows and starts again, gives up after 30 seconds without getting nearer and comes back a minute later
(`Medic.cs:245-283`). A carry cannot be declined. Shoving or hitting the medic is an attack like any other: he
remembers it for 60 seconds (`medics.yml:10-11`), will not treat that person (`Medic.cs:879-884`), and the whole
crew's fighters answer it unless the attacker is the Boss (`Crew.cs:148-183`, `:231-254`).

**Tries to leave.** Being fixed in this branch. The cause is that a patient who gets off the bed while downed or
over 100 damage "needs taking in" again (`Medic.cs:636-642`). In the working tree only the downed are carried, and
a patient on their feet and out of bed is let go with "Suit yourself. Come back if it gets worse."

**Barred, an attacker, an enemy.** `MayTreat` (`Medic.cs:873-889`) refuses anyone inside a container, anyone being
carried or pulled by somebody else, and whoever attacked this medic in the last minute; then the ship is asked.
ShipCommand refuses the barred and anyone who attacked the crew or is on its shared alert (`Command.Crew.cs:57-62`,
`:149-161`). The refusal is silent: a barred player on a ward bed is simply never visited. The test is repeated
while the medic holds a patient (`Medic.cs:781-783`), so someone who turns hostile mid-carry is dropped where the
medic stands. There is no faction test at all: a hostile NPC boarder lying downed is a patient like any other once
the alert has lapsed.

**Another NPC.** Crew on their feet heal themselves, one item every eight seconds, instantly and invisibly
(`Medic.SelfCare.cs:34-78`). A crewman over 40 damage counts as calling for a medic (`Medic.SelfCare.cs:81-85`).
Downed and dead crew are treated exactly like players, and a treated crewman on a barking ship says a thank-you
(`Content.Server/_WG/CrewBarks/WGCrewBarkSystem.Reactions.cs:66-70`). A player seen carrying a downed crewman is
told to put him down and has ten seconds (`Content.Server/_WG/NpcCrew/Systems/WGCrewConductSystem.Bodies.cs:24-96`).

**Brought in by a friend.** While the friend pulls or carries the patient the medics ignore both, without saying
why (`Medic.Carry.cs:40-44`). Put down on the ward floor, the patient is "downed" and the paramedic takes them.
Buckled to a ward bed, they are a bed patient.

**On a docked ship.** Docked ships are in the paramedic's area (`Medic.cs:794-804`). He walks aboard, prying doors
in his way like any crewman (`mobs.yml:21-24`), treats, and carries the downed back to his own ship's ward.

**A synthetic.** Not handled apart. None of the medic's items or drugs act on a chassis, so an IPC is carried to a
bed and waits for the pod's weld and rewire programs, if the pod rule lets it in.

### 1.3 What the medics say

The paramedic has nine lines: three on arrival, three on taking in, three on delivery (`medics.ftl:4-12`). The
surgeon reuses the arrival lines and adds three each for taking to the pod, starting it, finishing, taking to the
table, cutting and closing (`medics.ftl:25-45`), plus the cloning lines. One line per 20 seconds per medic, picked
from exactly three variants (`Medic.cs:102`, `:955-963`). No line names the patient, the injury or the treatment.
The working tree adds three lines for a patient who walks off.
The treatment popups (`medics.ftl:14-22`, `:42`) do name the action, in the third person, for whoever is looking at
the patient.

Separately, CrewBarks gives each medic a salute and greeting for the Boss, one guest greeting (paramedic only),
four idle thoughts and a thank-you (`paramedic.ftl`, `surgeon.ftl`). Those run on their own timers. A barker counts
as busy only while he has hostiles (`Content.Server/_WG/CrewBarks/WGCrewBarkSystem.Sweep.cs:111-114`), so "A quiet
ward is a happy ward." can come out during chest compressions and "Not one spleen all shift. Suspicious." during
surgery.

### 1.4 What is real and what is not

| Act | What happens | Real item or system |
|---|---|---|
| Gauze, brutepack, suture, mesh | one off-map item per kind, never used up (`Medic.cs:617-625`), applied by `WoundHealingSystem.TryApplyHealing` (`Medic.cs:603-609`) | effect real, item not; the hand-item path's side effects are skipped (defect 12) |
| Epinephrine, dylovene | `BloodstreamSystem.TryAddToChemicals` (`Medic.cs:611-614`) | reagent real, no pen or syringe |
| Tourniquet | `TourniquetSystem.Apply` (`Medic.cs:570-571`) | effect and necrosis clock real, no item |
| Shock | `WolfmedRevivalSystem.TryDefibrillate` and a sound (`Medic.cs:554-567`) | revival rules real, no defibrillator, no charge |
| CPR | `StartCpr` for ten seconds (`Medic.cs:568-569`) | real |
| Carry | `CarryingSystem.TryCrewCarry`, else a pull (`Medic.Carry.cs:15-21`) | real, minus the do-after and weight rule |
| Autodoc | real pod, real programs; bottles, limbs and organs spawned free (`Medic.Surgery.cs:337-399`) | pod real, stock not |
| Table surgery | real Shitmed steps from an off-map "instruments" autodoc (`Medic.Table.cs:97-106`, `medics.yml:35-48`) | steps real, tools invisible |
| Cloning | real database, pod and consent prompt (`Medic.Cloning.cs`) | real |

The paramedic wears a real `ClothingBeltMedicalFilled` (`medics.yml:62`) and never opens it. On the Lupercal the
medbay holds ten medical beds, a stasis bed, four operating tables, one autodoc, three cryo pods, six IV stands,
six roller beds, four defibrillator cabinets, two analyzers, blood packs, medkits, fourteen stocked lockers and two
medical vendors. The medics use the beds, the tables, the pod, the cloner and the morgue.

## 2. Defects and shallow spots

### 2.1 Being fixed in this branch, with what sits next to each

The first four are the ones named in the brief. The rest were found uncommitted in the working tree and bear on
the medics. None is proposed again below.

**Surgeon deletes clothing.** The deletion is the pod's own shears, which cut the suit and jumpsuit after a delay
when a step is blocked by clothing (`AutodocSystem.Undress.cs:116-128`, `QueueDel` at `:126`). The working tree has
the attending surgeon take the blocking clothes off whole while the pod waits, onto the floor beside him, and has
the table path drop them beside the patient (`AutodocSystem.Hand.cs`, `Medic.Surgery.cs` `Attend`). Next to it:
the clothes now lie on the Surgery floor, in a zone the patient may not be allowed back into, and nobody dresses
the patient, says where their things are or brings them to the bed; a garment the surgeon cannot take off (locked,
unremovable) still goes to the shears once the delay runs out; a player-run pod still cuts; and `LeaveTable`
deletes whatever is in the surgeon's off-map tray (`Medic.Table.cs:385-386`), which must stay a spare part only.

**Paramedic won't let patients leave the bed.** The working tree carries only the downed (`NeedsTakingIn`) and
adds `WalkedOff`: a patient on their feet, out of bed, out of his hands and with no priority is released with a
line. Next to it: `WalkedOff` has no test for movement or distance, so a patient the medic's own treatment gets
back on their feet is dismissed with "Suit yourself" where they stand, wounds unpatched; the line goes through the
20-second gap and is often swallowed; the surgeon's `TakeToPod` and `TakeToTable` still unbuckle and carry a
conscious bed patient (`Medic.Surgery.cs:230`, `Medic.Table.cs:124`) with nothing to remember a "no" except the
pod's two-minute rest (`Medic.Surgery.cs:33`, `:312`); and a "left by choice" memory belongs in
`WGCrewPatientCare`, so a patient who lies down again five seconds later is not treated as new. NPC patients have
the opposite fault: nothing in the crew brain unbuckles (no `UnbuckleOperator` in `htn.yml`), so a crewman put to
bed stays there for the round (inferred), holding one of ten beds.

**Medipens, dropped after use.** The working tree adds `InjectPen`: a fresh pen is spawned for each dose, used
through `HypospraySystem.TryDoInject` and dropped beside the patient, with the bare dose as the fallback, and adds
brute and burn auto-injectors at 40 damage in the group, each on a one-minute clock. Next to it: the pen is never
in the medic's hand, and the supply is endless; a patient who stays over the line gets a brute pen, a burn pen and
(while unconscious) an emergency pen every minute, so check each pen's contents against Wolfmed's overdose and
sedation lines for a ten-minute stay; "unconscious" includes a pain faint of a few seconds (`Medic.cs:933-937`),
which now costs a pen; and every dose leaves an entity on the floor for the round. The crew janitor mops puddles
and washes dirt and picks nothing up (`htn.yml:184-226`), so a busy ward collects hundreds of spent pens. The
break system in the same working tree already walks rubbish to a disposal unit (`WGCrewDowntimeSystem.FindBin`);
the medic can bin his pens the same way when the case ends.

**A second paramedic and surgeon.** Claims stop two medics taking one patient (`Medic.cs:135`, `:824`), and beds,
pods and tables are reserved by reading the other medics' destinations (`Medic.cs:653-659`,
`Medic.Surgery.cs:104-114`, `Medic.Table.cs:63-73`). Not covered: there is one autodoc, so the second surgeon
always operates by hand, and the fourth table is across the ship at (-35.5, 10.5); both paramedics will leave the
ward together, since nothing keeps one at the door; two medics of one voice share four idle lines, and "never the
same line twice" is per speaker; and a paramedic doing CPR on a bed patient keeps both surgeons away (defect 10).

**Readiness from the ship's alert codes (found in the working tree).** `WGCrewReadinessSystem` subscribes to
`ShipAlertCodeChangedEvent` and `ShipGeneralQuartersChangedEvent`, maps Green, Yellow and Red or Black or general
quarters to Normal, Alert and Battle, ends breaks, replans every crewman and has each say a line. A new HTN branch
sends everyone but the helm and the guns to their post at Battle. It sits below the medic branch, so a medic with
a patient keeps working. Next to it: the medics get the common lines ("General quarters! Moving to my station!"),
said whatever they are doing, a surgeon mid-operation included; nothing changes what Medical does at each level
(Phase 7); and a paramedic is still pulled out of the ward into the fight by the first casualty.

**Breaks (found in the working tree).** `WGCrewDowntimeSystem` sends roster crew, medics included, for a snack,
a drink or a sit-down every 8 to 16 minutes at Code Green, up to 40 metres from where they stand, and ends the
break within a second of a patient being assigned. Next to it: nothing stops both paramedics, or both surgeons,
being away at once; and a medic called off his break "holding whatever he holds" arrives with a hand full, which
matters to the carry now and to every hand-held tool later.

**Revived crew keep their post (found in the working tree).** `WGShipRosterSystem.OnMemberState` clears the
vacancy when a crewman is brought back before his post is refilled. After the refill the duplicate remains
(defect 22).

Also in the working tree and not medical: the janitor's pest hunt, a claim check so two janitors do not take one
puddle, and a rule that crew leave harmless animals alone.

### 2.2 Ranked by how visible each is to a player

1. **A standing, wounded player does not exist to them, and cannot ask.** `Medic.cs:860-871`; call for help is
   Downed-only (`WolfmedCallForHelpSystem.cs:70-76`); no listener, no treatment verb (`Medic.Cloning.cs:52-74`).
   This is the first thing every visitor to the ward meets.

2. **No diagnosis, no explanation, no goodbye.** One of three generic lines per 20 seconds (`Medic.cs:519`,
   `:955-963`), the take-in line usually swallowed by that gap (`Medic.cs:406`), and a silent release
   (`Medic.cs:422-426`). The player learns what was done from popups and what is still wrong from nobody.

3. **No consent.** A conscious player is picked up instantly (`Medic.Carry.cs:15-21`), unbuckled and put in the
   pod (`Medic.Surgery.cs:223-254`), stripped and anaesthetised at the table (`Medic.Table.cs:235-236`, `:280`),
   and given a new limb, with no question and no way to say no.

4. **Lines that do not fit.** Corpses are told "You're coming with me." and "Rest. We'll take it from here."
   (`Medic.cs:406`, `:763`). The surgeon shouts "Medic!" (`Medic.cs:519`). Idle jokes and greetings continue
   through CPR and surgery (`WGCrewBarkSystem.Sweep.cs:111-114`).

5. **A patient is a trespasser wherever the medic puts them (inferred).** Nothing exempts a patient from the
   zone rules: `Welcome` judges by standing and the zone underfoot (`Command.Crew.cs:91-146`), and the boarding
   sweep takes any living, unauthorized person a crewman can see (`Security.cs:71-79`, `:295-310`). On the Lupercal
   the autodoc, the Surgery tables, the surgeon's post and the two recovery beds at (36.5, 3.5) and (39.5, 3.5)
   stand in `medical-staff` (Lupercal map `:4824-4870`), and a patient leaving the pod at (45.5, 8.5) is taken to
   the nearest free bed, which is one of those two. A guest who wakes there is challenged, has 30 seconds, and is
   then answered as an attacker (`Security.cs:97-119`, `:336-359`). That makes them a shared hostile, which makes
   the medics refuse them (`Command.Crew.cs:149-161`) and drop them if mid-carry (`Medic.cs:781-783`). In the map
   as saved even `medical-public` is Locked (`:4801-4803`), so until the owner opens Medical this applies in the
   ward too.

6. **The medics are fighters (inferred).** `AnswerAttack` gives every crewman who is not Never-engage, a pilot or
   a gunner the attacker as his own (`Crew.cs:221-244`). That includes both medics, whose role is WhenAttacked and
   Guard (`medics.yml:74-96`). With that memory the fight precondition passes
   (`Content.Server/_WG/NpcCrew/HTN/WGCrewMayFightPrecondition.cs:17-28`) and the fight branch outranks the medic
   branch (`htn.yml:36-44` over `:59-72`). So when a boarder hits a marine, or a guest overstays a warning, the
   paramedic and surgeon leave their patients and go for him with their fists. `medics.yml:1-4` says they fight
   only when struck.

7. **Most of Wolfmed is untreated.** See "other conditions" in 1.2. The player-facing result: a medic patches the
   bruises on someone with a broken leg, a bullet in them and 50% blood, and walks away.

8. **Nothing is in their hands.** No analyzer, no gauze, no pen, no paddles, no stretcher. Treatments need no hand
   (`Medic.cs:508`) and draw on off-map items (`Medic.cs:617-625`). The belt, the cabinets, the IV stands, the
   roller beds and the cryo pods are scenery.

9. **Tourniquets are forgotten, and a ship with no surgeon stops at first aid.** No call to
   `WolfmedNecrosisSystem.Loosen` anywhere in the medic code. A paramedic returns no patching action for a serious
   patient whether or not a surgeon exists (`Medic.cs:480-481`), so on a paramedic-only ship (the JudgeLine crews,
   `Resources/Prototypes/_WG/JudgeLine/judge_line.yml:61-62`) the badly hurt are stabilised, bedded if there is a
   bed, and left. The working tree lifts this for a patient on their feet; for the downed and the bedded it stands.

10. **CPR without end, and it blocks the pod (inferred).** A paramedic's bed patient in an arrest he cannot shock
    gets compressions for as long as the patient lasts. His claim keeps the surgeon out (`Medic.cs:824`), and the
    surgeon's pod is the only source of blood.

11. **They sleep through the worst case (inferred).** The sleep pass counts only players whose mob state is Alive
    (`Content.Server/_WG/NpcCrew/NPCSystem.Sleep.cs:44-47`), within 64 tiles for crew (`mobs.yml:17`), and
    `StaysAwake` has no clause for a medic with a patient
    (`Content.Server/_WG/NpcCrew/Systems/WGCrewSleepSystem.cs:40-79`). A lone player who goes unconscious or dies,
    or a party that all goes down, leaves no Alive player near the medics. The system still assigns the patient
    every second, the brain never walks, and after 30 seconds the medic "gives up" for a minute
    (`Medic.cs:245-283`), forever.

12. **Treatment skips what the item would have done in a hand.** The hand path marks a suture closed for the
    infection clock, dresses weeping burns, cleans the part when the medic is sterile and changes blood level
    (`Content.Server/_WG/Wolfmed/Medical/HealingSystem.Wolfmed.cs:60-86`). The medics call `TryApplyHealing`
    directly (`Medic.cs:603-609`), so their sutures do not stop infection and their burn dressings do not stop
    fluid loss.

13. **No stall guard on the ward.** A treatment that "turned out to have nothing to do" returns false and leaves
    no memory (`Medic.cs:540-541`); the next look reads the same state and starts it again. Table surgery has a
    three-strike rule (`Medic.Table.cs:27`, `:338-340`), ward care none. A wound with a round in it refuses
    treatment (`Content.Shared/_WG/Wolfmed/Wounds/WolfmedEmbeddedObjectSystem.cs:15-18`) and the medic has no way
    to remove it, so a repeating three-second dressing is likely there (inferred), and because dressing is urgent
    it also holds off the carry (`Medic.cs:400-401`).

14. **Refusals are silent.** The barred, a recent attacker and a patient a friend is holding are skipped with no
    word (`Medic.cs:873-889`). Only the cloning verb says "I can't take your record." (`Medic.Cloning.cs:90-95`).

15. **Beds are just the nearest free one.** `PickDestination` takes the nearest free care bed to the patient
    anywhere on the grid (`Medic.cs:676-698`): the recovery beds in the Staff zone, the two by the morgue at
    (13.5, 36.5) and (13.5, 38.5), the stasis bed as an ordinary bed. A dead body that is "fixable" lies in a ward
    bed until someone pods it. Nobody is ever discharged, so beds only fill.

16. **Player medics and the NPCs work past each other.** A Shitmed step damages the body with the surgeon as
    origin (`Content.Server/_Shitmed/Medical/Surgery/SurgerySystem.cs:89-105`), and any damage to a crewman from a
    mob is an attack on the crew (`Crew.cs:148-183`), so a player operating on a downed crewman turns the ship on
    themselves unless they are the Boss (inferred). The Lupercal offers player Paramedic and Surgeon jobs with
    Staff standing (`Resources/Prototypes/_WG/AdminVessels/lupercal.yml:75-76`), who are not the Boss. A player
    treating a patient beside an NPC medic gets no acknowledgement and no help.

17. **New limbs from nowhere.** `Medic.Surgery.cs:383-399`. The severed limb on the deck is ignored though Wolfmed
    keeps it viable for five minutes, and the replacement is the body prototype's stock part, whatever the
    character looked like.

18. **Hostile NPCs are revived.** `MayTreat` has no faction test (`Medic.cs:873-889`). Once the alert lapses a dead
    raider is shocked back to life in the ward with nobody watching him.

19. **Only a paramedic collects.** The surgeon takes bed patients only (`Medic.cs:833`). With the paramedic dead,
    relieved or busy, a player dying ten tiles from an idle surgeon is left.

20. **Snaps and instant lifts.** The patient is teleported onto the bed or table (`Medic.cs:734`, `:759`) and
    lifted with no wind-up.

21. **The service ledger fills with single bandages.** Every finished do-after is one ledger entry and one admin
    log line (`Content.Server/_WG/ShipCommand/WGShipServiceSystem.cs:41-44`); the ledger keeps 500.

22. **Revival and restaffing make duplicates (inferred).** With `autoRestaff` (Lupercal prototype `:42`) a dead
    crewman's post is refilled and the old body only loses its roster mark
    (`Content.Server/_WG/ShipCommand/WGShipRosterSystem.cs:313-315`). If a medic then revives him he still has his
    crew, security and medic components. The working tree handles a revival before the refill; this is the case
    after it.

23. **Smaller.** Crew self-care is an instant heal with no animation (`Medic.SelfCare.cs:73`). `OfferReturn` is a
    copy of Wolfmed's (`Medic.cs:594-601`, `Revival.cs:267-275`). Line variants are hard-coded at three
    (`Medic.cs:962`). A patient arriving buckled to a roller bed is an untested path (carry, then a buckle while
    buckled).

## 3. Expansion proposal

### 3.0 Ground rules

Everything below stays in `_WG`. New medic behaviour goes in further partials of `WGCrewMedicSystem` (it already
has five), new lines in `medics.ftl`, new tests beside `WGCrewMedicTest` and `WGCrewSurgeonTest` in
`Content.IntegrationTests/Tests/_WG/NpcCrew`. No `PoolSettings { Destructive = true }`.

Real items through their own systems, no invented effects. The public entries that exist today:
`HealthAnalyzerSystem.BeginAnalyzingEntity` and `BuildVitals`
(`Content.Server/_WG/Wolfmed/Medical/HealthAnalyzerSystem.Vitals.cs:36`), `HypospraySystem.TryDoInject`,
`DefibrillatorSystem.CanZap` and `TryStartZap` (`Content.Server/Medical/DefibrillatorSystem.cs:95`, `:135`),
`WolfmedFluidPackSystem.TryStart` and `TryTransfer`, `WolfmedIvDripSystem.TryStartAttach`, `SetRate`, `SetMode` and
`Detach`, `WolfmedSplintSystem.CanApply` and `TryApply`, `WolfmedAntisepticSpraySystem.TrySprayOn`,
`WolfmedEmbeddedRemovalSystem.FindEmbeddedWound` and `TryRemoveOne`, `WolfmedNecrosisSystem.IsAtRisk`,
`IsHoldingABleed` and `Loosen`, `WolfmedDislocationSystem.TryRelocate`, `WolfmedVentilatorSystem.TryStartAttach`,
`WolfmedInfectionSystem.HasInfection`, `GetSepsis` and `CleanPart`, and Einstein Engines' `CPRSystem` for the
player's own CPR do-after. Upstream's medibot (`MedibotSystem`, `MedibotInjectOperator`) is the only other NPC
medic in the tree; it injects one reagent by damage threshold and has nothing to reuse beyond the idea.

The single source of "what is wrong with this patient" should be the analyzer's own report.
`WolfmedVitalsReport.Routes` (`Content.Shared/_WG/Wolfmed/Life/WolfmedVitalsReport.cs:165-166`,
`WolfmedRoutes` in `WolfmedRevivalComponents.cs:75-124`) names every process making the patient worse: bleeding,
internal bleeding, burn fluid, arrest, airway, lungs, circulation, sepsis, sedation, toxin, heat stroke, marrow,
hypothermia, vacuum. `DoFirstLine` (`:415-459`) already turns them into first-aid words. Triage, speech and the
toolset below all read that one report, so the medic and a player's analyzer never disagree.

Subscriptions. Only one system may subscribe a given (component, event) pair. The pairs that matter here:

| Pair | State |
|---|---|
| `(WGCrewMedicComponent, GetVerbsEvent<Verb>)` | taken, `Medic.Cloning.cs:49`. New verbs go in that handler. |
| `(WGCrewComponent, GetVerbsEvent<Verb>)` | taken, `WGCrewSetupSystem.cs:53` (admin verb) |
| `(WGCrewMedicComponent, MobStateChangedEvent)`, `(…, ComponentShutdown)`, `(…, WGCrewMedicDoAfterEvent)`, `(…, WolfmedSurgeryToolsEvent)` | taken by the medic system itself; extend, don't add |
| `(WGCrewComponent, MobStateChangedEvent)`, `(…, WolfmedConsciousnessChangedEvent)`, `(…, WGStripAttemptEvent)`, `(…, BeforeDamageChangedEvent)` | taken (`Crew.cs:99-100`, `WGCrewMusterSystem.cs:151`, `WGRadioOperatorSystem.cs:87`) |
| `(NPCRetaliationComponent, BeforeDamageChangedEvent)` | taken (`Crew.cs:101`); every medic carries the component |
| `(WGCrewBarkerComponent, BeforeDamageChangedEvent / DamageChangedEvent / DisarmedEvent)` | taken by CrewBarks (`WGCrewBarkSystem.Reactions.cs:21-24`) |
| `(WoundHostComponent, GetVerbsEvent<Verb>)`, `<AlternativeVerb>`, `<UtilityVerb>`, `InteractUsingEvent`, `AfterInteractUsingEvent`, `EmoteEvent` | all taken by Wolfmed (necrosis, dislocation, cautery, welding, embedded removal, body sounds). Put nothing on the patient's `WoundHostComponent`. |
| `(HealOnBuckleComponent, StrappedEvent / UnstrappedEvent)`, `(StasisBedComponent, …)` | taken by `BedSystem` |
| `(WGCrewMedicComponent, ListenEvent)` | free |
| `(WGMedicalBedComponent, StrappedEvent / UnstrappedEvent)` | free |
| any event on a new `WGCrewPatientComponent` | free by construction |
| `ShipAlertCodeChangedEvent`, `ShipGeneralQuartersChangedEvent` (broadcast) | raised (`ShipAlertSystem.cs:300`, `:340`); no subscriber at the audited commit, `WGCrewReadinessSystem` in the working tree. Read `Readiness(grid)` from it; don't subscribe again. |
| `WGCrewAlertEvent`, `WGCrewAlertClearedEvent`, `WGCrewMemberDownEvent`, `WGCrewTreatedEvent`, `WGShipPostStaffedEvent` (broadcast) | other systems subscribe; a broadcast takes one handler per system, so the medic system may add its own |

Each item below gives what the player sees, what it extends, a size (S about a day with its test, M a few days or
two modules, L a week or more, new UI or mapping) and the risks.

### Phase 0: Repairs

No design decisions except where marked. These remove the behaviour that works against the patient, and most of
the later phases assume them.

**0.1 Patients are not trespassers (M, decision).** Player sees: being carried into Surgery or waking in a
recovery bed never draws a challenge; on discharge the medic says which way is out and they have time to go.
Extends: `WGCrewSecuritySystem.ReceiveSighting` and its sweep ask the medic system whether the visitor is in care
(claimed by a living medic of that grid, in its pod, or on a bed or table a medic put them on, plus a leaving grace
that ends when they reach a zone that is theirs). `Command.Crew.cs:149-161` should not count a boarding-sighting
hostility against a patient. Risks: a pass into Surgery. Care starts only on a medic's initiative, never from
lying down on a staff-zone bed by oneself; the barred and attackers get none. The owner may prefer ShipCommand to
own this rule; then it is a field on `WGCrewAuthorizeEvent` instead.

**0.2 Medics keep working when the crew fights (S).** Player sees: during a boarding the medics treat the
wounded; they hit back only at whoever hits them. Extends: `WGCrewSystem.IsFighter` (`Crew.cs:221-225`) and the
rally, with a role flag on `wgCrewRole`. They must not fall into the "never fights" group either, which runs for the
bridge and lies down (`WGCrewShelterSystem`); this is a third kind, "keeps to his work". Risks: profiles that want
armed combat medics; make it the role's default, not a hard rule.

**0.3 Awake for a patient (S).** Player sees: a lone unconscious or dead player is still fetched. Extends:
`WGCrewSleepSystem.StaysAwake` returns true while `WGCrewMedicComponent.Patient` is set. The assignment already
runs for sleeping medics, so nothing else is needed. Risks: none found; the medic sleeps again on release.

**0.4 Lines that fit (S).** Player sees: no chatter at a corpse, a sober word to bystanders, the surgeon
introduced as a surgeon. Extends: `Medic.cs:406`, `:519`, `:763` choose keys by rank and by living, dead or morgue;
`medics.ftl`. Count the variants the way CrewBarks does (`WGCrewBarkSystem.cs:145-154`) so adding a line is a
Fluent edit.

**0.5 No small talk while working (S).** Extends: `WGCrewBarkSystem.Busy` (`Sweep.cs:111-114`) also asks the medic
system whether he has a patient. CrewBarks already depends on NpcCrew. Risks: none.

**0.6 Say why not (S).** Player sees: "I can't treat you. Captain's orders." to the barred, "Put them down here,
I've got them." to a friend holding the patient, once per person per few minutes. Extends: a refusal reason out of
`MayTreat` (`Medic.cs:873-889`) and one line per reason. Risks: spam; gate per (medic, person).

**0.7 A stall guard for ward care (S).** Extends: `WGCrewPatientCare` remembers a failed (action, part) for a
minute; `NextAction` passes over it; three different failures mark the patient "needs the surgeon". Risks: hiding a
real bug; log each stall at debug.

**0.8 CPR ends, claims yield, a lone paramedic does the patching (S-M).** Extends: a paramedic releases a bed
patient the surgeon could pod, and takes no bed patient in an arrest he cannot end while a surgeon is free;
`Medic.cs:480-481` applies only while `SurgeonOnDuty`; a surgeon with no bed patients collects from the deck when
no paramedic is free (`Medic.cs:833`). Risks: the two ranks trading a patient back and forth; give the hand-over a
cooldown in the care record.

**0.9 Tourniquet follow-through (S-M).** Player sees: the medic comes back to the limb, sutures under the strap
or loosens it, and says the time it went on. Extends: care record keeps (part, applied at); `IsAtRisk`,
`IsHoldingABleed`, `Loosen` (`WolfmedNecrosisSystem.cs:98`, `:324`, `:423`). Risks: loosening restarts the bleed;
only with gauze ready or on the way to the pod.

**0.10 One healing path (M).** Extends: lift the body of `HealingSystem.OnWoundHostDoAfter`
(`HealingSystem.Wolfmed.cs:30-145`) into a public method the hand path and the medics both call, so sutures mark,
burns dress, sterile hands clean and stacks are used. It is a `_WG` file. Risks: the do-after's repeat and popup
logic must stay with the hand path; Wolfmed's treatment tests cover the shared half.

**0.11 Beds empty again (S).** Extends: crew unbuckle themselves once up and not serious (upstream
`UnbuckleOperator` exists); fixable dead go to the stasis bed or straight to the surgeon's queue, never a ward bed.
Decide what a revived crewman whose post was refilled does: walk to the crew bay and leave, as a relieved one does
(`WGShipRosterSystem.cs:233-266`), is the cheap answer.

**0.12 One ledger entry per case (S).** Extends: `WGShipServiceSystem.OnCrewTreated` records on admission and
discharge with a summary; the per-action event stays for barks and tests.

**0.13 Surgery on a crewman is not an attack (M, decision).** Player sees: a player with a medical job can
operate on a downed crewman. Extends: `Crew.cs:148-183` passes over step damage from someone the ship welcomes,
on a crewman who is down and on a table or medical bed. Risks: murder by scalpel. Limit it to people on the staff
list or better, and let a death under the knife count as the attack it was.

### Phase 1: Bedside manner

The medic system owns clinical speech, because it carries information and must be said whether or not the ship
"barks". CrewBarks keeps manners (greetings, idle, thanks). The readiness and break systems in the working tree
already call `WGCrewBarkSystem.Bark` from NpcCrew, so the medic system can ask for a mannered line the same way.
All lines go through the normal chat as now.

**1.1 Notice the walking wounded (S-M).** Player sees: a hurt player who comes into the ward, or passes a
paramedic anywhere, is looked at and spoken to by name: "You're bleeding, {name}. Sit down, I'll see to it."
Minor hurts get an offer, not a pursuit. Extends: `Priority` (`Medic.cs:860-871`) gains a walk-in level between 4
and 5 for a standing player with any running route, an open bleed or damage over a threshold, within sight and a
few tiles of a medic or a medical bed; the CrewBarks guest greeting for medics becomes this observation. Risks:
medics pestering everyone with a scratch; offer once per person per ten minutes and act only on a yes (1.4).

**1.2 Three ways to ask (M).** Player sees: say "medic", "doctor" or "help" near one; use "Ask for treatment" on
one; or call for help when down, and get an answer: "On my way, {name}!" Extends: `ActiveListenerComponent` on the
medic kit (`medics.yml:6-11`) and `(WGCrewMedicComponent, ListenEvent)` with a keyword list in Fluent, as
`WGShipSquatterSystem.Talk.cs:50-59` does; the treatment verb inside the existing `OnGetVerbs`
(`Medic.Cloning.cs:52-74`, which today returns early for paramedics); an acknowledgement line when a call-for-help
flag is what won the patient. A request raises the asker, or the person they point at, to priority 4. Risks:
`ListeningSystem` walks every active listener per spoken message, which is fine for four medics and not for every
crewman; languages (the listener gets the obfuscated text for a tongue he lacks, treat that as "I don't
understand you"); keyword false positives, so require the medic to be within hearing range and not already busy
with a higher priority.

**1.3 Look first, then say what is wrong and what comes next (M).** Player sees: the medic takes out an
analyzer, scans for a second or two, and speaks the finding and the plan: "Arterial bleed, left leg. Tourniquet
now, then Surgery." Each new treatment gets its own first line ("Antitoxin going in.") instead of a repeat of
"Hold still". Extends: a scan step before the first action on a living, non-urgent patient
(`HealthAnalyzerSystem.BeginAnalyzingEntity`, `BuildVitals`); lines keyed by `WolfmedRoutes` bit and by
`WGCrewMedicAction`; the popups stay for onlookers. Risks: delay on the dying, so shock, tourniquet and dressing
skip the scan; line gap versus information, so clinical lines get their own short gap and never queue.

**1.4 Consent in three tiers (M).** Player sees: the unconscious, the arrested and anyone bleeding out are
treated at once, as now. A conscious patient is told and given three seconds ("I'm going to stitch that. Say if
you'd rather I didn't."). Anything invasive is asked and waits for a yes: being carried while able to walk,
opiates, anaesthesia, the pod, the table, cutting clothes, a replacement limb. Yes is a spoken word, the verb, or
lying down where pointed. Extends: consent state per patient in `WGCrewPatientCare` (a plain class the system
keeps, `MedicComponent.cs:20-39`), read at `Medic.cs:401-420`; the listener and verb from 1.2;
`PointingSystem.TryNpcPoint` for "that bed". Risks: an AFK or silent player waits for ever, so an unanswered
question falls back to the medic's judgement after a timeout only when the condition is worsening; consent must
not make unconscious care slower.

**1.5 No means no, and leaving is allowed (S).** Player sees: "Leave me be" by voice or verb gets "Your call.
You know where I am." and the medic goes back to his post. He does not follow a patient who walks off; he calls
after them once. Someone who gets up against advice hears the risk in one line and is let go. Extends: a declined
mark with an expiry in the care record, cleared when the patient worsens to an emergency tier; pairs with the
bed-leaving fix in flight. Risks: griefers declining for an unconscious friend; only the patient's own word counts.

**1.6 Thanks, abuse, blows (M, decision for blows).** Player sees: "thanks" gets a short reply in character;
abuse gets a dry one and no change in care; a single unarmed swipe from a patient in pain gets "Easy! I'm trying
to help you." and a step back instead of the whole crew. Extends: keywords from 1.2; for blows, `Forgives`
(`Crew.cs:115-124`) asks the medic system whether this is a patient's first unarmed blow under a damage cap.
Risks: an exploit to hit crew for free; one blow, unarmed, patient only, then the normal rules.

**1.7 Bystanders and friends (S-M).** Player sees: "Give me room." to someone standing on the patient during
CPR; "Put them down here." with a point at a bed for a friend carrying someone in; "Hold this." is out of scope.
Extends: the refusal reasons from 0.6; a proximity check in the CPR and shock branches. Risks: line spam in a
crowd; one line per event.

**1.8 Who he is talking to (S).** Player sees: the Boss is "Boss", staff are named, guests are "friend", and the
medic gives his own name when asked. Extends: `WGShipCommandSystem.GetAuthority` and `IsBoss`, as CrewBarks'
`Standing` does (`WGCrewBarkSystem.cs:222-229`), and `Identity.Name`.

**1.9 The dead (S).** Player sees: a time-of-death line to those present, the name used, where the body is
going, and for someone on file, "He's on file. I'll start the cloner." Extends: the morgue branch at
`Medic.cs:745-755` and the cloning line at `Medic.Cloning.cs:192`.

**1.10 A proper conversation window (L, optional).** Player sees: "Where does it hurt?" with answers, and the
consent questions as buttons. Extends: the Strangers dialogue (`wgStrangerDialogue` prototypes,
`WGStrangerDialogueEvent`, `WGStrangerChooseEvent`, the client window in `Content.Client/_WG/Strangers`), which
would have to move to a shared module first. Risks: a cross-module refactor and client work, with the sandbox
check; do 1.2 to 1.5 on speech and verbs first and see whether it is still wanted.

### Phase 2: Triage and teamwork

**2.1 A casualty board built from vitals (M).** Player sees: the medics go to the right person first and say so
as they pass the others: "You'll keep. Him first." Extends: replace the six-step `Priority` with categories read
from the vitals report. Immediate: arrest, airway, an undressable bleed, blood falling fast, brain tissue loss.
Urgent: unconscious, internal bleeding, sepsis damaging organs, toxic coma, heat stroke. Delayed: downed and
stable, fractures, infection. Minor: walking. Then the revivable dead by time since death, then the rest of the
dead. Within a category, by `WolfmedLifeSystem.GetBrainDeathSeconds` (`WolfmedLifeSystem.cs:1571`) and then
distance. The board is the existing `_claims` and `_care` (`Medic.cs:135-138`) kept per home grid. Risks: cost.
`BuildVitals` per candidate per medic per second is too much; build each candidate's entry once per second for the
grid and let every medic read it.

**2.2 Dropping a minor case for a dying one (S-M).** Player sees: "Hold that there. I'll be back." Extends:
between do-afters, a medic on a Delayed or Minor patient releases for an unclaimed Immediate; `Assign` already
steals claims (`Medic.cs:292-301`). Risks: flapping; a medic may be pre-empted once per patient.

**2.3 Stations for two and two (M).** Player sees: one paramedic goes out, the other stays at the ward door and
receives; one surgeon takes the pod, the other the table, recovery and the cloner. Extends: a station per medic,
chosen each time a call comes: the nearest free paramedic responds, the other holds unless there are two
Immediates out. Reservation by reading destinations (`Medic.cs:653-659`) widens to IV stands, defibrillators and
roller beds. Risks: with one of a pair dead the survivor must do both jobs, so stations are preferences.

**2.4 Two medics on one patient (M-L).** Player sees: one does compressions while the other fetches the
defibrillator or hangs blood; at the table the second surgeon answers an arrest while the first keeps working.
Extends: a claim becomes a case with a lead and an assistant, and an action is offered to the assistant only if it
complements the lead's (CPR with shock, treatment with IV, carry with clearing the way). Risks: the lead's
do-after breaks when the patient is moved, so assistants never carry; test ordering of two do-afters on one body.

**2.5 Handover (S-M).** Player sees: at the bed the paramedic reports and the surgeon answers: "Arterial bleed,
left leg, tourniquet on four minutes, two shocks." "Mine." Extends: `WGCrewPatientCare` becomes a record with a
timed list of what was done; the delivered line reads it. Risks: only when a surgeon is in earshot, otherwise the
line is wasted.

**2.6 Player medics are colleagues (M).** Player sees: an NPC who finds a player treating someone says "Your
patient. Call if you want hands.", leaves the case alone, and comes when asked to carry, fetch blood or do
compressions. A player holding the ship's medical job can say "take over". Extends: a player counts as holding a
case while they have a healing, CPR, defibrillator or surgery do-after on the patient or the patient in tow
(`Medic.Carry.cs:40-44` covers the last); `WGShipJobsComponent` identifies job holders; requests come through 1.2.
Needs 0.13. Risks: detecting "is treating" by do-after is approximate; time it out after 20 seconds of nothing.

**2.7 Calling it in (S-M).** Player sees: on the ship's channel, "Paramedic to Medical, one coming in, cardiac
arrest." and the surgeon walks to the pod. Extends: the crew radio path (`WGCrewCommsSystem`,
`WGNpcRadioSystem`, which caps routine traffic). Risks: channel noise; one call per case, none for Minor.

### Phase 3: Real tools

**3.1 Hands and a kit (M).** Player sees: the medic takes the item out, uses it, puts it away; the item is
gone or one use lighter. Extends: a kit partial that finds an item of a kind on the medic (belt, pockets, pack),
puts it in a hand, runs the do-after with a hand required, calls the item's own system, and stows it, the way the
weapon system draws, reloads from webbing and holsters. `Supplies` (`MedicComponent.cs:259-261`), `Inject`
(`Medic.cs:611-614`) and the working tree's spawn-a-pen-per-dose go. Ships that should never run dry get `infiniteSupplies` on the roster, as
`InfiniteAmmo` works (`Content.Server/_WG/ShipCommand/WGShipRosterComponent.cs:63`), which refills the used item in
place. Risks: hands are busy
during a carry, so the carry sets the patient down for urgent work; an NPC with full hands; items a player strips
from a living medic count as theft already (`Crew.cs:189-202`).

**3.2 The tools.**

| Finding | What the medic does | Entry | Size |
|---|---|---|---|
| Any living patient | scans with `HandheldHealthAnalyzer` | `BeginAnalyzingEntity`, `BuildVitals` | with 1.3 |
| Bleeding | real `Gauze`; a real `Tourniquet` item used up | hand path (0.10); `TourniquetSystem.Apply` | S |
| Arrest | real `Defibrillator` from belt or cabinet, cell charge used; CPR as players do it | `DefibrillatorSystem.CanZap`, `TryStartZap`; `CPRSystem` | M |
| Low blood (circulation, marrow, post-shock) | `Bloodpack` in the field; at a bed wheels an IV stand over, loads a pack, sets the rate, takes it down at target | `WolfmedFluidPackSystem.TryStart`; `WolfmedIvDripSystem.TryStartAttach`, `SetRate`, `Detach`; `GetTransfusionGuidance` | M |
| Unconscious, pain | pens (in flight): epinephrine; analgesic to get the downed walking; opiate only with consent and under the sedation line | `HypospraySystem.TryDoInject` | in flight |
| Opiate overdose | naloxone pen | same | S |
| Toxins | `AntiPoisonMedipen` or dylovene by syringe | same | S |
| Radiation | `RadAutoInjector`, then blood | same | S |
| Burns | ointment or mesh through the hand path so the burn is dressed; fluids by IV for burn fluid loss | 0.10; IV as above | S |
| Fracture | `WGWolfmedSplint` on a limb; osteogen pill; surgery through the pod | `WolfmedSplintSystem.CanApply`, `TryApply` | S-M |
| Dislocation | sets the joint | `WolfmedDislocationSystem.TryRelocate` | S |
| Open wounds | antiseptic spray after dressing | `WolfmedAntisepticSpraySystem.TrySprayOn` | S |
| Spreading infection, sepsis | spaceacillin by syringe, once, then recheck | hypospray or syringe path; `WolfmedInfectionSystem.GetSepsis` | S-M |
| Lodged round or shrapnel | hemostat, one object per do-after | `WolfmedEmbeddedRemovalSystem.FindEmbeddedWound`, `TryRemoveOne` | M |
| Not breathing, lungs gone | breath mask and tank on the patient; ventilator at the bed | `InternalsSystem`; `WolfmedVentilatorSystem.TryStartAttach` | M, needs a ventilator mapped |
| Too cold to shock, heat stroke | moves the patient out of the cold or to the cryo bay, says why | carry; no direct temperature write | M |
| Waiting for the pod | the worst waiting patient gets the stasis bed | `PickDestination` | S |

Risks across the table: each new action is another branch of `NextAction` and another way to stall, so 0.7 comes
first; dose safety (one opiate dose, one antibiotic dose, recheck from the report); the paramedic's limit becomes
"what he carries", not a damage number.

**3.3 Moving people properly (M).** Player sees: an unconscious patient is strapped to a roller bed and wheeled
when one is near; the dead go into a body bag before the morgue; the carry stays for emergencies and gets a
one-second lift. Extends: buckle and pull instead of `TryCrewCarry`; the six roller beds on the Lupercal. Risks:
crew pathing with a pulled bed through doors and past crates; keep the carry as the fallback it is now.

**3.4 Synthetics (M).** Player sees: "You need a welder, not a medic. Engineering's on the way." and the ship's
engineer comes with tools, or the medic brings them to the pod. Extends: route chassis patients to the engineer's
work list (`WGCrewWorkSystem.Engineer.cs`) or straight to the pod's weld programs; hydraulic fluid by
`WolfmedFluidPackSystem`. Risks: scope; the line alone is S and already better than silence.

**3.5 Crew look after themselves visibly (S).** Extends: `TreatSelf` (`Medic.SelfCare.cs:62-78`) runs the same
kit and do-after as 3.1, or the crewman walks to the ward.

### Phase 4: Patient flow

**4.1 Admission and a record (M).** Player sees: nothing new by itself; it is what makes the rest possible, and
admins can read it. Extends: a `WGCrewPatientComponent` on anyone admitted: stage (triage, ward, pre-op, surgery,
recovery, ready), bed, lead medic, consent, where their belongings are, and the timed list from 2.5. A
`WGCrewPatientStageEvent` broadcast feeds CrewBarks and the ledger (0.12). Being a new component, it can take
`StrappedEvent`-style reactions without touching `WoundHostComponent`. Risks: cleanup when the patient leaves the
grid, dies or the medics do.

**4.2 Beds with a purpose (M, some mapping).** Player sees: walk-ins use the ward, post-op patients the recovery
beds beside Surgery, the critical the stasis bed, and nobody alive is put by the morgue. Extends: a `use` field on
`WGMedicalBedComponent` (`Content.Shared/_WG/NpcCrew/WGMedicalBedComponent.cs`) set on the map, read by
`PickDestination`; a free pair `(WGMedicalBedComponent, StrappedEvent)` tells the system at once when someone lies
down, in place of waiting for the sweep. Risks: every ship without the field keeps today's nearest-bed rule;
`VesselGridPathTest` and the map lints after a Lupercal edit.

**4.3 Before surgery (M).** Player sees: the finding, the plan, the question, then clothes off into a locker or
tray that is named to them, a line in, and the walk or wheel to the pod or table. Extends: `TakeToPod` and
`TakeToTable` (`Medic.Surgery.cs:223-236`, `Medic.Table.cs:114-130`) gain the consent gate and the belongings
step, on top of the clothing fix in flight.

**4.4 In surgery (S-M).** Player sees: milestones spoken for whoever watches, not every step: "Opening the
chest.", "Heart's in.", "Closing." Extends: `PickProcedure` and `FinishStep` (`Medic.Table.cs:256-282`,
`:317-343`); the procedure's own name from its prototype. The pod already speaks for itself.

**4.5 Recovery (M).** Player sees: a word on waking, pain control, antibiotics after an open wound, and a check
every few minutes: "How's the leg? Another five minutes." Extends: `OperatedRest` (`Medic.Surgery.cs:33`) becomes
the recovery stage with a time the patient is told; rounds read the vitals report again. Risks: holding beds; the
round is also what discharges.

**4.6 Discharge (M).** Player sees: the medic comes, helps them up, points at their things, says what is still
wrong and what to do about it ("Splint stays on. Come back for the bone gel."), and hands over a slip of paper
with what was done. Extends: criteria from the report (conscious, no running route, no open bleed, sedation low,
no tourniquet); `TryUnbuckle`; a `Paper` written from the record; the ledger entry. Risks: discharge into a zone
the patient may not stand in is what 0.1's grace is for.

**4.7 Death in care (S).** Player sees: the surgeon stops, says so, covers the body and takes it to the morgue,
and starts the cloner for someone on file. Extends: 1.9 and the morgue branch.

### Phase 5: Supply

**5.1 Finite kits and a store (M).** Player sees: a medic who has used his gauze goes to the medical locker and
refills; on a ship that is cut off he runs out and says so. Extends: the engineer's pattern, a `WGCrewFuelStores`
crate he draws from by hand and that a persistent ship tops up every five minutes (NpcCrew README, engineer
section): a `WGCrewMedicalStores` locker, stocked by the roster on commanded ships and finite on encounter ships.
The Lupercal's fourteen stocked lockers and two vendors are there to draw from. Risks: locker access and locks;
a restock run must never outrank a patient.

**5.2 The pod's stock is real (M-L).** Player sees: the surgeon carries bottles from the store to the pod; a
missing limb is the patient's own if the paramedic brought it in ("Got the arm."), otherwise one printed at the
biofabricator, which takes time. Extends: `TopUp` and `StockTray` (`Medic.Surgery.cs:337-399`) take from stores;
the paramedic picks up severed parts at the scene while Wolfmed's five-minute clock runs
(`WolfmedNecrosisSystem.OnDetached`, `:372`); the two `MedicalBiofabricator` machines on the Lupercal. Risks: a
longer wait for a limb is a real cost to the player; keep `infiniteSupplies` ships instant.

**5.3 Waste (S).** Player sees: spent pens and wrappers go in a bin, not on the floor. Extends: when a case
ends the medic picks up what he dropped and walks it to the nearest disposal unit, with the break system's
`FindBin` and its `InteractUsing` on the unit. Risks: a pen a player has pocketed is gone, so he gives up on a
missing one at once.

**5.4 Running out (S).** Player sees: "I'm out of blood. Autodoc, now." Extends: `NextAction` falls back to the
next best item and the surgeon's queue; one line per shortage.

### Phase 6: Life in the medbay

Today both ranks are Guard duty: stand at the post, wait a random while (`htn.yml:164-182`, `:259-283`), say one
of four thoughts now and then. The working tree adds a break every 8 to 16 minutes (2.1). Nothing they do between
patients has anything to do with medicine.

**6.1 Things to do between patients (M-L).** Player sees: rounds of the occupied beds, restocking, wiping the
table, wheeling IV stands and roller beds back to their places, hanging the defibrillator back in its cabinet,
washing hands at the sink before and after surgery, sitting at the desk. Extends: a medic routine before the
Guard branch, on the pattern of `WGCrewRoutineSystem`'s patrol timer (`WGCrewRoutineSystem.cs:44-58`), with points
found from what stands near the post (sinks, lockers, chairs, beds) or from mapper markers. Risks: it only matters
with a player near, which is also the only time the brain is awake, so the cost is small; every task must yield to
a patient at once.

**6.2 Two medics talk (M).** Player sees: a line and an answer between the pair, about the last patient or
nothing much. Extends: CrewBarks gains a paired kind: one barker speaks, a named partner in range answers two
seconds later; more idle lines per voice. Risks: the area gap that keeps crew from talking over each other must
let the answer through.

**6.3 Watches for a pair (S).** Breaks themselves are in the working tree. Player sees: with two of each rank
there is always one in Medical. Extends: `WGCrewDowntimeSystem.Free` also asks the medic system whether his
opposite number is on a break or out on a call, and a medic's break stays within Medical's own machines and seats
when it has them; he puts down what he holds before he takes a patient. Risks: a lone medic never gets a break,
which is the right way round.

**6.4 Noticing people (S).** Player sees: "You're limping." to a passer-by, which leads into 1.1. Extends: the
CrewBarks greeting for medic voices reads the vitals report for the person greeted.

### Phase 7: Alert codes and mass casualties

The ship has situation codes Green, Yellow, Red and Black and general quarters
(`Resources/Prototypes/_WG/ShipPa/alert_codes.yml`, `Content.Server/_WG/ShipPa/ShipAlertSystem.cs`), broadcast as
they change (`Content.Shared/_WG/ShipPa/ShipPaEvents.cs:5-15`). The working tree's `WGCrewReadinessSystem` turns
them into Normal, Alert and Battle for the whole crew, sends everyone to their post at Battle and ends breaks (2.1).
What follows is only what those levels should mean in Medical. The crew's own boarding alert is `WGCrewAlertEvent`
and `WGCrewAlertClearedEvent`.

**7.1 What each level means in Medical (S-M).** Player sees: at Alert the IV stands go to the beds, the pod is
topped up and the paramedic has the defibrillator on him; at Battle the ward takes only Immediate and Urgent and
walk-ins are told to wait; the medics answer the alarm in their own words ("Surgery's ready. Send them in.")
and not at all in the middle of a procedure. Extends: the routine, the stations and the triage cut-off read
`WGCrewReadinessSystem.Readiness`; medic lines for the `stations`, `alert` and `stand-down` kinds in
`paramedic.ftl` and `surgeon.ftl`; the readiness bark skips a medic with a patient. Risks: admins who set codes
for atmosphere; the level changes behaviour only when there is a patient or a threat.

**7.2 Working under fire (M-L).** Player sees: during a boarding the paramedic does not kneel in a firing lane.
He drags the casualty round a corner first, treats there, and brings them in when the way is clear. Extends: the
out-of-sight spot finder the fall-back system already has (`WGCrewFallBackSystem`), and the muster's safe post
rule; needs 0.2. Risks: a patient left in the open because no cover is near; a time limit, then he goes anyway.

**7.3 Mass casualty (M).** Player sees: with more Immediates than medics, they stabilise and move on: stop the
bleed, shock, next; a corridor becomes a collection point; the surgeon comes out to help; someone calls it on the
radio and asks for hands. Extends: a mode on the board from 2.1 that caps work per patient per pass and suspends
the take-in until the Immediates are held.

**7.4 Abandon ship (M, decision).** Player sees: at Black the medics wake who they can, wheel or carry bed
patients to the docks, and leave the dead. Extends: the same carry with a dock as the destination. Risks: on the
Lupercal Black may never be used in earnest; skip unless wanted.

### 3.9 Order and size

Phase 0 first and whole: about two weeks of small pieces, each with a test, and three decisions (0.1, 0.11, 0.13).
Phase 1 without 1.10 is the largest visible gain for its size, about two weeks. Phases 2 and 3 are independent of
each other and of 4; 3.1 gates the rest of 3 and all of 5. Phase 4 needs 0.1 and 1.4. Phases 6 and 7 can wait and
lose nothing by waiting.

| Phase | Items | Rough size |
|---|---|---|
| 0 Repairs | 13 | 8 S, 2 S-M, 3 M |
| 1 Bedside manner | 10 | 3 S, 2 S-M, 4 M, 1 L (optional) |
| 2 Triage and teamwork | 7 | 3 S-M, 3 M, 1 M-L |
| 3 Real tools | 4 and the table | 1 S, 3 M; the table's rows are S or S-M each once 3.1 is in, M where marked |
| 4 Patient flow | 7 | 1 S, 1 S-M, 5 M |
| 5 Supply | 4 | 2 S, 1 M, 1 M-L |
| 6 Life in the medbay | 4 | 2 S, 1 M, 1 M-L |
| 7 Alerts | 4 | 1 S-M, 2 M, 1 M-L |

### 3.10 Tests to write first

The two fixtures have eleven tests between them and none covers speech, refusal, zones, fights or sleep. Before
Phase 0, pin the inferred defects, each as a test that fails today: a guest carried to a Staff-zone bed is not
challenged (defect 5); the medics keep their patient when a marine is struck (defect 6); a medic with a patient and
no Alive player aboard walks to them (defect 11); a bed patient in an unshockable arrest reaches the pod (defect
10); a ward treatment that fails three times stops (defect 13); a crewman put to bed gets up (section 2.1); a
staff-list player's incision on a downed crewman raises no alert (defect 16); a revived crewman whose post was
refilled does not go back on duty (defect 22). Each is one grid, one or two medics and one body, and fits the
existing fixture setup.

## 4. Open questions for the owner

1. Should a standing, lightly hurt player be approached, or only offered help when they ask? (1.1)
2. How much consent is wanted? The three tiers in 1.4, or a saved per-player preference alongside the existing
   consent toggles (`Content.Server/_WG/Prototypes/README.md`), such as "NPC medics may operate without asking"?
3. May a medic ever act against a conscious patient's "no"? The draft says only when they lose consciousness.
4. Patients in Staff and Locked zones: should NpcCrew exempt people in care (0.1), or should ShipCommand grant
   them a temporary standing the owner can see and switch off?
5. Do the medics fight at all? The draft says only whoever strikes them. Should a boarder who shoots the surgeon's
   patient count?
6. Should hostile NPCs and players who attacked the crew be treated once they are down? If yes, who guards them?
7. Stays medical free? Phase 4 gives a natural place to charge (discharge), and the price rules already exist.
8. Finite supplies on the Lupercal, or `infiniteSupplies` there and finite everywhere else?
9. A replacement limb: the patient's own when found, otherwise a stock one at once, or a printed one after a wait?
   And should it match the character's markings and skin?
10. May a paramedic board a docked player ship uninvited, and pry its doors, to reach a casualty? Should he bring
    them back to his own ward or treat them where they are?
11. Should the surgeon ever leave Medical? The draft lets him collect when no paramedic can.
12. A player holding the ship's Paramedic or Surgeon job: do the NPCs take orders from them ("take over",
    "bring blood")?
13. A revived crewman whose post was already refilled: leaves by the crew bay, or takes the post back and the
    newcomer leaves?
14. Is the Strangers dialogue window wanted for medics (1.10), or should it all be speech and verbs?
15. Mapping: are a ventilator, a named belongings locker by the tables, a stores locker and bed purposes on the
    Lupercal acceptable map changes, and is a second autodoc wanted for the second surgeon?
16. How chatty? The draft speaks on every new finding and treatment. A ship-wide setting next to CrewBarks'
    `LineGap` would let the owner tune it.
