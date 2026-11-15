# Narcotics, purity and neuroware design

Hard drugs for organic characters, their neuroware twins for characters with a non-organic brain, and a purity
grade that every reagent in the game can carry. This page is the brief for the agents who build it: decisions are
final unless marked **open**, numbers are starting values to tune, and each work package at the end can be built
and verified on its own.

Nothing here is built yet. File and line references were checked on 2026-11-11 against `main` at `25a6c50108`;
confirm each one before relying on it.

## Rules for implementing agents

- Read `AGENTS.md` first. It governs module folders, `WOLFGATE` markers, generated READMEs, code style, engine
  traps and verification, and it wins over this page where they disagree.
- One module, `WolfChem`, in three parts: `Purity`, `Narcotics` and `Neuroware`. Each part is a subfolder of the
  module folder in every area it touches. This page is `Docs/_WF/WolfChem/DESIGN.md` and the module README, with
  a hand-written overview of each part, is in `Content.Server/_WF/WolfChem`.
- Before building a package, grep for what it needs and report differences from this page instead of working
  around them silently.
- Every player-facing string goes through Fluent. Every new prototype ID takes the `WF` prefix.
- Every screen effect is off or static under the reduced-motion cvar, as `RainbowOverlay` is.
- Recipes use in-game reagents and are invented for the game. Don't model real synthesis routes.
- Each package ends with: the affected projects built, its tests run through `Tools/_WF/Testing/test.ps1 -Test
  <Fixture>`, a headless server log free of `[ERRO]`/`[FATL]` for prototype changes, a real client log free of
  `Sandbox violation` for Client or Shared changes, and `python Tools/_WF/Ci/modules.py --write`.

## What exists today

| Area | State |
|---|---|
| Reagents | About 20 narcotics. Desoxyephedrine is meth in all but name; Fentanyl (`_Mono`) has a sellable crystal; the Wolfmed and Onyx painkillers have a sedation meter and Naloxone |
| Visuals | Nearly every drug only applies `SeeingRainbows`. Lattice has 19 hallucination kinds (`WFDriftEffectsSystem.Show`) and Remade has a wobble shader (`WFRemadeDread`); no reagent triggers either |
| Addiction, tolerance, purity, mood | None. "Addictive" appears only in flavour text |
| Delivery | Smoking (mask slot only), pills, syringes, hyposprays, food, drink. No snorting, no bong |
| Economy | No drug carries a `Contraband` component; `Class3Chem` (`_Mono/contraband_severities.yml`) is unused; narcotic reagents have no `pricePerUnit`. Only `FentanylSolidified` has a price, tax and sold alert. The Strangers dealer sells joints |
| Synthetics | IPCs and converted synthetics flush every reagent except the `WFContaminant` group and can't be injected. The HardLight Synth species metabolises narcotics, against its guidebook page. The synthetic HUD has `tint`, `glitch` and `slice` shader parameters and a fault list. No data-stick item, no virus mechanic |

## Decisions

| Topic | Decision |
|---|---|
| Names | A mix. Real: cocaine, crack, opium, heroin, hash. Invented: Redline, Anvil, Chorus, Halo. Desoxyephedrine keeps its name; its solid form is "crystal" |
| Purity | Reagent-wide. Any reagent in any solution can carry a grade; a reagent with none is standard grade (100%) |
| Impure fraction | Toxic, and several times more so in a narcotic, so a weak batch is worse than a small dose of a good one |
| Overdose | Counted in effective units (units x purity), so pure product is the dangerous one |
| Dependence | Lasts the round and is lost with the body. Traits let a character start each round already dependent |
| Combat drugs | Strong, with downsides that scale with the benefit |
| Who drugs work on | The brain decides. A chemical dose registers only on an organic brain; neuroware loads only on a non-organic one. The body still takes impurity damage either way |
| Neuroware | Each drug has a neuroware twin that applies the same profile, so balancing one balances both |

## Architecture

```
reagent metabolised --(units x purity)--> WFDose effect --+
                                                          +--> WFIntoxicationComponent.Level[profile]
neuroware program tick --(rate x integrity)---------------+              |
                                                                          v
                                             WFIntoxicationSystem: tiers, effects, crash, dependence
                                                                          |
                         organic brain: drug shader    |    neuroware host: neuroware shader and HUD faults
```

One system applies every drug's effects from one number per profile. Reagents and neuroware are only two ways of
raising that number, which is what keeps the twins identical and keeps purity, overdose and dependence in one place.

## Purity (`WolfChem/Purity`)

### Storage

Purity is a side table on `Solution`, not `ReagentData`. `ReagentData` is part of a reagent's identity
(`ReagentId.Equals`), so two grades of one reagent would sit as separate stacks and every caller that looks a
reagent up by prototype with no data (`RemoveReagent(string, ...)`, `GetReagentQuantity`) would miss graded
entries. A side table changes nothing for code that ignores it.

- `Solution` is `sealed partial` (`Content.Shared/Chemistry/Components/Solution.cs:19`). Add
  `Content.Shared/_WF/WolfChem/Purity/Solution.Purity.cs`, a partial in the upstream namespace, holding
  `[DataField] Dictionary<string, float>? Purity` keyed by reagent prototype ID, and the helpers below.
- A missing key means 1.0. Remove a key when its value rounds to 1.0 or its reagent leaves the solution, so
  untouched solutions serialise and network exactly as today.
- Helpers: `GetPurity(prototype)`, `SetPurity(prototype, value)`, `BlendPurity(prototype, existingQuantity,
  addedQuantity, addedPurity)` (volume-weighted mean), `GetEffectiveQuantity(prototype)`.

### Hooks in `Solution.cs`

Each is a one-line marked call into the partial (`// WOLFGATE(WolfChem): ...`).

| Method | Line | Hook |
|---|---|---|
| `Solution(Solution)` copy constructor | 172 | Copy the table |
| `AddReagent(ReagentId, FixedPoint2, bool)` | 363 | Blend the added quantity in at 1.0, which pulls a graded reagent toward standard when standard stock is added |
| `AddSolution` | 880 | Blend each reagent with the other solution's grade |
| `SplitSolution`, `SplitSolutionWithout`, `SplitSolutionWithOnly` | 648, 586, 618 | The split-off solution takes the grades of what it received |
| `RemoveReagent`, `RemoveSolution`, `RemoveAllSolution`, `SetContents`, `ScaleSolution` | 485, 833, 575, 1030, 424 | Prune keys whose reagent is gone |

Add `AddReagent` overloads that take a purity in the partial, for sources that create graded reagent.
Check that `Solution`'s net serialisation carries the new field to clients; the ChemMaster needs it.

### Where a grade comes from

- **Reactions.** `ChemicalReactionSystem` removes reactants and adds products around
  `Content.Shared/Chemistry/Reaction/ChemicalReactionSystem.cs:123-184`. Before the removal, take the mean grade of
  the graded non-catalyst reactants weighted by amount consumed; standard-grade reactants don't raise it, so the
  product is as good as its precursor. A reaction with fillers is a cut and averages over every reactant. After the product is added, blend it in at
  `inputGrade x processFactor`.
- **Process factor.** A new prototype kind `wfReactionPurity`, keyed by reaction ID, so no upstream reaction
  prototype is edited: `optimalTemp`, `tolerance` (kelvin from optimal at which the factor reaches the floor) and
  `floor` (default 0.4). The factor falls linearly from 1.0 at the optimum to the floor. `drift` moves the optimum
  by up to that many kelvin either way, rolled per reaction at round start, so a cook finds it again with the
  tester each shift. A reaction with no entry
  has factor 1.0, so existing chemistry passes grades through unchanged.
- **Botany.** A component `WFProducePurity` on produce sets the grade of its solution from the plant's potency
  (`lerp(0.5, 1.0, potency / 100)`). Opt-in per plant; only the narcotic plants get it at first. **Open:**
  applying it to all produce would change every botany medicine and is a separate balance decision.
- **Fixed grades.** A component `WFSolutionPurity` (`solution`, `reagents: {id: grade}`) applied at map init, for
  vendor stock, dealer stock and loot.
- **Dispensers** give standard grade, by having no hook.

### What a grade does

- **Metabolism.** At `Content.Server/Body/Systems/MetabolizerSystem.cs:195`, multiply `scale` by the reagent's
  grade. Effects already honour `scale`, so medicine at 60% heals 60%.
- **Impure fraction.** In the same block, deal Poison of `metabolisedUnits x (1 - grade) x purity.impurity_damage`
  (cvar, 1.0 per unit), or `x purity.narcotic_impurity_damage` (6.0) for a reagent in the Narcotics group.
- **Thresholds.** `ReagentThreshold` (`Content.Server/EntityEffects/EffectConditions/ReagentThreshold.cs:37`)
  compares `GetTotalPrototypeQuantity`. Switch it to `GetEffectiveQuantity` under a marker. This shifts every
  existing overdose threshold for graded reagents only.
- **Price.** `PricingSystem` prices solutions at `Content.Server/Cargo/Systems/PricingSystem.cs:113-150`; multiply
  each reagent's value by its grade, so cutting stretches a batch without changing what it is worth.
- **Not scaled yet:** touch and ingestion reactions (`ReactiveSystem`), tile reactions, and reagents consumed as
  fuel. Leave them until someone needs them.

### Reading a grade

- ChemMaster buffer and container rows show a percentage beside any graded reagent.
- Health analyzer and reagent-scanner goggles show it where they already list reagents.
- A cheap handheld `WFPurityTester`: use it on a container or item to get a popup with each graded reagent's
  percentage. This is the street-level tool; sell it at the Strangers dealer.
- Standard-grade reagents show nothing, so existing UI looks the same until a graded reagent appears.

### Tests (`Content.IntegrationTests/Tests/_WF/WolfChem/Purity`, or `Content.Tests/_WF/WolfChem/Purity` for the pure `Solution` ones)

- Blend: 10 u at 0.6 plus 10 u at 1.0 gives 20 u at 0.8; split keeps the grade; removing the reagent removes the key.
- A solution that never saw a grade has a null or empty table after add, split and transfer.
- Reaction: off-optimum temperature lowers the product grade; a reaction with no `wfReactionPurity` passes the
  input grade through.
- Metabolism: a 50% heal reagent heals half and deals impurity Poison; the overdose threshold trips on effective
  units.
- Round-trip a graded solution through map serialisation.

## Intoxication (`WolfChem/Narcotics`)

### Profiles

Prototype kind `wfDrugProfile`:

| Field | Meaning |
|---|---|
| `family` | `Stimulant`, `Opioid`, `Psychedelic`, `Empathogen`, `Cannabinoid`, `Combat`, `Nicotine`. Dependence and overlays key on it |
| `decay` | Level lost per second |
| `tiers` | Ordered list of `{ min, effects, speed, statusEffects, overlay }`. The highest tier whose `min` is at or below the level applies. The last tier is the overdose |
| `crash` | `{ after, duration, effects, speed, overlay, stamina, staminaCap, collapse }`: applied when the level falls below the first tier after having spent `after` seconds above it. `stamina` drains per second but stops at `staminaCap` of the crit threshold (0.45), since every hit resets regen and an uncapped drain stun-locks; `collapse` is one knockdown as it starts. A crash's `Drowsiness` nods 1-2 s every 1-2 minutes |
| `dependenceGain` | Dependence added per unit of level gained |
| `rushGain`, `rushHalfLife` | The peak of a fresh dose: extra level per level added to the blood, halving every `rushHalfLife` seconds. It never reaches the overdose tier |
| `neuroware` | The twin: program name, HUD fault lines, banner, and `amount`, the level a full load delivers (8 unless set) |

`effects` are ordinary `EntityEffect`s run with base args, so they must not use `ReagentThreshold` or `OrganType`
(both throw outside a reagent context). Tiers replace thresholds.

### Components and systems

- `WFIntoxicationComponent` (Shared, networked to the owner): `Levels` per profile, time above the first tier,
  active crashes. Added on first dose, removed when empty.
- `WFIntoxicationSystem` (Server): ticks once a second, decays levels, picks tiers, runs effects, refreshes the
  movement-speed modifier, starts and ends crashes, feeds dependence. Its tick is a public method so tests can call
  it; integration tests sit in the lobby and round-gated updates never run.
- Entity effects: `WFDose` (`profile`, `perUnit`; adds `metabolised units x grade x perUnit`; does nothing when the
  brain is a neuroware host) and `WFClearIntoxication` (`family`, `amount`) for antagonists. Naloxone clears
  `Opioid`; Haloperidol clears `Stimulant` and `Psychedelic`; Dylovene clears `Combat`; Psicodine clears `Empathogen`
  and `Cannabinoid`. Nicotine has no overdose and no antagonist.
- Brain check: one shared helper, `IsNeurowareHost(body)`, true when the body's brain organ has
  `WFNeurowareHostComponent`. It lives in `Narcotics` so `Neuroware` depends on `Narcotics` and not the reverse.
- The human heart metabolises at most two Narcotic reagents at a time (`maxReagents: 2`). Leave that as the
  natural limit on stacking.

### Dependence

- `WFDependenceComponent`: a 0-1 meter per family, the last dose time per family, and tolerance per family.
- Gain comes from `dependenceGain`. The meter decays slowly while clean (0.02 per minute, a quarter of that above
  the withdrawal mark), so a light habit fades within the round and a real one doesn't.
- Tolerance rises with use (half the dependence gained) and divides the blood level by `1 + tolerance` (up to x2),
  and speeds a fed level's `decay` by as much. Every tier threshold, the overdose included, effectively rises with
  it, as does heroin's sedation (`WFSedation`), so the pull is toward dosing more.
- Withdrawal starts when a family's meter is above 0.5 and no profile of that family has been above its first
  tier for 5 minutes. Stages by meter: popups and cravings; then jitter, a stamina cap and torso pain held at 40
  (75 at the top stage; Downed is 128, and painkillers relieve it); `Opioid` and `Stimulant` add vomiting and a
  1-2 s nod every 1-2 minutes at the top stage, and `Nicotine` and `Cannabinoid` stop at cravings. Only reaching the
  first tier again ends it, so a taste doesn't, and tolerance raises what it takes. A dependent body that is high
  on its family sheds 1 stamina damage a second. Withdrawal never kills.
- Round scope: the components live on the body. Cloning or a new body starts clean. Nothing is written to the
  database.
- Traits `WFDependentStimulant`, `WFDependentOpioid`, `WFDependentNicotine`: start the round at 0.8 in that family
  and stop the meter decaying below 0.55 (cravings only, once the starting 0.8 has eased), and give a first dose as trait gear. Follow the cost and category conventions in
  `Resources/Prototypes/_Mono/Traits/physical.yml`; they are drawbacks and should refund points.

### Visuals (Client)

One overlay, `WFIntoxicationOverlay`, with one shader that takes a family and an intensity from the active tier.
Start from existing shaders rather than writing each look from nothing.

| Family | Look | Starting point |
|---|---|---|
| Stimulant | Brighter, sharper and more vivid; heavy adds glare round lights and a faint brightness pulse on a heartbeat that speeds up with the tier; overdose blows out the highlights, blurs the edges into tunnel focus and trembles by a pixel. Nothing darkens | new |
| Opioid | Warm tint, soft blur, heavy vignette, slow breathing zoom | `Drowsiness` radial blur, `Vignette` |
| Psychedelic | Colour drift and trails, plus Lattice hallucinations from the server through `WFDriftEffectsSystem.Show` (`Content.Server/_WF/Lattice/WFDriftEffectsSystem.cs:128`) | `Rainbow` |
| Empathogen | Bloom and a soft glow around other people | new |
| Combat | Tunnel vision, red desaturation outside the centre | `GradientCircleMask` |
| Crash | Desaturation toward grey, slight darkening | `Greyscale` |

A body whose brain is a neuroware host gets none of these; see Neuroware.

### Nodding off

A heavy opioid high nods every 30 to 90 s for 1.5 to 3 s, an overdose every 15 to 30 s for 2.5 to 4 s
(`WFNodSystem`). The body stays standing and conscious and can still act, but moves at x0.2. The networked
`NodStart`/`NodEnd` on the intoxication component drive the client: `WFNodOverlay` closes soft-edged lids over the
view in 0.6 s and opens them at the end, and `WFDrugAudioSystem` occludes world sounds by up to 7. A third of nods
show to others as an emote, and waking pops a line to the player. Nobody asleep or dead nods. A neuroware host on an
opioid program nods too, as a stall in standby: its feed dims to about half instead of closing, with its own emote
and wake lines. The overdose keeps its real forced
sleep on top.

### Chat and speech

A neuroware host gets all of this, the sound below and Chorus's hallucinations from its programs, as an organic
does from the drug.

- **Reading** (`WFDrugChatSystem`, client). Incoming speech, radio and emotes are rewritten on the high player's
  screen only, from the same strongest high the overlay draws (`WFIntoxicationSystem.DominantHigh`): psychedelics
  colour each word along a drifting hue and, heavy or overdosed, shuffle the middle letters of 6% or 15% of words;
  opioids grey the text, empathogens tint it pink and cannabinoids green, stronger with the look. The change is
  made as the message arrives, so lines read while high stay that way in the log. Hooked from
  `ChatUIController.ProcessChatMessage` before codewords and highlights, which would split the message text.
- **Speaking** (`WFDrugSpeechSystem`, server, an accent on `WFIntoxicationComponent`). Light highs only change
  the voice; from heavy on, each family changes the words, and families stack:
  - Stimulant: doubles 25% of words, stutters 15% on the first letter ("g-g-go") and turns full stops into
    exclamations. Overdosed: 45% doubled, 40% shouted, 20% stuttered and commas dropped so it runs on.
  - Opioid: a drunk-style slur (o→u, s→sh, a→ah, u→oo, c→k, dragged letters), a pause after 25% of words, and
    the end trails off. Overdosed: twice the slur, pauses after 40%, and 12% of mid-sentence words drop out.
  - Cannabinoid: a filler ("uh", "like", ...) before 10% of words and a 40% chance the end drifts off ("...,
    man..."). Overdosed: 20% fillers, and a 35% chance the sentence stops halfway ("so the thing is... yeah.").
  - Empathogen: draws out the last vowel of 15% of words ("sooo") and softens full stops into "~". Overdosed: 30%
    stretched and a 40% chance of a gushing line on the end.
  - Psychedelic: at heavy, swaps 15% of neighbouring word pairs; Chorus's top tier scrambles every word through
    its `ScrambledAccent` status instead. Its `Mimic` hallucination already puts lines nobody said into local chat.
  - Combat (Redline, Anvil) leaves speech alone. Filler, tail and gush lines are in `speech.ftl`.
- **Voice** (`WFDrugSpeechSystem` on the Voice module's `SpeechVoiceModifyEvent`). The character voice changes per
  line, for everyone who hears it, by each family's phase; families stack and the result stays in the creator's
  ranges. Syllable time is a multiplier, so lower is faster:

  | Family | Light | Heavy | Overdose |
  |---|---|---|---|
  | Stimulant | syllables ×0.85 | ×0.7, pitch ×1.04 | ×0.55, pitch ×1.08, pitch variance +0.1 |
  | Opioid | syllables ×1.15 | ×1.4, pitch ×0.94, intonation ×0.5 | ×1.7, pitch ×0.9, flat |
  | Cannabinoid | — | syllables ×1.2, intonation ×0.7 | ×1.35, intonation ×0.5 |
  | Psychedelic | — | pitch variance +0.15, vibrato 0.12 | +0.3, vibrato 0.25 |
  | Empathogen | — | pitch ×1.03, intonation ×1.6 | pitch ×1.05, intonation ×2, tremolo 0.3 |

### Sound (Client)

`WFDrugAudioSystem` changes world sounds (not global ones: music, interface, announcements) for the local player's
strongest high, every frame, so it wins over area echo and over network states that reset the pitch:

| Family | Reverb (engine preset) | Pitch |
|---|---|---|
| Psychedelic | `Psychotic` | 1 |
| Opioid | `Underwater` | 0.9 |
| Cannabinoid | `Dizzy` | 0.96 |
| Empathogen | `Drugged` | 1 |
| Stimulant | none | 1.06 |

A light high only gets half the pitch shift, a heavy one the table, an overdose double the shift. When the high
ends, every changed sound gets its own pitch back and loses the reverb; area echo puts its own back on its next pass.

### Examine cues

`WFDrugCuesSystem` shows a high to whoever examines the body, as symptoms and never as a drug's name, so a medic can
tell pinpoint pupils (naloxone) from blown ones (haloperidol) without an analyzer. There are two lines, each the
strongest thing going on:

| Line | Seen when | From |
|---|---|---|
| Face (pupils, skin) | Within details range, with nothing covering the eyes (`IdentityBlocker` coverage) | A light high, drink, or withdrawal's last stage |
| Body (how they move) | At any examine range, through any clothing; not on a body that is asleep or in crit | A heavy high or drink; a crash, up close; withdrawal from its second stage |

An overdose has its own body line. A neuroware host shows flickering optics and stuttering servos instead.

### Flying

`WFDrugPilotingSystem` handles `GetShuttleInputsEvent` on the pilot, after the pilot's own handler. Light, heavy and
overdose:

| State | Effect |
|---|---|
| Stimulant | Turn x1.05, x1.1, x1.1 and thrust x1.03, x1.05, x1.05; an overdose jerks the turn input for 0.2 s every 2 to 5 s |
| Stimulant crash | Turn x0.88, thrust x0.92: more than the high gave |
| Opioid | The ship follows the stick with a lag of 0.25, 0.6, 1.2 s, and keeps going after it is let go; through a nod the stick stays where it was |
| Cannabinoid | Brake input x0.85, x0.65, x0.5 and turn x0.95, x0.85, x0.75 |
| Alcohol (40, 120, 240 s left on the drunk timer) | While flying, the turn input sways by up to 0.1, 0.25, 0.45 and the thrust is skewed by as many radians |

With several pilots the game averages their inputs and multipliers. NPC pilots are untouched.

### Exotic sense

A tier with the `ExoticSense` trait, which every Chorus tier has, senses exotic matter. Every 2 s
`WFExoticSenseSystem` finds the nearest asteroid rock on the map within 500, 1,000 or 1,500 m (by phase; rocks only load about that far from a ship) that would drop at
least one shard when broken (`mass x band.exotic`, so only in the Coda) and networks its position to the owner, whose
client draws a violet light over it or at the edge of the view towards it, brighter for a richer rock. The price is
Drift: a carrier with a sensing mind aboard gains it x1.5 (`WFDriftGainEvent`, raised by the Lattice on everyone
aboard).

## Substances

`perUnit`, tiers and durations are starting values. Level is in effective units.

| Substance | Name | Family | Role | High | Downside and overdose | Route |
|---|---|---|---|---|---|---|
| `WFCocaine` | Cocaine | Stimulant | Social | Speed x1.1, faster stamina recovery, light pain suppression, talkative popups. Short | Jitter and anxiety at the high tier; a crash of drowsiness and slowed stamina. Overdose: heart strain (`WFStrainHeart`), Asphyxiation | Snorted |
| `WFCrack` | Crack | Stimulant | Cheap, fast | Cocaine's high, stronger and a third as long | Triple `dependenceGain`; harsher crash | Smoked |
| `WFOpium` | Opium | Opioid | Social downer | Moderate pain relief, calm | Drowsiness | Smoked |
| `WFHeroin` | Heroin | Opioid | Hard downer | Strong pain relief through `WolfmedPainRelief`, euphoria | Slow movement; nodding off (eyes shut, still standing) when heavy, and brief forced sleep in an overdose. Overdose is Wolfmed's sedation airloss through `WFSedation`, graded and blunted by tolerance. Naloxone reverses it | Injected, smoked |
| Desoxyephedrine (existing) | Crystal | Stimulant | Combat stimulant | Speed x1.25 from the profile; halved stuns and knockdowns, stamina-crit resistance and no slowdown from damage need a heavy dose (level 8), as profile traits, so tolerance blunts them and Burst has them; the reagent keeps its jitter and pain relief | Unchanged Poison; adds a long crash | Smoked as crystal, or as today |
| `WFRedline` | Redline | Combat | Offence | Speed x1.3, stun and knockdown resistance, no slowdown from damage, pain immunity | Heart damage every tick; can't sprint-stop cleanly (jitter); when it ends, a 10 s knockdown, then 60 s or more at speed x0.5 with stamina held at 80%. Overdose: cardiac arrest | Injected |
| `WFAnvil` | Anvil | Combat | Endurance | Incoming Brute and Burn reduced 30%, pain immunity, stamina-crit resistance | Speed x0.85; the user's own damage readout is hidden while high; damage avoided is repaid at 40% as Poison over the crash. Overdose: organ damage | Injected |
| `WFChorus` | Chorus | Psychedelic | Social, prospecting | Lattice hallucinations scaling with tier: whispers and blips, then glimpses and phantoms, then mimics and doubles. A light on the screen points to the nearest rock holding exotic matter, at the cost of faster Drift | Disorientation, scrambled speech at the top tier. No physical overdose; the top tier adds brief `Pacified` | Swallowed tab |
| `WFHalo` | Halo | Empathogen | Social | Glow overlay, light pain relief, warm popups, laugh and hug emotes | Rising body temperature while high; a crash of sadness popups and low stamina. Overdose: overheating | Pill |
| `WFHash` | Hash | Cannabinoid | Social | Solid concentrate that delivers THC at a higher dose | THC's | Bong, pipe |
| Nicotine (existing) | Nicotine | Nicotine | Habit | Gains a small effect: light stamina recovery | Dependence only | Smoked |

Combat-drug rule: a tier that gives more must cost more in the same tier, and the cost must be something the user
can't dose their way out of. Redline's collapse and Anvil's repayment both start when the level drops, so
redosing only postpones and enlarges them. A light dose gives part of the benefit (Redline x1.15 with no stun
resistance, Anvil without stamina-crit resistance), stuns are halved rather than removed, and each combat drug past
the first strains the heart by 0.1 a second.

### Production

- **Coca** (`WFCoca` seed, plant, leaf): leaves hold `WFCocaAlkaloid`. Grind the leaves, then react the alkaloid
  into `WFCocaine` with a `wfReactionPurity` entry. A second reaction turns Cocaine into crack rocks (a solid
  entity, as Fentanyl solidifies at `Resources/Prototypes/_Mono/Recipes/Reactions/fentanyl.yml`).
- **Poppy**: check whether upstream's poppy seed exists here and reuse it; otherwise add `WFPoppy`. The pod holds
  `WFOpium`; chilled below 270 K with water as a catalyst, 5 u sets into a `WFOpiumResin` lump at the batch's grade. Opium reacts into `WFHeroin`.
- **Crystal**: Desoxyephedrine chilled below 240 K with acetone as a catalyst sets into `WFCrystalShard` shards.
- **Redline, Anvil, Halo, Chorus**: chemistry-only, each a two-step route with a narrow optimum so that grade
  matters. Chorus needs a Lattice-flavoured input (exotic matter or a deep-band material; check
  `Docs/_WF/Lattice/DESIGN.md` for what exists) so it is rare and comes from the frontier.
- **Hash**: press dried cannabis in a crafting graph.
- **Cutting**: flour reacts with cocaine or heroin a unit for a unit into twice the powder at half the grade
  (`fillers` on `wfReactionPurity` count as grade zero).
- Seeds come from the exotic seeds crate and dealer stock, not the public seed vendor.
- Solid products (rocks, shards, bricks, baggies of powder) keep their solution's grade and carry
  `Contraband` with `Class3Chem`.

### Delivery

- **Snorting.** Powder comes in a baggie item. Using a baggie on a table or other flat surface places a
  `WFPowderLine` entity holding a small solution. Activating a line starts a 1.5 s do-after and moves its solution
  into the user's bloodstream chemicals. Blocked by a worn mask or helmet that covers the face.
- **Bagging solids.** A baggie is also a four-slot storage for solid drugs (`WFBaggable` tag). Clicking it with a
  solid, or clicking a loose solid with it, puts the solid in with a popup. Using the baggie in hand takes the last
  one out into a free hand, using it on a table with no powder inside sets one down, and examining it up close
  lists them. The storage window is off the use key but stays on the verb.
- **Bong.** A held item (`WFBong`) with a bowl slot that accepts the `Smokable` tag and solid drugs, and a water
  reservoir. Using it with a lit bowl takes one hit: a 2 s do-after that moves a fixed amount to the user's
  bloodstream and emits a small smoke puff. It works from the hand, unlike pipes, which only work in the mask
  slot (`Content.Server/Nutrition/EntitySystems/SmokingSystem.cs:118-175`). The bowl also takes anything with
  the `WFBaggable` tag, which adds dried cannabis, tobacco and shrooms; a stack loads one piece and hands back the
  rest. The sprite shows the water level, a loaded bowl, and an animated ember and smoke while lit
  (`WFBongVisuals`).
- **Smokable solids.** Crack rocks, crystal shards, opium and hash fit pipe and bong bowls.
- **Second-hand smoke** (`WFSecondhandSmokeSystem`). Lit smokables and bong hits release `Gas.Smoke` (0.3 mol per
  unit smoked) and move 15% of what was smoked into a reservoir at the source. Every 2 s, anyone breathing without
  internals within 8 tiles inhales `0.05 u × smoke moles on their tile` per second from the reservoirs, split by each
  reservoir's size over `1 + distance`. Atmos gives the room behaviour: smoke builds up in a closed room, thins in a
  big one and is scrubbed or vented. Reservoirs halve every 90 s, and nobody can breathe more than was left, so fire
  smoke carries nothing. Smoke is harmless below 0.25 u in the lungs (about 1 mol on a tile), so a haze doesn't
  choke but a fire or a packed hotbox does.
- **Tabs and pills** use the existing pill system. Injectables use existing syringes.

## Neuroware (`WolfChem/Neuroware`)

### Hosts

- `WFNeurowareHostComponent` goes on non-organic brain organs: `WFWolfmedOrganPositronicBrain`
  (`Resources/Prototypes/_WF/Wolfmed/Body/organs.yml:161`) and `OrganSynthBrain`
  (`Resources/Prototypes/_HL/Body/Organs/synth.yml:21`, a marked edit).
- This makes the HardLight Synth species ignore chemical doses, which brings it in line with its guidebook page.
- An organic body with a positronic brain is a host. An organic brain in an MMI is not.
- Borgs are out of scope: they have no body or organs, and laws and ion storms already cover them.

### Items

- `WFNeurowareStick`: `profile`, `integrity` (0-1), `loads` remaining, `amount` (level added per load; unset takes
  the profile's `neuroware.amount`), `rate` (level per second while loading).
- Using a stick on a host starts a do-after: 2 s on yourself, 4 s on someone else, with a popup to the target, as
  `WolfmedFluidPackSystem` does (`Content.Server/_WF/Wolfmed/Medical/WolfmedFluidPackSystem.cs`). A non-host gets
  a "no port" popup.
- Loading adds a running program to `WFNeurowareComponent` on the body. Each second it adds
  `rate x integrity` to the profile's level until `amount` is delivered, and the same to its rush
  (`WFIntoxicationSystem.AddRush`), so a load peaks the way a dose in the blood does. Everything after that is
  `WFIntoxicationSystem`.
- **Load size.** A full load is 8, as much as a heavy line or two; 10 for the drugs that come in a 10 u vial and for
  Crystal, and 14 for Idle, which have no rush to carry a load into the heavy tier.
- **Copying.** Using a stick on a blank stick copies it at `integrity x 0.85` with one load, and the blank shows a strip of tape.
  Each copy spends one of the source's loads and a tenth of its integrity, so a stick yields at most as many sticks
  as it has loads, run or copied, and a one-load copy is either run or copied once. Pristine builds come from
  dealers, the uplink and rare loot, so a street copy is a few generations down.
- **Encoding.** Using a blank on anything that holds a drug with a twin, other than a body, starts a 3 s do-after
  that reads the drug in and uses it up: integrity is the drug's grade, the level taken is `units x perUnit` up to
  three full loads, split into the fewest loads that are no larger than a full one. A solid goes with its drug and a
  vessel is left with the rest. This is the synthetic's supply line: whoever grows or cooks for organics can supply
  a synthetic at the same grade.
- **Sources.** Blanks print at the autolathe for a sheet of steel and one of plastic and are priced under that. The
  Reaver dealer sells blanks and pristine builds, the planet dealer sells a street copy of the buyer's choice, and
  `WFNeurowareStickLoot` (mostly blanks, some copies, rarely a pristine build) rolls in maintenance fluff, both the
  upstream table and the Mono locker one, and uncommon salvage treasure.
- Sprites: `Resources/Textures/_WF/WolfChem/Neuroware/Objects/stick.rsi`, a USB stick with a tinted body, a printed label for
  pristine builds and tape for copies.

### Integrity

Integrity is neuroware's purity and enters the same way, as a multiplier on the dose. The missing fraction causes
glitches instead of Poison: each second a running program has a `(1 - integrity) x 0.15` chance of one of a brief
stun, a forced Beep or Buzz emote, ten seconds of `ScrambledAccent`, or a false HUD fault.

### Presentation

`WFIntoxicationOverlay` draws a host's strongest high with its own shader, `WFNeuroware`
(`Resources/Textures/_WF/WolfChem/Neuroware/Shaders/neuroware.swsl`), over the same ghost trails: the program corrupts the
optics feed instead of the senses. Stimulants trace edges in cyan under a racing refresh line, opioids settle into the organic
high's muted amber and drop resolution and colour depth into standby, psychedelics map brightness onto a cycling false-colour palette and smear blocks,
empathogens bloom warm into a thermal view, cannabinoids fade to green phosphor with a ghost image, and combat
programs leave a red targeting circle with traced edges outside it. Every program lays scanlines; an overdose slips
rows of blocks and flickers dead pixels; a crash is a weak grey signal. The world bends and the phase kick zooms as
they do for an organic high, and the stimulant feed lifts the shadows rather than crushing them. Its profile state also shows through the
synthetic HUD (`Content.Server/_WF/Wolfmed/Hud/WolfmedSyntheticHudSystem.cs`, client in
`Content.Client/_WF/Wolfmed/Overlays/`):

- The HUD system raises a collect event during `Refresh` so `Neuroware` can add fault lines from the profile's
  `neuroware` strings ("UNSIGNED PROCESS: whiteline.d", "THERMAL GOVERNOR OVERRIDDEN").
- The screen shader's `glitch` and `slice` are driven from the active tier (a strong program tears the screen), and
  the HUD shows even on an undamaged chassis while a program runs. The red `tint` rim is left to damage; the
  program's own shader carries its colour.
- Withdrawal shows as recurring faults ("HABIT LOOP: process not found") with the same gameplay penalties.
- Synthetics without the Wolfmed HUD still get popups and speech effects.

### Twins

| Drug | Program |
|---|---|
| Cocaine, crack | Whiteline |
| Opium, heroin | Null Loop |
| Crystal | Burst |
| Redline | Redline (same name, sold as firmware) |
| Anvil | Damper |
| Chorus | Choir.sig |
| Halo | Halo |
| Hash, THC | Idle |

### Prerequisites and cure

- IPCs and converted synthetics don't allow the `Drunk`, `Drowsiness`, `ForcedSleep`, `StaminaModifier` or
  `Adrenaline` status effects (`Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml:310-327`,
  `Resources/Prototypes/_WF/Biotype/biotypes.yml:184-197`). Add the ones the profiles use.
- The chassis reboot (`WFChassisPanelSystem.Controls.cs`, `FinishReboot`) clears every intoxication level and
  running program. It doesn't touch dependence. It already costs charge and a few seconds of paralysis.
- Redline's speed on a synthetic also raises battery drain and core temperature for the duration, in place of
  heart damage.

## Economy and law

- Every product carries `Contraband` with `Class3Chem`, except hash: it is legal like the cannabis it is pressed
  from, and THC has no price, so nothing holding it is marked either.
- Solid products get a `StaticPrice` scaled by grade, and the `ItemTax` and `SoldAlert` setup that
  `FentanylSolidified` uses (`Resources/Prototypes/_Mono/Entities/Objects/Misc/solids.yml`).
- The contraband is the reagent, not the container: any item or anchorable structure holding a priced narcotic in a
  solution (beaker, jug, syringe, pill, baggie, vial, storage tank) gains the same tag, contraband, tax and sold alert (`WFNarcoticGoodsSystem`,
  `WFNarcoticMarks`) and loses them when emptied. A blank stick that takes a copy gains them for good, unless the
  program's profile is `legal` (cannabis, whose twin is Idle).
  Narcotics sold on a cargo pallet fund the Reaver campaign by value (`fundingGoodsValue`).
- The Strangers dealer (`Resources/Prototypes/_WF/Strangers/dialogues.yml:7`) sells low-grade product, seeds,
  the purity tester and neuroware copies. The Reaver runner (`WFEncounterReaverDealer`, a trader ship flying
  Ashfall colours) sells full grades, seeds, kit and pristine sticks, and buys nothing; the Underground market tab buys and sells them
  too. The bong is crafted from two glass and one plastic.
- Bench laws (`Resources/Prototypes/_WF/Bench/laws.yml`): add narcotics possession and trafficking, and unlawful
  neuroware loading onto an unwilling host.
- A guidebook page under `Resources/ServerInfo/_WF/WolfChem/Narcotics` covers grades, dependence and neuroware from the
  player's side, and the Synths page (`Resources/ServerInfo/_WF/Synthetics/SynthsAndYou.xml`) gains a line on
  neuroware.

## Work packages

Build in order; packages on the same line of the dependency column can run in parallel.

| # | Package | Module | Depends on | Scope | Done when |
|---|---|---|---|---|---|
| 1 | Purity core | Purity | none | `Solution` partial and hooks, metabolism scale and impurity damage, effective-unit thresholds, pricing, `wfReactionPurity`, `WFSolutionPurity`, `WFProducePurity` | The Purity tests pass; a server run shows no behaviour change for ungraded reagents |
| 2 | Purity UI | Purity | 1 | ChemMaster, analyzer and goggles percentages; `WFPurityTester` | A graded reagent shows its percentage in each; ungraded UI is unchanged |
| 3 | Intoxication core | Narcotics | 1 | `wfDrugProfile`, component, system, `WFDose`, `WFClearIntoxication`, brain check, one test profile | Tests cover tiers, decay, crash and the brain check |
| 4 | Dependence | Narcotics | 3 | Component, withdrawal stages, tolerance, the three traits | Tests cover gain, decay, withdrawal onset and relief, trait start state |
| 5 | Overlays | Narcotics | 3 | `WFIntoxicationOverlay` and shader, six looks, Lattice hook for `Psychedelic` | Each family renders in a client; reduced motion disables motion; no sandbox violation |
| 6 | Substances | Narcotics | 3 | Reagents, profiles, plants, reactions, solid products, sprites | YAML linter clean; each drug reachable from raw inputs in a test |
| 7 | Delivery | Narcotics | 6 | Powder lines, bong, smokable solids | Tests: a line and a bong hit each move reagent into the bloodstream |
| 8 | Existing reagents | Narcotics | 4 | `WFDose` on Desoxyephedrine, Ephedrine, Fentanyl, the Wolfmed and Onyx opioids, Nicotine, THC; antagonist effects; fix `CigaretteTHC` (holds RobustHarvest) | Marked edits only; linter clean |
| 9 | Neuroware | Neuroware | 3, 4 | Host marker, stick, loading, copying, integrity glitches, HUD faults and shader drive, reboot clear, status-effect allow-lists | Tests: load on host and refusal on non-host, copy integrity, reboot clears; client check of HUD |
| 10 | Economy and law | Narcotics | 6, 9 | Contraband, prices, dealer and market stock, laws, guidebook | Linter clean; dealer sells; sold alert fires |

Packages 1 and 9 touch code outside `_WF` the most; keep each upstream edit to a single marked call.

## Open questions

- Whether `WFProducePurity` should apply to all botany produce.
- Whether touch and ingestion reactions should scale with grade.
- Whether borgs should ever run neuroware.
- Final numbers for every tier, to be set in playtests.
