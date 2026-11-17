# WolfChem

Wolfgate chemistry in three parts, each in its own subfolder: `Narcotics` (street drugs, intoxication and
dependence), `Purity` (a grade any reagent can carry) and `Neuroware` (drug programs for synthetics). The design brief
for all three is [Docs/_WG/WolfChem/DESIGN.md](../../../Docs/_WG/WolfChem/DESIGN.md).

## Narcotics

Street drugs and what they do to a body. Each drug doses a `wgDrugProfile` (tiers with speed, traits, popups and an
overlay, grouped into light, heavy and overdose phases; a crash; neuroware strings). A street drug's level follows the
units left in the blood (`WGDose`), so its metabolism rate sets the length of the high, the analyzer
shows it and purging ends it (rates finer than 0.01 u a tick carry over on the organ, `MetabolizerSystem.Granular.cs`, so a slow organ still clears it); the excess above an overdose flushes at `overdoseHalfLife`. Upstream reagents that dose a
profile (meth, ephedrine, THC, nicotine, the medical opioids) do the same, with their rates and per-tick effects
retuned; only neuroware feeds the level directly, and that part decays by `decay`,
set to the drug's metabolism rate so a twin lasts as long. A fresh dose in the blood also gives a rush (`rushGain`),
a short peak that can reach the heavy tier but never the overdose. `WGIntoxicationSystem` reads the
blood, ticks the levels, announces phase changes, runs tier effects and crashes, and grants trait components (damage
slowdown, halved stuns, pain numbness, Anvil's damper and its Poison repayment); a second combat drug strains the
heart. `WGAlcoholSystem` does the same
for ethanol: the `Drunk` effect's `perUnitInBlood` holds the drunk timer at the ethanol left (`inBloodAbove` for an
overdose one). Other upstream status effects from reagents (rainbows, muting, drowsiness, forced sleep) are held with
`refresh: true` the same way, and `perUnitInBlood` on `GenericStatusEffect` scales the held time by the units left
where a reagent's old length must stay (mute toxin, chloral hydrate, fentanyl); the recreational ones clear slower
so they last as long as they used to.
`WGDependenceSystem` builds a per-family habit with tolerance and withdrawal, which only reaching the family's first tier ends; a meter above the withdrawal mark
fades at a quarter of the rate, and five habit traits (stimulant, opioid, Halo, cannabis, nicotine) start a character dependent with a first fix in hand, a copied program stick for a neuroware host; cannabis and nicotine withdrawal stop at cravings. The client draws one overlay per family and tints incoming chat (`WGDrugChatSystem`) and changes world sounds (`WGDrugAudioSystem`);
`WGDrugSpeechSystem` rushes stimulant speech and trails opioid speech off, and `WGNodSystem` makes a heavy opioid high
nod off: the eyes shut and sound muffles for a few seconds while the body stays up and crawls (`WGNodOverlay`). `WGAnalyzerConditionsSystem` fills the health analyzer's
Conditions tab: each drug level, held status effect, crash and habit, with the reagents behind it, their clearance
time from the organs' rates, the reagents that remove them (`AdjustReagent`) and family antidotes
(`WGClearIntoxication`).

A high also shows from outside and at the helm. `WGDrugCuesSystem` adds examine lines that never name a drug: one for
the face (pupils, skin), which needs uncovered eyes and a close look and shows from a light high, and one for how the
body moves, which shows from a heavy high at any distance and through a sealed suit; drink, crashes and withdrawal
from its second stage have their own, and a neuroware host shows optics and servo faults instead.
`WGDrugPilotingSystem` changes the input a pilot gives a ship (`GetShuttleInputsEvent`): stimulants raise turn and
thrust and twitch the stick in an overdose, and their crash lowers both by more; opioids make the ship trail the stick
and hold it through a nod; cannabis softens the brakes and the turn; alcohol makes the heading wander.
`WGExoticSenseSystem` gives a tier with the `ExoticSense` trait (Chorus) a light on the screen
(`WGExoticSenseOverlay`) towards the nearest rock that would drop at least a shard of exotic matter, 500, 1,000 or
1,500 m out by phase, and makes the carrier it rides gain Drift half again as fast (`WGDriftGainEvent`).

Substances are grown (coca, the opium poppy, cannabis pressed into hash) or cooked through graded reactions, and the
solids keep their grade. Flour cuts cocaine and heroin (`fillers` on `wgReactionPurity`). `WGDeliverySystem` adds powder lines from a baggie and the bong; `WGSecondhandSmokeSystem` puts smoke
gas and a share of everything smoked into the air, and doses whoever breathes it nearby. Products other than hash,
which is legal like cannabis, are Class 3 contraband with grade-scaled prices, a sale tax and a sold alert, and `WGNarcoticGoodsSystem` gives the same marks
(`WGNarcoticMarks`) to any item or movable structure while one of its solutions holds a priced narcotic, a beaker, pill or storage tank as much as a baggie, and to a blank stick that takes a copy; the Strangers dealer sells cut stock, a Reaver
runner (`WGEncounterReaverDealer`, a trader encounter) sells full grade and buys nothing, the bong is
crafted from glass and plastic, and the uplink chemicals tab lists a few. The Bench has three laws
for it and the Medical guide a player page.

Entry points: `WGDose` and `WGClearIntoxication` (entity effects), `SharedWGIntoxicationSystem` (levels, tiers,
`IsNeurowareHost`), `WGIntoxicationSystem.Tick`, `WGDependenceSystem.Tick`, `WGDeliverySystem`,
`WGDrugCuesSystem.GetCues`, `WGDrugPilotingSystem.GetHandling`, `WGExoticSenseSystem.Tick`.

## Purity

A purity grade (0-100%) that any reagent in any solution can carry. A reagent with no grade is standard grade, so
untouched chemistry looks and behaves exactly as before. A graded reagent works at its grade when metabolised (60%
medicine heals 60%), its impure fraction deals Poison (`wg.purity.impurity_damage` per unit, default 1.0;
`wg.purity.narcotic_impurity_damage`, default 6.0, for the Narcotics reagent group), overdose thresholds count
effective units (units x grade), and it sells at its grade.

Grades live in a side table on `Solution` (`Solution.Purity`, keyed by reagent prototype; see `Solution.Purity.cs`),
which `Solution`'s add, split, transfer and remove methods keep in step. They come from:

- Reactions: the product takes the mean grade of the graded reactants it consumes (standard-grade ones don't raise
  it) times the reaction's process factor, set by a `wgReactionPurity` prototype keyed by reaction ID (`optimalTemp`,
  `tolerance`, `floor`, and `drift`, which `WGPurityDriftSystem` rolls into a per-round shift of the optimum). No
  entry means factor 1.0. A reaction that sets into a solid uses the `WGCreateGradedEntity` effect, which passes the
  grade to the spawned item.
- `WGProducePurity` on produce: grades by plant potency, 50% at 0 to 100% at 100.
- `WGSolutionPurity`: fixed grades at map init, for vendor, dealer and loot stock.

Dispensers give standard grade. Touch reactions, tile reactions and fuel ignore grades for now.

## Neuroware

Drugs for synthetics. A body whose brain organ has `WGNeurowareHost` (the positronic brains) ignores chemical doses
and takes data sticks instead: using a stick on a host loads its program (2 s on yourself, 4 s on someone else),
which feeds the twin drug profile's level at the stick's integrity and gives the drug's rush, so highs, crashes and
dependence all come from the Narcotics part. A full load is the profile's `neuroware.amount`. Copying a stick onto a
blank gives the copy one load at 85% of the source's integrity and costs the source a load and a tenth of its
integrity, so a stick yields at most as many sticks as it has loads; the missing
share makes a running program glitch (stun, a forced beep, scrambled speech, a false HUD fault). A blank used on
anything that holds a drug with a twin (3 s) reads it in and uses it up: the stick's integrity is the drug's grade,
and it takes up to three loads, none larger than a full one. Blanks print at the autolathe. A written stick is a
narcotic at the market unless its profile is `legal`, as cannabis and its twin Idle are. The Wolfmed synthetic HUD lists what is running as fault lines and its shader is driven by the tier,
Redline's speed costs charge and heat, and a chassis panel reboot stops every program and clears the levels.

Entry points: `WGNeurowareSystem` (`TryStartLoad`, `Load`, `TryCopy`, `TryStartEncode`, `Encode`, `Tick`), `WGNeurowareStickComponent`, and the
client partial of `WolfmedSyntheticHudOverlaySystem`. Sticks are in `Resources/Prototypes/_WG/WolfChem/Neuroware`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/WolfChem/Narcotics/HealthAnalyzerSystem.Narcotics.cs`](Narcotics/HealthAnalyzerSystem.Narcotics.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/MetabolizerSystem.Granular.cs`](Narcotics/MetabolizerSystem.Granular.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGAlcoholSystem.cs`](Narcotics/WGAlcoholSystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGAnalyzerConditionsSystem.cs`](Narcotics/WGAnalyzerConditionsSystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGDeliverySystem.cs`](Narcotics/WGDeliverySystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGDependenceSystem.cs`](Narcotics/WGDependenceSystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGDrugCuesSystem.cs`](Narcotics/WGDrugCuesSystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGDrugPilotingSystem.cs`](Narcotics/WGDrugPilotingSystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGDrugSpeechSystem.cs`](Narcotics/WGDrugSpeechSystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGExoticSenseSystem.cs`](Narcotics/WGExoticSenseSystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGIntoxicationSystem.Blood.cs`](Narcotics/WGIntoxicationSystem.Blood.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGIntoxicationSystem.cs`](Narcotics/WGIntoxicationSystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGIntoxicationSystem.Traits.cs`](Narcotics/WGIntoxicationSystem.Traits.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGMetabolismCarryComponent.cs`](Narcotics/WGMetabolismCarryComponent.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGNarcoticGoodsSystem.cs`](Narcotics/WGNarcoticGoodsSystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGNarcoticMarkedComponent.cs`](Narcotics/WGNarcoticMarkedComponent.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGNodSystem.cs`](Narcotics/WGNodSystem.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGPilotImpairmentComponent.cs`](Narcotics/WGPilotImpairmentComponent.cs)
- [`Content.Server/_WG/WolfChem/Narcotics/WGSecondhandSmokeSystem.cs`](Narcotics/WGSecondhandSmokeSystem.cs)
- [`Content.Server/_WG/WolfChem/Neuroware/WGNeurowareSystem.cs`](Neuroware/WGNeurowareSystem.cs)
- [`Content.Server/_WG/WolfChem/Purity/BotanySystem.Purity.cs`](Purity/BotanySystem.Purity.cs)
- [`Content.Server/_WG/WolfChem/Purity/WGCreateGradedEntity.cs`](Purity/WGCreateGradedEntity.cs)
- [`Content.Server/_WG/WolfChem/Purity/WGProducePurityComponent.cs`](Purity/WGProducePurityComponent.cs)
- [`Content.Server/_WG/WolfChem/Purity/WGPurityDriftSystem.cs`](Purity/WGPurityDriftSystem.cs)
- [`Content.Server/_WG/WolfChem/Purity/WGPuritySystem.cs`](Purity/WGPuritySystem.cs)
- [`Content.Server/_WG/WolfChem/Purity/WGPurityTesterComponent.cs`](Purity/WGPurityTesterComponent.cs)
- [`Content.Server/_WG/WolfChem/Purity/WGPurityTesterSystem.cs`](Purity/WGPurityTesterSystem.cs)
- [`Content.Server/_WG/WolfChem/Purity/WGSolutionPurityComponent.cs`](Purity/WGSolutionPurityComponent.cs)
- [`Content.Server/_WG/WolfChem/Purity/WGSolutionPuritySystem.cs`](Purity/WGSolutionPuritySystem.cs)

### Shared

- [`Content.Shared/_WG/WolfChem/Narcotics/Delivery/WGBaggieSystem.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/Delivery/WGBaggieSystem.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/Delivery/WGDeliveryComponents.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/Delivery/WGDeliveryComponents.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/SharedWGIntoxicationSystem.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/SharedWGIntoxicationSystem.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/WGAnalyzerConditions.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/WGAnalyzerConditions.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/WGClearIntoxication.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/WGClearIntoxication.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/WGDependenceComponent.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/WGDependenceComponent.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/WGDose.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/WGDose.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/WGDrugProfilePrototype.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/WGDrugProfilePrototype.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/WGExoticSenseComponent.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/WGExoticSenseComponent.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/WGIntoxicationComponent.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/WGIntoxicationComponent.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/WGNeurowareHostComponent.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/WGNeurowareHostComponent.cs)
- [`Content.Shared/_WG/WolfChem/Narcotics/WGSedation.cs`](../../../Content.Shared/_WG/WolfChem/Narcotics/WGSedation.cs)
- [`Content.Shared/_WG/WolfChem/Neuroware/WGNeurowareComponents.cs`](../../../Content.Shared/_WG/WolfChem/Neuroware/WGNeurowareComponents.cs)
- [`Content.Shared/_WG/WolfChem/Purity/ChemicalReactionSystem.Purity.cs`](../../../Content.Shared/_WG/WolfChem/Purity/ChemicalReactionSystem.Purity.cs)
- [`Content.Shared/_WG/WolfChem/Purity/Solution.Purity.cs`](../../../Content.Shared/_WG/WolfChem/Purity/Solution.Purity.cs)
- [`Content.Shared/_WG/WolfChem/Purity/WGPurityCVars.cs`](../../../Content.Shared/_WG/WolfChem/Purity/WGPurityCVars.cs)
- [`Content.Shared/_WG/WolfChem/Purity/WGPurityText.cs`](../../../Content.Shared/_WG/WolfChem/Purity/WGPurityText.cs)
- [`Content.Shared/_WG/WolfChem/Purity/WGReactionPurityPrototype.cs`](../../../Content.Shared/_WG/WolfChem/Purity/WGReactionPurityPrototype.cs)

### Client

- [`Content.Client/_WG/WolfChem/Narcotics/WGDrugAudioSystem.cs`](../../../Content.Client/_WG/WolfChem/Narcotics/WGDrugAudioSystem.cs)
- [`Content.Client/_WG/WolfChem/Narcotics/WGDrugChatSystem.cs`](../../../Content.Client/_WG/WolfChem/Narcotics/WGDrugChatSystem.cs)
- [`Content.Client/_WG/WolfChem/Narcotics/WGExoticSenseOverlay.cs`](../../../Content.Client/_WG/WolfChem/Narcotics/WGExoticSenseOverlay.cs)
- [`Content.Client/_WG/WolfChem/Narcotics/WGExoticSenseSystem.cs`](../../../Content.Client/_WG/WolfChem/Narcotics/WGExoticSenseSystem.cs)
- [`Content.Client/_WG/WolfChem/Narcotics/WGIntoxicationOverlay.cs`](../../../Content.Client/_WG/WolfChem/Narcotics/WGIntoxicationOverlay.cs)
- [`Content.Client/_WG/WolfChem/Narcotics/WGIntoxicationSystem.cs`](../../../Content.Client/_WG/WolfChem/Narcotics/WGIntoxicationSystem.cs)
- [`Content.Client/_WG/WolfChem/Narcotics/WGNodOverlay.cs`](../../../Content.Client/_WG/WolfChem/Narcotics/WGNodOverlay.cs)
- [`Content.Client/_WG/WolfChem/Narcotics/WolfmedDiagnosticPanel.Conditions.cs`](../../../Content.Client/_WG/WolfChem/Narcotics/WolfmedDiagnosticPanel.Conditions.cs)
- [`Content.Client/_WG/WolfChem/Neuroware/WolfmedSyntheticHudOverlaySystem.Neuroware.cs`](../../../Content.Client/_WG/WolfChem/Neuroware/WolfmedSyntheticHudOverlaySystem.Neuroware.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/AlcoholTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/AlcoholTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/AnalyzerConditionsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/AnalyzerConditionsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/DeliveryTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/DeliveryTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/DependenceTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/DependenceTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/DrugCuesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/DrugCuesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/DrugPilotingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/DrugPilotingTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/DrugSpeechTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/DrugSpeechTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/ExoticSenseTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/ExoticSenseTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/HeroinSedationTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/HeroinSedationTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/IntoxicationTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/IntoxicationTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/NodTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/NodTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/RadioProbeSystem.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/RadioProbeSystem.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/SecondhandSmokeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/SecondhandSmokeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/SoldAlertTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/SoldAlertTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/StatusInBloodTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/StatusInBloodTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/SubstanceTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Narcotics/SubstanceTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Neuroware/NeurowareTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Neuroware/NeurowareTest.cs)
- [`Content.IntegrationTests/Tests/_WG/WolfChem/Purity/PurityTest.cs`](../../../Content.IntegrationTests/Tests/_WG/WolfChem/Purity/PurityTest.cs)

### Unit tests

- [`Content.Tests/_WG/WolfChem/Purity/SolutionPurityTest.cs`](../../../Content.Tests/_WG/WolfChem/Purity/SolutionPurityTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/WolfChem/Narcotics/encounters.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/encounters.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/dealer.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/dealer.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/delivery.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/delivery.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/marks.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/marks.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/produce.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/produce.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/reaver_dealer.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/reaver_dealer.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/solids.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/solids.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/vials.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Entities/vials.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/guidebook.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/guidebook.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/listings.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/listings.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/profiles.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/profiles.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/reactions.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/reactions.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/reagents.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/reagents.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Recipes/baggie.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Recipes/baggie.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Recipes/bong.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Recipes/bong.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Recipes/hash.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Recipes/hash.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/Recipes/vial.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/Recipes/vial.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/shaders.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/shaders.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/tags.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/tags.yml)
- [`Resources/Prototypes/_WG/WolfChem/Narcotics/traits.yml`](../../../Resources/Prototypes/_WG/WolfChem/Narcotics/traits.yml)
- [`Resources/Prototypes/_WG/WolfChem/Neuroware/lathe.yml`](../../../Resources/Prototypes/_WG/WolfChem/Neuroware/lathe.yml)
- [`Resources/Prototypes/_WG/WolfChem/Neuroware/loot.yml`](../../../Resources/Prototypes/_WG/WolfChem/Neuroware/loot.yml)
- [`Resources/Prototypes/_WG/WolfChem/Neuroware/shaders.yml`](../../../Resources/Prototypes/_WG/WolfChem/Neuroware/shaders.yml)
- [`Resources/Prototypes/_WG/WolfChem/Neuroware/sticks.yml`](../../../Resources/Prototypes/_WG/WolfChem/Neuroware/sticks.yml)
- [`Resources/Prototypes/_WG/WolfChem/Purity/Entities/Objects/purity_tester.yml`](../../../Resources/Prototypes/_WG/WolfChem/Purity/Entities/Objects/purity_tester.yml)
- [`Resources/Prototypes/_WG/WolfChem/Purity/Recipes/lathe.yml`](../../../Resources/Prototypes/_WG/WolfChem/Purity/Recipes/lathe.yml)

### Localization

- [`Resources/Locale/en-US/_WG/WolfChem/Narcotics/analyzer.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Narcotics/analyzer.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Narcotics/cues.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Narcotics/cues.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Narcotics/dealer.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Narcotics/dealer.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Narcotics/delivery.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Narcotics/delivery.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Narcotics/dependence.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Narcotics/dependence.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Narcotics/guidebook.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Narcotics/guidebook.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Narcotics/intoxication.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Narcotics/intoxication.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Narcotics/speech.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Narcotics/speech.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Narcotics/substances.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Narcotics/substances.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Narcotics/twins.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Narcotics/twins.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Neuroware/neuroware.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Neuroware/neuroware.ftl)
- [`Resources/Locale/en-US/_WG/WolfChem/Purity/purity.ftl`](../../../Resources/Locale/en-US/_WG/WolfChem/Purity/purity.ftl)

### Textures

- [`Resources/Textures/_WG/WolfChem/Narcotics/Hydroponics/coca.rsi/`](../../../Resources/Textures/_WG/WolfChem/Narcotics/Hydroponics/coca.rsi/)
- [`Resources/Textures/_WG/WolfChem/Narcotics/Hydroponics/opium_poppy.rsi/`](../../../Resources/Textures/_WG/WolfChem/Narcotics/Hydroponics/opium_poppy.rsi/)
- [`Resources/Textures/_WG/WolfChem/Narcotics/Objects/baggie.rsi/`](../../../Resources/Textures/_WG/WolfChem/Narcotics/Objects/baggie.rsi/)
- [`Resources/Textures/_WG/WolfChem/Narcotics/Objects/bong.rsi/`](../../../Resources/Textures/_WG/WolfChem/Narcotics/Objects/bong.rsi/)
- [`Resources/Textures/_WG/WolfChem/Narcotics/Objects/powder_line.rsi/`](../../../Resources/Textures/_WG/WolfChem/Narcotics/Objects/powder_line.rsi/)
- [`Resources/Textures/_WG/WolfChem/Narcotics/Objects/solids.rsi/`](../../../Resources/Textures/_WG/WolfChem/Narcotics/Objects/solids.rsi/)
- [`Resources/Textures/_WG/WolfChem/Narcotics/Objects/vial.rsi/`](../../../Resources/Textures/_WG/WolfChem/Narcotics/Objects/vial.rsi/)
- [`Resources/Textures/_WG/WolfChem/Narcotics/Shaders/intoxication.swsl`](../../../Resources/Textures/_WG/WolfChem/Narcotics/Shaders/intoxication.swsl)
- [`Resources/Textures/_WG/WolfChem/Narcotics/Shaders/intoxication_trail.swsl`](../../../Resources/Textures/_WG/WolfChem/Narcotics/Shaders/intoxication_trail.swsl)
- [`Resources/Textures/_WG/WolfChem/Neuroware/Objects/stick.rsi/`](../../../Resources/Textures/_WG/WolfChem/Neuroware/Objects/stick.rsi/)
- [`Resources/Textures/_WG/WolfChem/Neuroware/Shaders/neuroware.swsl`](../../../Resources/Textures/_WG/WolfChem/Neuroware/Shaders/neuroware.swsl)

### Guidebook

- [`Resources/ServerInfo/_WG/WolfChem/Narcotics/Narcotics.xml`](../../../Resources/ServerInfo/_WG/WolfChem/Narcotics/Narcotics.xml)

### Docs

- [`Docs/_WG/WolfChem/DESIGN.md`](../../../Docs/_WG/WolfChem/DESIGN.md)

## Non-modular edits

- [`Content.Client/Chemistry/UI/ChemMasterWindow.xaml.cs`](../../../Content.Client/Chemistry/UI/ChemMasterWindow.xaml.cs)
- [`Content.Client/UserInterface/Systems/Chat/ChatUIController.cs`](../../../Content.Client/UserInterface/Systems/Chat/ChatUIController.cs): chat seen through the local player's high, before tags split the message text
- [`Content.IntegrationTests/Pair/TestPair.cs`](../../../Content.IntegrationTests/Pair/TestPair.cs): records radio sends for the sold-alert test
- [`Content.Server/Body/Systems/MetabolizerSystem.cs`](../../Body/Systems/MetabolizerSystem.cs)
  - impurity damage
  - a rate below 0.01 u a tick builds up instead of rounding to nothing
  - a graded reagent works at its grade
  - the impure fraction is toxic
  - a tick that only carries a slow rate over still takes a slot
- [`Content.Server/Body/Systems/StomachSystem.cs`](../../Body/Systems/StomachSystem.cs)
  - read before the removal prunes it
  - a digested reagent keeps its grade
- [`Content.Server/Botany/Systems/BotanySystem.Produce.cs`](../../Botany/Systems/BotanySystem.Produce.cs): narcotic produce is graded by potency
- [`Content.Server/Cargo/Systems/PricingSystem.cs`](../../Cargo/Systems/PricingSystem.cs): a reagent is worth its grade
- [`Content.Server/Chemistry/EntitySystems/ChemMasterSystem.cs`](../../Chemistry/EntitySystems/ChemMasterSystem.cs)
  - read before the removal prunes it
  - the reagent keeps its grade on the way out of the buffer
  - the reagent keeps its grade on the way into the buffer
- [`Content.Server/EntityEffects/EffectConditions/ReagentThreshold.cs`](../../EntityEffects/EffectConditions/ReagentThreshold.cs): thresholds count effective units (units x grade)
- [`Content.Server/EntityEffects/Effects/Drunk.cs`](../../EntityEffects/Effects/Drunk.cs): drunkenness that follows the reagent in the blood
- [`Content.Server/EntityEffects/Effects/StatusEffects/GenericStatusEffect.cs`](../../EntityEffects/Effects/StatusEffects/GenericStatusEffect.cs): status time that follows the reagent in the blood
- [`Content.Server/Medical/HealthAnalyzerSystem.cs`](../../Medical/HealthAnalyzerSystem.cs): drug levels, held effects and dependence
- [`Content.Shared/_Onyx/Medical/HealthAnalyzerChemicalInfo.cs`](../../../Content.Shared/_Onyx/Medical/HealthAnalyzerChemicalInfo.cs): the reagent's grade
- [`Content.Shared/Chemistry/Components/Solution.cs`](../../../Content.Shared/Chemistry/Components/Solution.cs)
  - copy the grades
  - a standard-grade addition dilutes a graded reagent
  - drop grades of reagents scaled away
  - drop grades of removed reagents
  - an empty solution has no grades
  - the remove and re-add below would lose grades
  - both halves keep their grades
  - the split-off solution takes the grades of what it received
  - RemoveAllSolution below would drop grades
  - blend each reagent with the other solution's grade
  - keep the grades of reagents that stay
- [`Content.Shared/Chemistry/EntitySystems/SharedSolutionContainerSystem.cs`](../../../Content.Shared/Chemistry/EntitySystems/SharedSolutionContainerSystem.cs): goggles show grades
- [`Content.Shared/Chemistry/Reaction/ChemicalReactionSystem.cs`](../../../Content.Shared/Chemistry/Reaction/ChemicalReactionSystem.cs)
  - read before the reactants go
  - the product takes the reaction's grade
- [`Content.Shared/Chemistry/SharedChemMaster.cs`](../../../Content.Shared/Chemistry/SharedChemMaster.cs)
  - grades of Reagents
  - grades of BufferReagents
- [`Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs`](../../../Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs): drug levels, held effects and dependence
- [`Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml`](../../../Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml): neuroware twins apply these to a synthetic's own body
- [`Resources/Prototypes/_ES/Reagents/gases.yml`](../../../Resources/Prototypes/_ES/Reagents/gases.yml): thin haze from smoking is harmless; fire-thick smoke still chokes
- [`Resources/Prototypes/_Impstation/Reagents/biological.yml`](../../../Resources/Prototypes/_Impstation/Reagents/biological.yml): held while it is in the blood instead of piling up each tick
- [`Resources/Prototypes/_Mono/Catalogs/Fills/Lockers/salvmaint_locker.yml`](../../../Resources/Prototypes/_Mono/Catalogs/Fills/Lockers/salvmaint_locker.yml): neuroware sticks turn up in maintenance lockers
- [`Resources/Prototypes/_Mono/Entities/Objects/Misc/solids.yml`](../../../Resources/Prototypes/_Mono/Entities/Objects/Misc/solids.yml): fits a baggie
- [`Resources/Prototypes/_Mono/Reagents/letoferol.yml`](../../../Resources/Prototypes/_Mono/Reagents/letoferol.yml): drunkenness follows the units in the blood
- [`Resources/Prototypes/_Mono/Reagents/medicine.yml`](../../../Resources/Prototypes/_Mono/Reagents/medicine.yml)
  - only in an overdose, like its jitter
  - drunkenness follows the units in the blood
- [`Resources/Prototypes/_Mono/Reagents/narcotics.yml`](../../../Resources/Prototypes/_Mono/Reagents/narcotics.yml)
  - doses the Heroin profile at one and a half strength
  - held while it is in the blood instead of piling up each tick
  - held at the units left, as long as it used to pile up to
  - drunkenness follows the units in the blood
- [`Resources/Prototypes/_NF/Entities/Objects/Specific/Hydroponics/leaves.yml`](../../../Resources/Prototypes/_NF/Entities/Objects/Specific/Hydroponics/leaves.yml): fits a baggie
- [`Resources/Prototypes/_NF/Reagents/medicine.yml`](../../../Resources/Prototypes/_NF/Reagents/medicine.yml): held while it is in the blood instead of piling up each tick
- [`Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml`](../../../Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml)
  - medical opioids build dependence slowly through the Opium profile
  - medical opioids build dependence slowly through the Heroin profile
- [`Resources/Prototypes/Catalog/Fills/Crates/botany.yml`](../../../Resources/Prototypes/Catalog/Fills/Crates/botany.yml): drug crops come only from this crate and dealers
- [`Resources/Prototypes/Entities/Markers/Spawners/Random/maintenance.yml`](../../../Resources/Prototypes/Entities/Markers/Spawners/Random/maintenance.yml): neuroware sticks turn up in maintenance
- [`Resources/Prototypes/Entities/Markers/Spawners/Random/Salvage/tables_loot.yml`](../../../Resources/Prototypes/Entities/Markers/Spawners/Random/Salvage/tables_loot.yml): neuroware sticks turn up in salvage treasure
- [`Resources/Prototypes/Entities/Objects/Consumable/Smokeables/Cigarettes/cigarette.yml`](../../../Resources/Prototypes/Entities/Objects/Consumable/Smokeables/Cigarettes/cigarette.yml): the THC cigarette held plant fertiliser instead of THC
- [`Resources/Prototypes/Entities/Objects/Specific/Hydroponics/leaves.yml`](../../../Resources/Prototypes/Entities/Objects/Specific/Hydroponics/leaves.yml)
  - fits a baggie
  - fits a baggie and a bong bowl
- [`Resources/Prototypes/Guidebook/medical.yml`](../../../Resources/Prototypes/Guidebook/medical.yml): drugs, grades and neuroware
- [`Resources/Prototypes/Hydroponics/seeds.yml`](../../../Resources/Prototypes/Hydroponics/seeds.yml): the opium poppy is a poppy mutation
- [`Resources/Prototypes/Reagents/cleaning.yml`](../../../Resources/Prototypes/Reagents/cleaning.yml)
  - held while it is in the blood instead of piling up each tick
  - held while it is being digested instead of piling up each tick
- [`Resources/Prototypes/Reagents/Consumable/Drink/alcohol.yml`](../../../Resources/Prototypes/Reagents/Consumable/Drink/alcohol.yml)
  - drunk lasts while ethanol does, so it clears at half speed to keep the old duration
  - halved since ethanol now stays twice as long above the threshold
  - drunkenness follows the ethanol left in the blood
- [`Resources/Prototypes/Reagents/medicine.yml`](../../../Resources/Prototypes/Reagents/medicine.yml)
  - drunkenness follows the units in the blood
  - Dylovene flushes combat drugs, Redline and Anvil
  - only in an overdose, like its jitter
  - held while it is in the blood instead of piling up each tick
  - Psicodine calms empathogen and cannabis highs
  - Haloperidol brings down stimulant and psychedelic highs
- [`Resources/Prototypes/Reagents/narcotics.yml`](../../../Resources/Prototypes/Reagents/narcotics.yml)
  - crystal sells by grade
  - crystal stays in the blood for minutes, so per-tick numbers below are cut to match
  - was -1 a tick at 50x the clearance
  - was -0.5
  - was 0.75 a tick at 50x the clearance
  - was 2 a tick at 50x the clearance
  - was 2
  - the crystal high and its effects last while it stays in the blood
  - doses the Crystal profile for its overlay, dependence and crash
  - the speed is on the Crystal profile's tiers, so tolerance and the Burst twin get it
  - stun resistance is a trait of the Crystal profile's heavy tier
  - stamina-crit resistance and no damage slowdown are traits of the Crystal profile's heavy tier
  - clears with the rest of the crystal
  - builds the stimulant habit; it keeps its own speed, so it doesn't raise Crystal's level
  - a cannabis high lasts while the THC stays in the blood
  - doses the Cannabis profile
  - was -1 a tick at 25x the clearance
  - held while THC is in the blood instead of piling up 16 s a tick
  - nicotine builds up in the blood over a cigarette
  - doses the Nicotine profile, which is what builds the habit
  - was -0.2 a tick at 50x the clearance
  - was -0.1
  - clears slower so the high lasts as long as before
  - scaled to the slower clearance
  - held while it is in the blood instead of piling up each tick
  - drunkenness follows the units in the blood
  - held at the units left, as long as it used to pile up to
- [`Resources/Prototypes/Reagents/toxins.yml`](../../../Resources/Prototypes/Reagents/toxins.yml)
  - held while it is in the blood instead of piling up each tick
  - held at the units left, as long as it used to pile up to
  - clears slower so the high lasts as long as before
- [`Resources/Prototypes/Recipes/Lathes/Packs/engineering.yml`](../../../Resources/Prototypes/Recipes/Lathes/Packs/engineering.yml): blank data sticks print at the autolathe
- [`Resources/Prototypes/Recipes/Lathes/Packs/medical.yml`](../../../Resources/Prototypes/Recipes/Lathes/Packs/medical.yml): purity testers print with the chemistry kit

<!-- WOLFGATE-GENERATED END -->
