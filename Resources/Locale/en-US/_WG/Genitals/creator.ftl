# Creator anatomy tab (WolfgateGenitalEditor). Clinical wording.
# Cup and testicle size words are shared with examine.ftl (wg-genitals-cup-*, wg-genitals-testicles-size-N).

wg-genitals-tab = Anatomy

## Gates, shown instead of the controls.
wg-genitals-gate-disabled = Anatomy options are turned off on this server.
wg-genitals-gate-offline = Anatomy options depend on your consent settings, which are only available while connected to a server.
wg-genitals-gate-consent = Anatomy options are part of adult content. Turn on Adult content in consent settings to use them.
wg-genitals-gate-age = Anatomy options are only available for characters aged { $age } or older.
wg-genitals-gate-species = This species has no configurable anatomy.
wg-genitals-open-consent = Open consent settings

## Preview. Only changes the creator's preview, never the character.
wg-genitals-preview = Preview
wg-genitals-preview-worn = As worn
wg-genitals-preview-underwear = Underwear only
wg-genitals-preview-nude = Unclothed
wg-genitals-preview-arousal = Arousal preview
wg-genitals-preview-arousal-none = None
wg-genitals-preview-arousal-partial = Partial
wg-genitals-preview-arousal-full = Full

## Typical anatomy.
wg-genitals-preset-menu = Typical anatomy ({ $species })
wg-genitals-preset-male = Typical male
wg-genitals-preset-female = Typical female
wg-genitals-preset-clear = Clear
wg-genitals-preset-suggested = Matches this character's sex.
wg-genitals-preset-confirm = Replace the current anatomy settings?
wg-genitals-preset-apply = Replace
wg-genitals-preset-cancel = Cancel

## Reveal mode and undergarments.
wg-genitals-reveal = Reveal anatomy when
wg-genitals-reveal-undergarments = Undergarments are removed
wg-genitals-reveal-clothing = Clothing is removed
wg-genitals-reveal-clothing-tooltip = Anatomy is drawn over undergarments once clothing is removed.
wg-genitals-no-undergarments = No undergarments selected: anatomy shows as soon as clothing is removed.
wg-genitals-no-undergarments-species = This species has no undergarments, so nothing covers anatomy once clothing is removed. Consider the Always hidden visibility.
wg-genitals-choose-undergarments = Choose undergarments

## Organ cards.
wg-genitals-include = Include
wg-genitals-shape = Shape
wg-genitals-penis = Penis
wg-genitals-length = Length
wg-genitals-length-value = { $cm } cm (size { $step })
wg-genitals-sheath = Sheath
wg-genitals-sheath-none = None
wg-genitals-sheath-sheath = Sheath
wg-genitals-sheath-slit = Slit
wg-genitals-sheath-unavailable = This shape does not allow this option.
wg-genitals-sheath-colour = Sheath colour
wg-genitals-testicles = Testicles
wg-genitals-testicles-type = Type
wg-genitals-testicles-none = None
wg-genitals-testicles-external = External
wg-genitals-testicles-internal = Internal
wg-genitals-testicles-internal-note = Internal testicles are neither drawn nor described.
wg-genitals-testicle-size = Size
wg-genitals-vagina = Vagina
wg-genitals-womb = Womb
wg-genitals-womb-present = Present (internal)
wg-genitals-breasts = Breasts
wg-genitals-cup = Cup
wg-genitals-lactation = Lactation
wg-genitals-lactating = Lactating
wg-genitals-udders-note = Size does not change the sprite of this shape.
wg-genitals-colour = Colour
wg-genitals-match-skin = Match skin tone
wg-genitals-visibility = Visibility
wg-genitals-visibility-normal = Normal
wg-genitals-visibility-normal-tooltip = Shown when not covered by clothing or undergarments.
wg-genitals-visibility-hidden = Always hidden
wg-genitals-visibility-hidden-tooltip = Never drawn or described to others. Can be changed during a round.
wg-genitals-visibility-through = Show through clothing
wg-genitals-visibility-through-tooltip = Drawn over clothing. Can be changed during a round.
wg-genitals-load-failed = Stored anatomy settings could not be fully read. They are kept unless you change them.

## Save confirmation for characters that cannot have anatomy.
wg-genitals-save-confirm-title = Anatomy settings
wg-genitals-save-confirm-text = Saving at this age removes the anatomy settings.
wg-genitals-save-confirm-save = Save
wg-genitals-save-confirm-cancel = Cancel

## Warnings for the validator issues the creator shows (GenitalIssue); the age, species and load gates have their own lines.
wg-genitals-warning-UnknownShape = A stored shape no longer exists, so that organ was removed.
wg-genitals-warning-WrongSlotShape = A stored shape does not fit its organ, so that organ was removed.
wg-genitals-warning-SheathNotAllowed = This shape does not allow the chosen sheath, so none is used. It returns when a shape that allows it is picked.
wg-genitals-warning-TesticlesWithoutPenis = Testicles need a penis, so they were removed.
wg-genitals-warning-WombWithoutVagina = A womb needs a vagina, so it was removed.
wg-genitals-warning-SizeClamped = The size was out of range and has been adjusted.
wg-genitals-warning-CupClamped = The cup size was out of range and has been adjusted.
wg-genitals-warning-LengthClamped = The length was out of range and has been adjusted.
wg-genitals-warning-InvalidValue = Some stored values were invalid and have been reset.
wg-genitals-warning-PregnancyMarkingWithoutWomb = A pregnancy marking is selected, but no womb is configured.

wg-genitals-nsfw-text = Adult description
wg-genitals-nsfw-text-hint = Shown from the heart on your examine card, only to adult characters whose players have adult content on, and only while you have it on too.
wg-genitals-nsfw-text-placeholder = What others notice that the normal description leaves out.
wg-genitals-nsfw-text-counter = {$count} / {$max}
