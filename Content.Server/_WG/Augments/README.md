# Augments

Cyberware a character carries from round to round, after the augments panel on Meridian Rift (Nova Sector). Every
cybernetic part, organ and upgrading autosurgeon in the game is a `wgAugment` in `augments.yml`: plain styled limbs,
hands and feet (free), J.W.L arms, S.P.E.E.D legs, D.E.X hands, cyber eyes, IAS lungs, the faction hearts and
Sandevistans, mantis blades, dermal armor and muscle fibers. A piece of cyberware missing from it is lost at the
end of every round.

A character gets an augment three ways:

- **The creator.** The Augments tab draws the character as a doll (`WGAugmentDoll`); hovering a part outlines it
  and clicking it lists that part's limb, organs and implants as cards (`WGAugmentCard`), each with its icon, what it
  does and its price, greyed out where it can't go. An icon (`WGAugmentIcons`) is the augment's `icon`, else the sprite
  of the entity it installs; implants name one because autosurgeons all look alike. A styled limb's style is picked
  from a dropdown that draws the limb in each (`WGAugmentStylePicker`). The loadout beside it totals what confirming
  buys and pays back. The tab takes its colors and fonts from the player's Wolfgate skin (`WGAugmentTheme`) and
  folds from three columns to two where it is narrow. A `price` is taken once from the character's bank
  when it is saved with the augment (`ServerPreferencesManager.SettleAugments`), and the character owns it
  (`AugmentUnlocks`); one it no longer picks is sold back for `WGAugmentRules.RefundPercent` (half) of its price
  if it was paid for here. Only the server adds unlocks. Nothing is gated by
  faction: any character can buy the faction hearts and implants here.

  The value follows the bought hardware, so it can be stolen and sold (`WGAugmentSystem.Paid.cs`). A part a
  character spawns with and paid for carries a `WGAugmentPaidComponent` mark naming that character. Cut out of that
  body, the part is priced at its own price plus what its augments sell back for, so a cargo depot, a trader or
  anything else that pays by appraised price buys it as loot; inside the body it is worth no more than any part,
  so a corpse has to be cut open first. Whoever's body holds the marked part when it is saved instead owns the
  augment as paid for. Either way the character it was taken from loses the unlock and the pick, online or not,
  and is told in chat: when the part is sold or destroyed during the round, or when the taker's body is saved.
  A purchase has one mark: a printed or cloned copy has none, is priced like any part and ends with the round; selling
  in the creator removes the mark from the part still in the body; and a character can't sell a part that has left
  its body. A body left behind by `ghostrespawn` keeps its marks for whoever loots it.
- **Buying it in a round** from the faction lathes, uplinks or loadouts, as before, and installing it. That
  hardware is unmarked, so it works for the round and is gone at its end: only paid-for hardware persists, so a
  printed J.W.L arm or an uplink Sandevistan can't stand in for the creator's price.
- **Scavenging.** `WGAugmentSelector` (the `WGAugmentScavengeTable`) drops a priced augment's bare part or organ,
  which a surgeon puts in, or an upgrade's autosurgeon. It turns up only on the floors of wrecks
  (`WGAugmentScavengeSpawner` in the wreck debris floor list, `wrecks.yml`). The weight is the inverse of the price, so a Sandevistan is about 33 times rarer than cyber eyes.
  A find is as good as a bought piece: the spawner (`WGAugmentScavengeSpawnerComponent`) gives it a mark that
  belongs to nobody, so it sells as loot for what the augment sells back for, and kept through the end of a round
  it is owned as paid for. A marked autosurgeon passes its mark to the part it upgrades. That value is why finds
  are rare: roughly one per 13 enormous wrecks or 55 small ones, worth about 115,000 on average.

Implants (`kind: Upgrade`) are a set of their own (`AugmentImplants`), not slot choices: an implant's `slot` is the
part it goes in, matching its autosurgeon's target. A part holds `implantSlots` of them (a J.W.L arm or S.P.E.E.D leg
two, other limbs and natural parts one, the torso two or three under a cyber chest, a missing limb none), and `host`
says whether that part must be synthetic: a cybernetic or trait limb, plating, or a natural part not built of flesh (an
IPC's). Mantis blades need one. A
hand or foot with no pick of its own counts as its limb's (`WGAugmentRules.LimbAbove`). An
implant that no longer fits after a limb changes is dropped. They are stored in the augments column as `Implant:`
entries; the old one-per-slot entries (`LeftArmImplant`, `Dermal`, ...) still load.

The rules read the body a character is built with, not its species': one converted to a biotype that isn't its
species' own gets that biotype's body (`WGAugmentRules.Body`), so a synthetic human has the chassis' parts, with no
heart to replace and no flesh for an organic implant. Changing the biotype in the creator drops the picks the new
body can't take. A pick that still fails to reach the body at spawn (`PlayedCharacter.Unfitted`, logged as a
warning) was never there to lose: a save or a forfeit leaves it and its unlock on the profile.

Wolfgate's own cyberware is in `implants.yml`. The tool arm and the finger lighter are deployables
(`WGDeployableImplantComponent`, one subclass each so a part can hold both): `WGDeployableImplantSystem` gives the body
an action that folds the implant's item into that side's hand and back, and an EMP folds it in and locks it. The tool
arm lists six tools, so its action opens a wheel under the mouse instead (`WGDeployableWheel`, sent and answered by
network events): picking a tool folds it out in place of the one that was out, and picking that one folds it back in.
A tool is spawned as it folds out and deleted as it folds in, so the welder comes out full each time. Pressing drop (Q) with a
deployable's item or a mantis blade in the active hand folds it back in (`WGAugmentDropRetractSystem`, bound ahead of
the hands system's drop, through the implant's own retract); anything else drops as usual. A mantis blade hits for
Slash 17 + Piercing 9 at 1.5 a second, and its pair strikes along at two thirds (Goob `Multihit`), so one blade is
well under an energy sword and a pair a little over it. The
smartlink (`WGSmartlinkSystem`) is recoil control for a gun held in its hand: the camera kick is cut to about a third,
projectiles fly a fifth faster, the spread each shot adds is halved and it settles half again as fast. The gun's
tightest and widest spread stay its own (scaling those let a wielded rifle's flat wield bonus floor it at zero), so on
a wielded rifle, whose spread the wield bonus already pins at zero, the kick and the speed are what it changes. An EMP
scrambles it until it wears off, and the gun then kicks and spreads more than unlinked, with no faster rounds. While linked, the player's screen (`WGSmartlinkOverlay`) shows the gun's rounds by the cursor and brackets the
living, uncloaked mob in sight within twelve tiles that is nearest the cursor, with its range. The PD-VI HF-12 prosthetic liver (`WGAugmentScrubberLiver`) metabolises alcohol, poison and narcotics four times as fast and clears
Wolfmed toxin load three times as fast (`WolfmedOrganComponent.ClearanceFactor`).

The PD-VI OV-40 "Tapetum" eyes (`WGAugmentThermalTintEyes`, `WGThermalTintsSystem`) put White Dream's `ThermalVision`,
Mono's `NightVision` and `WGThermalTintsComponent` on the body. Neither vision has a toggle of its own: one action steps
through the bands in `implants.yml` and then switches off. The red band is thermal, with its body colour and wash; the
others are night vision with the quad-tube goggles' settings, minus the tubes, each in its own phosphor. Each switch
plays the goggles' click under a raised scanner chirp and a quarter-second glitch (`WGThermalGlitchOverlay`) in the new
band's colour, with no text. The wash isn't in the thermal vision's network state, so `WGThermalTintsVisualsSystem`
copies it from the band on every state. They keep eye protection but not flash immunity.

The TSF KR-30 and KR-31 "Corona" frag and concussion charges (`WGAugmentHaloBurst`, `WGAugmentHaloStun`,
`WGBurstLauncherSystem`) are chest upgrades that throw a ring of eight amber orbs
(`WGHaloOrbComponent`) out from the body. The orbs drift to a stop about a tile and a half out, stopping at walls, and go off
together after a two-second fuse, each blasting the mobs within reach and in sight: damage for the frag, stamina for the
concussion. Like an explosion, each blast also throws everything loose it catches, outward from the wearer. Until then `WGHaloArmingComponent` holds the wearer still inside a shield, on both sides so the client doesn't
predict a step, and their own blasts pass them by. Each model is its own component (`WGHaloBurstComponent`,
`WGHaloStunComponent`) with its own action, so both can be fitted, but only one ring is out at a time. The action holds
two bursts as charges and a spent one comes back every three minutes; the button's cooldown sweep shows the fuse, then
the wait for the next charge when it is empty. `Tools/_WG/Augments/build_halo_sprites.py` draws the shield and orb.

Three implants bring back the combat traits Monolith removed (#5023), paid for and losable: the adrenal booster
gives Monolith's `Vigor` stamina bonus, the threat-sense sole its `TrapAvoider`, and the pain editor
(`WGPainEditorSystem`) scales pain gain down in place of Tenacity, whose crit threshold Wolfmed doesn't read. The
booster doesn't stack with the trait it overlaps: it holds its stamina bonus off a body with Lethargy, as Vigor was
exclusive with it. The booster also gives a second wind
(`WGAdrenalBoosterSystem`): a knockdown that lands while stamina is at or over its threshold ends after 1.5 s with
stamina restored, then the booster rests two minutes; slips and pain shocks don't spend it. A pain shock's Wolfmed
adrenaline lasts twice as long on its body, so the pain inhibitor keeps you up and the booster gets you back up.

The reflex booster (`WGReflexBoosterComponent`) shares the head's one implant slot with the pain inhibitor and the
vitals monitor. While its body flies a ship, thrust and turning are 15% stronger through Mono's per-pilot multipliers on
`GetShuttleInputsEvent`, and it stacks with the Helmsmanship trait. A bullet or bolt about to hit it misses one time in
twenty (`ProjectileReflectAttemptEvent`, the hook the elite dodge pool uses); beams aren't dodged. It costs 5% more pain
from every wound.

The Kagemusha holo projector (`WGHoloProjectorSystem`) is a chest upgrade with a 90-second action. It casts two copies
of its body and swaps the body to a random tile within five steps, leaving one copy where the body stood. Every landing
tile comes from a flood fill that stops at space, walls, machines, counters and any door or airtight tile, open or shut,
so nobody lands outside the room. A doorway or a cramped spot won't cast and costs no cooldown. A copy (`WGHoloDecoy`)
is an NPC that loiters on the idle HTN at its source's speed, wearing the source's sprite, which the client copies.
It has no thermal signature, flickers out when it takes any damage, and despawns after 20 seconds.

Four combat implants:

- **HT-6 "Hotaru" arm laser** (`WGArmLaserComponent`). A deployable that folds a rapid-fire laser out of a cybernetic
  forearm. Its battery recharges on its own. With `keepCharge`, the deployable system remembers the charge when the
  laser folds away and adds what the recharger would have restored, so folding it doesn't refill it. With one in each
  arm, `WGTwinLaserSystem` (shared, so the shooter predicts both shots) fires the other laser whenever one fires, and
  doubles both lasers' spread while both are out.
- **ZS-3 "Zaslon" hand shield** (`WGHandShieldSystem`). Raised from a cybernetic hand, which it keeps busy. While it is
  up the body carries `WGHandShieldUserComponent`, and the shield stops shots, beams and blows (stun blows included,
  through the source `BeforeStaminaDamageEvent` now carries) that come within its 135� front arc. Beams glance off.
  Each hit drains its charge pool, which refills only while the shield is lowered. Drained, it collapses and can't be
  raised until it is full again (`WGDeployAttemptEvent`). The augment's `exclusive` group allows only one shield per
  body. Whether a shot is covered is decided in `SharedWGHandShieldSystem` on both sides, so the shooter's client stops
  its predicted bullet at the shield; the server drains the charge.
- **KG-9 "Kasumi" optical camouflage** (`WGOpticalCamoSystem`). An action that puts the implant's `Stealth` and
  `StealthOnMove` on the body for 20 seconds: gone at once and while still, fading back in on the move, like the MOD
  cloaking module but settling faster and giving less away per step. Attacking, shooting, firing the cannon, being hurt by someone else or an
  EMP drops it early, and then the action cools down. It won't stack with stealth from anything else.
- **KR-40 "Grom" shoulder cannon** (`WGShoulderCannonSystem`). A chest upgrade whose one action raises the cannon over
  the shoulder or folds it away. While it is up, the body carries `WGShoulderCannonDeployedComponent` and
  `WGShoulderCannonVisualsSystem` draws it on the body; the worn states are placed like tgstation's MOD flamethrower
  overlay. Where the cannon is on the near side it draws just under the helmet, and where it is on the far side
  (the `-back` states) under the whole body. A middle click (`ContentKeyFunctions.MouseMiddle`, bound on the server)
  aims it at the clicked spot. The cannon glows, unshaded, for a 0.8 s charge, then fires a slow plasma bolt there, and
  can't start another charge for 5 s. The hands stay free. An EMP folds it away and locks it for a minute.

Three more look after the body, each a component the autosurgeon puts on it. The nutriment pump
(`WGNutrimentPumpSystem`) feeds its owner a little whenever they reach starving, which leaves them peckish and food
still worth eating. The reviver (`WGReviverSystem`) is a defibrillator of its own: when the heart stops it charges
for ten seconds and shocks through `WolfmedRevivalSystem`, so it is refused for what a medic's paddles are refused
for and restarts the heart on their odds, and it heals no wound. While it charges it holds every bleed the way a
stasis weave does (`WolfmedStasisHoldComponent`, until three minutes after the shock) and runs its 60u of blood in,
towards Wolfmed's brain-safe line, so a body that bled out at the arrest line comes back at half its blood: awake,
Downed, and with the hold's three minutes to dress its wounds before they bleed again. A hemophage gets that blood
only with a wound that bleeds (dressed or held ones count), so the reviver never feeds a tumor that starved its host.
It doesn't charge for a destroyed heart. It has three shocks, fifteen seconds
apart, then recharges for ten minutes; an EMP knocks the charge out and locks it for two. The iatric monitor
(`WGIatricMonitorSystem`) gives an action that opens the health analyzer's window on its own body; the action entity
is the analyzer, and being held by the body is what keeps the window in range.

A health analyzer scan carries the cyberware in the body (`WGAugmentSystem.ReadInstalled`, sent as
`HealthAnalyzerScannedUserMessage.Augments`), and the organs tab lists it under the organs (`WGAugmentScanList`).

Installed cyberware is written back to the profile (`WGAugmentSystem.Persistence`), only when the character goes
into cryo or the round ends with it alive (not brain-dead) in the body it spawned in. `ReadBody` lists the augments in
the body: each slot's part or organ matched by prototype, and `ReadImplants` the upgrades by the components their
autosurgeon adds (`AutoSurgeonSystem.WgHasUpgrade`). Of those, only paid-for hardware is kept and owned: a marked
part, or one the profile already paid for. What the round put in unpaid ends with it, and what the character spawned
with and lost is gone along with its unlock, refunding nothing. A player who left mid-round has the stored profile
read and written instead (`SaveBodyOffline`); revokes and offline writes to one player's profiles run one at a time (`Queued`), so
two that read it first don't lose each other's change. A slot or the implants changed in the creator
mid-round keep the new pick. An implant put in a part it doesn't fit (a mantis blade in a flesh arm) works for the
round but isn't kept.

A character that doesn't make it through the round in its own body loses every augment and unlock (`Forfeit`), online
or not: a player who uses `ghostrespawn` with no body in cryo (`ForfeitAugments`), one dead at the end of the round,
one that was cloned or moved to another body, and one who left it for a ghost role. Its marks stay on the body it
left for whoever loots it. A character asleep in cryo was saved going in and is out of play until it wakes
(`_asleep`), so going back to the lobby or its stored body expiring takes nothing. The admin `respawn` command takes nothing. A job that spawns its own entity instead of
the character (a borg, the station AI) isn't tracked at all, so the profile comes out of that round as it went in.

The limbs are real Shitmed cybernetic parts: they take EMPs, repair with a welder and cable, and come off and go
back on like any chassis limb. A hand or foot a limb generates (`GenerateChildPart`) belongs to that limb, not to
a slot of its own. Markings from the Markings tab are drawn for flesh and stay off an augmented layer (its base
sprites set `allowsMarkings: false`); custom markings draw over augments like anywhere else.

Plating (`kind: Plating`) converts a part in place, since a torso can't be swapped like a limb: the rest of the body
hangs from it. The part is drawn in the picked style and counts as synthetic for implants, and it holds its
`implantSlots` if that is more than the natural part's. It installs nothing, so it is never scavenged or lost, and
it stays as picked between rounds. The one shipped is the Kiras chest chassis (`WGAugmentCyberChest`), 400,000 for a
third chest implant; implants that need flesh, like the adrenal booster, no longer fit. Head plating and missing
limbs have no content yet; the integration tests use test-only prototypes for them.

Traits that fit their own limbs at spawn (Prybar Prosthetics) claim those slots in `trait_slots.yml`.
A character with one can't take augments there: validation drops them and the tab names the trait instead. A new
limb trait needs an entry there. The trait systems swap their limbs the way augments are swapped, then call
`WGAugmentSystem.StyleTraitLimbs`, which draws them in the EE Bishop style fitted to the species, so an Avali's
prosthetics keep the Avali outline.

Entry points: `augments.yml`, `WGAugmentRules` (slots, validation, storage), `SharedHumanoidAppearanceSystem.ApplyAugments`,
run from `LoadProfile` after the biotype (the client's doll calls `ApplyAugmentVisuals` from its own `LoadProfile`),
and `WGAugmentSystem`, which swaps the parts in slot order (a limb before its hand) and then puts the implants in
through `AutoSurgeonSystem.WgApplyUpgrade`. The old part is cut loose without the events that read as an injury
(`SharedBodySystem.WgCutLoose`), so a hand keeps what it holds and Wolfmed sees no amputation.

## Styles and art

Every robotic style Meridian Rift offers (cyborg, surplus, advanced, the department sets and the IPC brands) and
each Einstein Engines cyberlimb brand is a `wgAugmentStyle`. `Tools/_WG/Augments/port.py` writes the RSIs under
`Resources/Textures/_WG/Augments`, the base sprites, `styles.yml` and the style names, from a Meridian Rift checkout:

    python Tools/_WG/Augments/port.py --source <Meridian-Rift checkout>

Every style has digitigrade legs, drawn when the character's legs are digitigrade (LegStyle). Meridian Rift draws
them for the cyborg and advanced styles; for the rest each row of the digitigrade cyborg leg samples the same row of
the style's own leg. Each layer is trimmed to the human body's outline (the digitigrade human legs for digitigrade
ones), the body clothing is drawn for, so nothing shows past a sleeve or trouser leg. Sheets that draw both legs of a
side view in one state are split into the two legs, and arms and hands drawn a pixel or two off are moved onto the body
before the trim.

Species whose limbs are clearly unlike the human's (Avali, Resomi, Vox, Diona, Moth, Vulpkanin and the rest) get every
style redrawn onto their own limbs: the script groups each limb outline a roundstart species or a leg style draws that
overlaps the human's by less than `CLOSE_ENOUGH` into a `wgAugmentShape` (`shapes.yml`) and writes each style's
version of it (`fitted` on the style). Closer bodies wear the human-shaped sprites. At load the augment
draws the version fitted to the limb the body would draw there (`WGAugmentRules.Underlying`, its leg style's or its
species'), so it takes that limb's pixels and the species' clothing fits it. Rerun the script after adding a species
or a leg style.

A profile loaded onto a body that already has augments (cloning, a DNA scrambler, an admin load) only changes the
slots it picks: the rest keep their parts and their look, as a reattached limb would. The creator's doll redraws from
scratch, so an unpicked augment comes off it.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/Augments/HealthAnalyzerSystem.Augments.cs`](HealthAnalyzerSystem.Augments.cs)
- [`Content.Server/_WG/Augments/ServerPreferencesManager.Augments.cs`](ServerPreferencesManager.Augments.cs)
- [`Content.Server/_WG/Augments/WGAdrenalBoosterSystem.cs`](WGAdrenalBoosterSystem.cs)
- [`Content.Server/_WG/Augments/WGAugmentDropRetractSystem.cs`](WGAugmentDropRetractSystem.cs)
- [`Content.Server/_WG/Augments/WGAugmentPaidComponent.cs`](WGAugmentPaidComponent.cs)
- [`Content.Server/_WG/Augments/WGAugmentScavengeSpawnerComponent.cs`](WGAugmentScavengeSpawnerComponent.cs)
- [`Content.Server/_WG/Augments/WGAugmentSystem.cs`](WGAugmentSystem.cs)
- [`Content.Server/_WG/Augments/WGAugmentSystem.Paid.cs`](WGAugmentSystem.Paid.cs)
- [`Content.Server/_WG/Augments/WGAugmentSystem.Persistence.cs`](WGAugmentSystem.Persistence.cs)
- [`Content.Server/_WG/Augments/WGAugmentSystem.Scavenge.cs`](WGAugmentSystem.Scavenge.cs)
- [`Content.Server/_WG/Augments/WGAugmentSystem.Traits.cs`](WGAugmentSystem.Traits.cs)
- [`Content.Server/_WG/Augments/WGBurstLauncherSystem.cs`](WGBurstLauncherSystem.cs)
- [`Content.Server/_WG/Augments/WGDeployableImplantSystem.cs`](WGDeployableImplantSystem.cs)
- [`Content.Server/_WG/Augments/WGHandShieldSystem.cs`](WGHandShieldSystem.cs)
- [`Content.Server/_WG/Augments/WGHoloProjectorSystem.cs`](WGHoloProjectorSystem.cs)
- [`Content.Server/_WG/Augments/WGIatricMonitorSystem.cs`](WGIatricMonitorSystem.cs)
- [`Content.Server/_WG/Augments/WGNutrimentPumpSystem.cs`](WGNutrimentPumpSystem.cs)
- [`Content.Server/_WG/Augments/WGOpticalCamoSystem.cs`](WGOpticalCamoSystem.cs)
- [`Content.Server/_WG/Augments/WGReflexBoosterServerSystem.cs`](WGReflexBoosterServerSystem.cs)
- [`Content.Server/_WG/Augments/WGReviverSystem.cs`](WGReviverSystem.cs)
- [`Content.Server/_WG/Augments/WGShoulderCannonSystem.cs`](WGShoulderCannonSystem.cs)

### Shared

- [`Content.Shared/_WG/Augments/AutoSurgeonSystem.Augments.cs`](../../../Content.Shared/_WG/Augments/AutoSurgeonSystem.Augments.cs)
- [`Content.Shared/_WG/Augments/HumanoidAppearanceComponent.Augments.cs`](../../../Content.Shared/_WG/Augments/HumanoidAppearanceComponent.Augments.cs)
- [`Content.Shared/_WG/Augments/HumanoidCharacterProfile.Augments.cs`](../../../Content.Shared/_WG/Augments/HumanoidCharacterProfile.Augments.cs)
- [`Content.Shared/_WG/Augments/SharedBodySystem.Augments.cs`](../../../Content.Shared/_WG/Augments/SharedBodySystem.Augments.cs)
- [`Content.Shared/_WG/Augments/SharedHumanoidAppearanceSystem.Augments.cs`](../../../Content.Shared/_WG/Augments/SharedHumanoidAppearanceSystem.Augments.cs)
- [`Content.Shared/_WG/Augments/SharedWGHandShieldSystem.cs`](../../../Content.Shared/_WG/Augments/SharedWGHandShieldSystem.cs)
- [`Content.Shared/_WG/Augments/WGAdrenalBoosterComponent.cs`](../../../Content.Shared/_WG/Augments/WGAdrenalBoosterComponent.cs)
- [`Content.Shared/_WG/Augments/WGApplyAugmentsEvent.cs`](../../../Content.Shared/_WG/Augments/WGApplyAugmentsEvent.cs)
- [`Content.Shared/_WG/Augments/WGAugmentChoice.cs`](../../../Content.Shared/_WG/Augments/WGAugmentChoice.cs)
- [`Content.Shared/_WG/Augments/WGAugmentPrototype.cs`](../../../Content.Shared/_WG/Augments/WGAugmentPrototype.cs)
- [`Content.Shared/_WG/Augments/WGAugmentRules.cs`](../../../Content.Shared/_WG/Augments/WGAugmentRules.cs)
- [`Content.Shared/_WG/Augments/WGAugmentSelector.cs`](../../../Content.Shared/_WG/Augments/WGAugmentSelector.cs)
- [`Content.Shared/_WG/Augments/WGAugmentShapePrototype.cs`](../../../Content.Shared/_WG/Augments/WGAugmentShapePrototype.cs)
- [`Content.Shared/_WG/Augments/WGAugmentSlot.cs`](../../../Content.Shared/_WG/Augments/WGAugmentSlot.cs)
- [`Content.Shared/_WG/Augments/WGAugmentStylePrototype.cs`](../../../Content.Shared/_WG/Augments/WGAugmentStylePrototype.cs)
- [`Content.Shared/_WG/Augments/WGAugmentTraitSlotsPrototype.cs`](../../../Content.Shared/_WG/Augments/WGAugmentTraitSlotsPrototype.cs)
- [`Content.Shared/_WG/Augments/WGBurstLauncherComponent.cs`](../../../Content.Shared/_WG/Augments/WGBurstLauncherComponent.cs)
- [`Content.Shared/_WG/Augments/WGDeployableImplantComponent.cs`](../../../Content.Shared/_WG/Augments/WGDeployableImplantComponent.cs)
- [`Content.Shared/_WG/Augments/WGHaloArmingSystem.cs`](../../../Content.Shared/_WG/Augments/WGHaloArmingSystem.cs)
- [`Content.Shared/_WG/Augments/WGHandShieldComponent.cs`](../../../Content.Shared/_WG/Augments/WGHandShieldComponent.cs)
- [`Content.Shared/_WG/Augments/WGHoloProjectorComponent.cs`](../../../Content.Shared/_WG/Augments/WGHoloProjectorComponent.cs)
- [`Content.Shared/_WG/Augments/WGIatricMonitorComponent.cs`](../../../Content.Shared/_WG/Augments/WGIatricMonitorComponent.cs)
- [`Content.Shared/_WG/Augments/WGNutrimentPumpComponent.cs`](../../../Content.Shared/_WG/Augments/WGNutrimentPumpComponent.cs)
- [`Content.Shared/_WG/Augments/WGOpticalCamoComponent.cs`](../../../Content.Shared/_WG/Augments/WGOpticalCamoComponent.cs)
- [`Content.Shared/_WG/Augments/WGPainEditorComponent.cs`](../../../Content.Shared/_WG/Augments/WGPainEditorComponent.cs)
- [`Content.Shared/_WG/Augments/WGPainEditorSystem.cs`](../../../Content.Shared/_WG/Augments/WGPainEditorSystem.cs)
- [`Content.Shared/_WG/Augments/WGReflexBoosterComponent.cs`](../../../Content.Shared/_WG/Augments/WGReflexBoosterComponent.cs)
- [`Content.Shared/_WG/Augments/WGReflexBoosterSystem.cs`](../../../Content.Shared/_WG/Augments/WGReflexBoosterSystem.cs)
- [`Content.Shared/_WG/Augments/WGReviverComponent.cs`](../../../Content.Shared/_WG/Augments/WGReviverComponent.cs)
- [`Content.Shared/_WG/Augments/WGShoulderCannonComponent.cs`](../../../Content.Shared/_WG/Augments/WGShoulderCannonComponent.cs)
- [`Content.Shared/_WG/Augments/WGSmartlinkComponent.cs`](../../../Content.Shared/_WG/Augments/WGSmartlinkComponent.cs)
- [`Content.Shared/_WG/Augments/WGSmartlinkSystem.cs`](../../../Content.Shared/_WG/Augments/WGSmartlinkSystem.cs)
- [`Content.Shared/_WG/Augments/WGThermalTintsComponent.cs`](../../../Content.Shared/_WG/Augments/WGThermalTintsComponent.cs)
- [`Content.Shared/_WG/Augments/WGThermalTintsSystem.cs`](../../../Content.Shared/_WG/Augments/WGThermalTintsSystem.cs)
- [`Content.Shared/_WG/Augments/WGTwinLaserComponent.cs`](../../../Content.Shared/_WG/Augments/WGTwinLaserComponent.cs)
- [`Content.Shared/_WG/Augments/WGTwinLaserSystem.cs`](../../../Content.Shared/_WG/Augments/WGTwinLaserSystem.cs)

### Client

- [`Content.Client/_WG/Augments/HumanoidProfileEditor.Augments.cs`](../../../Content.Client/_WG/Augments/HumanoidProfileEditor.Augments.cs)
- [`Content.Client/_WG/Augments/UI/WGAugmentBackdrop.cs`](../../../Content.Client/_WG/Augments/UI/WGAugmentBackdrop.cs)
- [`Content.Client/_WG/Augments/UI/WGAugmentCard.cs`](../../../Content.Client/_WG/Augments/UI/WGAugmentCard.cs)
- [`Content.Client/_WG/Augments/UI/WGAugmentDoll.cs`](../../../Content.Client/_WG/Augments/UI/WGAugmentDoll.cs)
- [`Content.Client/_WG/Augments/UI/WGAugmentEditor.cs`](../../../Content.Client/_WG/Augments/UI/WGAugmentEditor.cs)
- [`Content.Client/_WG/Augments/UI/WGAugmentIcon.cs`](../../../Content.Client/_WG/Augments/UI/WGAugmentIcon.cs)
- [`Content.Client/_WG/Augments/UI/WGAugmentMask.cs`](../../../Content.Client/_WG/Augments/UI/WGAugmentMask.cs)
- [`Content.Client/_WG/Augments/UI/WGAugmentScanList.cs`](../../../Content.Client/_WG/Augments/UI/WGAugmentScanList.cs)
- [`Content.Client/_WG/Augments/UI/WGAugmentStylePicker.cs`](../../../Content.Client/_WG/Augments/UI/WGAugmentStylePicker.cs)
- [`Content.Client/_WG/Augments/UI/WGAugmentTheme.cs`](../../../Content.Client/_WG/Augments/UI/WGAugmentTheme.cs)
- [`Content.Client/_WG/Augments/UI/WGDeployableWheel.cs`](../../../Content.Client/_WG/Augments/UI/WGDeployableWheel.cs)
- [`Content.Client/_WG/Augments/UI/WGSmartlinkOverlay.cs`](../../../Content.Client/_WG/Augments/UI/WGSmartlinkOverlay.cs)
- [`Content.Client/_WG/Augments/UI/WGThermalGlitchOverlay.cs`](../../../Content.Client/_WG/Augments/UI/WGThermalGlitchOverlay.cs)
- [`Content.Client/_WG/Augments/WGDeployableWheelSystem.cs`](../../../Content.Client/_WG/Augments/WGDeployableWheelSystem.cs)
- [`Content.Client/_WG/Augments/WGHandShieldSystem.cs`](../../../Content.Client/_WG/Augments/WGHandShieldSystem.cs)
- [`Content.Client/_WG/Augments/WGHoloDecoyVisualsSystem.cs`](../../../Content.Client/_WG/Augments/WGHoloDecoyVisualsSystem.cs)
- [`Content.Client/_WG/Augments/WGShoulderCannonVisualsSystem.cs`](../../../Content.Client/_WG/Augments/WGShoulderCannonVisualsSystem.cs)
- [`Content.Client/_WG/Augments/WGSmartlinkOverlaySystem.cs`](../../../Content.Client/_WG/Augments/WGSmartlinkOverlaySystem.cs)
- [`Content.Client/_WG/Augments/WGThermalTintsVisualsSystem.cs`](../../../Content.Client/_WG/Augments/WGThermalTintsVisualsSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentAdrenalBoosterTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentAdrenalBoosterTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentBodyTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentBodyTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentClothedTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentClothedTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentCombatImplantsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentCombatImplantsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentCyberChestTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentCyberChestTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentDropRetractTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentDropRetractTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentEditorTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentEditorTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentHaloBurstTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentHaloBurstTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentHoloProjectorTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentHoloProjectorTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentImplantsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentImplantsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentPersistenceTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentPersistenceTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentPreviewTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentPreviewTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentPurchaseTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentPurchaseTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentReflexBoosterTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentReflexBoosterTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentRespawnTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentRespawnTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentRulesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentRulesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentSpeciesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentSpeciesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentThermalEyesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentThermalEyesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentTraitLimbsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentTraitLimbsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentTraitOverlapTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentTraitOverlapTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Augments/AugmentVitalImplantsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Augments/AugmentVitalImplantsTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/Augments/augments.yml`](../../../Resources/Prototypes/_WG/Augments/augments.yml)
- [`Resources/Prototypes/_WG/Augments/base_sprites.yml`](../../../Resources/Prototypes/_WG/Augments/base_sprites.yml)
- [`Resources/Prototypes/_WG/Augments/implants.yml`](../../../Resources/Prototypes/_WG/Augments/implants.yml)
- [`Resources/Prototypes/_WG/Augments/loot.yml`](../../../Resources/Prototypes/_WG/Augments/loot.yml)
- [`Resources/Prototypes/_WG/Augments/parts.yml`](../../../Resources/Prototypes/_WG/Augments/parts.yml)
- [`Resources/Prototypes/_WG/Augments/shapes.yml`](../../../Resources/Prototypes/_WG/Augments/shapes.yml)
- [`Resources/Prototypes/_WG/Augments/styles.yml`](../../../Resources/Prototypes/_WG/Augments/styles.yml)
- [`Resources/Prototypes/_WG/Augments/trait_slots.yml`](../../../Resources/Prototypes/_WG/Augments/trait_slots.yml)

### Localization

- [`Resources/Locale/en-US/_WG/Augments/augments.ftl`](../../../Resources/Locale/en-US/_WG/Augments/augments.ftl)
- [`Resources/Locale/en-US/_WG/Augments/styles.ftl`](../../../Resources/Locale/en-US/_WG/Augments/styles.ftl)

### Textures

- [`Resources/Textures/_WG/Augments/advanced.rsi/`](../../../Resources/Textures/_WG/Augments/advanced.rsi/)
- [`Resources/Textures/_WG/Augments/arm_laser.rsi/`](../../../Resources/Textures/_WG/Augments/arm_laser.rsi/)
- [`Resources/Textures/_WG/Augments/bishop.rsi/`](../../../Resources/Textures/_WG/Augments/bishop.rsi/)
- [`Resources/Textures/_WG/Augments/bishop2.rsi/`](../../../Resources/Textures/_WG/Augments/bishop2.rsi/)
- [`Resources/Textures/_WG/Augments/cyborg.rsi/`](../../../Resources/Textures/_WG/Augments/cyborg.rsi/)
- [`Resources/Textures/_WG/Augments/e3n.rsi/`](../../../Resources/Textures/_WG/Augments/e3n.rsi/)
- [`Resources/Textures/_WG/Augments/eebishop.rsi/`](../../../Resources/Textures/_WG/Augments/eebishop.rsi/)
- [`Resources/Textures/_WG/Augments/eehephaestus.rsi/`](../../../Resources/Textures/_WG/Augments/eehephaestus.rsi/)
- [`Resources/Textures/_WG/Augments/eemorpheus.rsi/`](../../../Resources/Textures/_WG/Augments/eemorpheus.rsi/)
- [`Resources/Textures/_WG/Augments/eeshellguard.rsi/`](../../../Resources/Textures/_WG/Augments/eeshellguard.rsi/)
- [`Resources/Textures/_WG/Augments/eewardtakahashi.rsi/`](../../../Resources/Textures/_WG/Augments/eewardtakahashi.rsi/)
- [`Resources/Textures/_WG/Augments/eexion.rsi/`](../../../Resources/Textures/_WG/Augments/eexion.rsi/)
- [`Resources/Textures/_WG/Augments/eezenghu.rsi/`](../../../Resources/Textures/_WG/Augments/eezenghu.rsi/)
- [`Resources/Textures/_WG/Augments/engineering.rsi/`](../../../Resources/Textures/_WG/Augments/engineering.rsi/)
- [`Resources/Textures/_WG/Augments/halo_orb.rsi/`](../../../Resources/Textures/_WG/Augments/halo_orb.rsi/)
- [`Resources/Textures/_WG/Augments/halo_shield.rsi/`](../../../Resources/Textures/_WG/Augments/halo_shield.rsi/)
- [`Resources/Textures/_WG/Augments/hephaestus.rsi/`](../../../Resources/Textures/_WG/Augments/hephaestus.rsi/)
- [`Resources/Textures/_WG/Augments/hephaestus2.rsi/`](../../../Resources/Textures/_WG/Augments/hephaestus2.rsi/)
- [`Resources/Textures/_WG/Augments/holo_projector.rsi/`](../../../Resources/Textures/_WG/Augments/holo_projector.rsi/)
- [`Resources/Textures/_WG/Augments/implants.rsi/`](../../../Resources/Textures/_WG/Augments/implants.rsi/)
- [`Resources/Textures/_WG/Augments/mariinsky.rsi/`](../../../Resources/Textures/_WG/Augments/mariinsky.rsi/)
- [`Resources/Textures/_WG/Augments/mining.rsi/`](../../../Resources/Textures/_WG/Augments/mining.rsi/)
- [`Resources/Textures/_WG/Augments/morpheus.rsi/`](../../../Resources/Textures/_WG/Augments/morpheus.rsi/)
- [`Resources/Textures/_WG/Augments/reflex_booster.rsi/`](../../../Resources/Textures/_WG/Augments/reflex_booster.rsi/)
- [`Resources/Textures/_WG/Augments/scrappy.rsi/`](../../../Resources/Textures/_WG/Augments/scrappy.rsi/)
- [`Resources/Textures/_WG/Augments/scrappytinted.rsi/`](../../../Resources/Textures/_WG/Augments/scrappytinted.rsi/)
- [`Resources/Textures/_WG/Augments/security.rsi/`](../../../Resources/Textures/_WG/Augments/security.rsi/)
- [`Resources/Textures/_WG/Augments/shellguard.rsi/`](../../../Resources/Textures/_WG/Augments/shellguard.rsi/)
- [`Resources/Textures/_WG/Augments/shoulder_cannon.rsi/`](../../../Resources/Textures/_WG/Augments/shoulder_cannon.rsi/)
- [`Resources/Textures/_WG/Augments/surplus.rsi/`](../../../Resources/Textures/_WG/Augments/surplus.rsi/)
- [`Resources/Textures/_WG/Augments/tool_arm.rsi/`](../../../Resources/Textures/_WG/Augments/tool_arm.rsi/)
- [`Resources/Textures/_WG/Augments/wardtakahashi.rsi/`](../../../Resources/Textures/_WG/Augments/wardtakahashi.rsi/)
- [`Resources/Textures/_WG/Augments/xion.rsi/`](../../../Resources/Textures/_WG/Augments/xion.rsi/)
- [`Resources/Textures/_WG/Augments/xion2.rsi/`](../../../Resources/Textures/_WG/Augments/xion2.rsi/)
- [`Resources/Textures/_WG/Augments/zenghu.rsi/`](../../../Resources/Textures/_WG/Augments/zenghu.rsi/)
- [`Resources/Textures/_WG/Augments/zhenkov.rsi/`](../../../Resources/Textures/_WG/Augments/zhenkov.rsi/)
- [`Resources/Textures/_WG/Augments/zhenkovdark.rsi/`](../../../Resources/Textures/_WG/Augments/zhenkovdark.rsi/)

### Tools

- [`Tools/_WG/Augments/build_halo_sprites.py`](../../../Tools/_WG/Augments/build_halo_sprites.py)
- [`Tools/_WG/Augments/port.py`](../../../Tools/_WG/Augments/port.py)

## Non-modular edits

- [`Content.Client/Humanoid/HumanoidAppearanceSystem.cs`](../../../Content.Client/Humanoid/HumanoidAppearanceSystem.cs)
  - the doll shows the picked augments, client-side only
  - an augment's layer takes no markings
- [`Content.Client/Lobby/UI/HumanoidProfileEditor.xaml`](../../../Content.Client/Lobby/UI/HumanoidProfileEditor.xaml)
  - the wga xmlns on the root tag below is for the augment editor.
  - augments tab (xmlns:wga), after anatomy so earlier tabs keep their indices
- [`Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs`](../../../Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs)
  - a trait with its own limbs claims those slots
  - a trait with its own limbs takes those slots
  - augments the new species can't take are dropped
- [`Content.Server.Database/Model.cs`](../../../Content.Server.Database/Model.cs): augments picked per slot (slot:augment[:style] entries) and priced ones paid for.
- [`Content.Server/_Mono/Traits/Physical/BionicLegsSystem.cs`](../../_Mono/Traits/Physical/BionicLegsSystem.cs)
  - drawn to the species' own leg shape
  - cut the old leg loose like an augment swap, so the body keeps its leg markings
- [`Content.Server/_Mono/Traits/Physical/PrybarProstheticsSystem.cs`](../../_Mono/Traits/Physical/PrybarProstheticsSystem.cs)
  - drawn to the species' own arm shape
  - cut the old arm loose like an augment swap, so the body keeps its arm markings
- [`Content.Server/_NF/Commands/GhostRespawnCommand.cs`](../../_NF/Commands/GhostRespawnCommand.cs): respawning instead of cryo loses the character's augments
- [`Content.Server/Database/ServerDbBase.cs`](../../Database/ServerDbBase.cs)
- [`Content.Server/Medical/HealthAnalyzerSystem.cs`](../../Medical/HealthAnalyzerSystem.cs): the cyberware in the body
- [`Content.Server/Preferences/Managers/ServerPreferencesManager.cs`](../../Preferences/Managers/ServerPreferencesManager.cs)
  - the balance the client last saw, to tell a stale copy
  - a client save can't unlock augments; new priced ones are bought from the bank here
  - the creator shows the balance, unlocks and dropped augments the save left
- [`Content.Shared/_Goobstation/Autosurgeon/AutoSurgeonSystem.cs`](../../../Content.Shared/_Goobstation/Autosurgeon/AutoSurgeonSystem.cs): augments reapply and detect upgrades
- [`Content.Shared/Body/Systems/SharedBodySystem.Parts.cs`](../../../Content.Shared/Body/Systems/SharedBodySystem.Parts.cs): a part cut loose for an augment isn't lost
- [`Content.Shared/Damage/Systems/StaminaSystem.cs`](../../../Content.Shared/Damage/Systems/StaminaSystem.cs)
  - who dealt it and with what, for the hand shield
  - source and weapon, for the hand shield
- [`Content.Shared/Humanoid/SharedHumanoidAppearanceSystem.cs`](../../../Content.Shared/Humanoid/SharedHumanoidAppearanceSystem.cs): swaps in the augmented parts and draws their styles
- [`Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs`](../../../Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs): the cyberware in the body, for the organs tab
- [`Content.Shared/Preferences/HumanoidCharacterProfile.cs`](../../../Content.Shared/Preferences/HumanoidCharacterProfile.cs): augments the species can't take are dropped
- [`Resources/Prototypes/_Goobstation/Entities/Objects/Weapons/Melee/mantis_blade.yml`](../../../Resources/Prototypes/_Goobstation/Entities/Objects/Weapons/Melee/mantis_blade.yml)
  - was 14; one blade stays under an energy sword's DPS, a pair beats it
  - was 7
- [`Resources/Prototypes/_NF/Entities/World/Debris/wrecks.yml`](../../../Resources/Prototypes/_NF/Entities/World/Debris/wrecks.yml): scavenged cyberware, the dearer the rarer

<!-- WOLFGATE-GENERATED END -->
