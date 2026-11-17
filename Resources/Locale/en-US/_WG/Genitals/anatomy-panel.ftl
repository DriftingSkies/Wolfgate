# In-game Anatomy panel (AnatomyUIController, AnatomyWindow). Clinical wording; arguments are localised words or numbers.

wg-anatomy-window-title = Anatomy
wg-anatomy-button-tooltip = Open the anatomy panel.
ui-options-function-open-anatomy-panel = Open anatomy panel

## Settings
wg-anatomy-reveal = Reveal anatomy
wg-anatomy-reveal-undergarments = When undergarments are removed
wg-anatomy-reveal-clothing = When clothing is removed
wg-anatomy-reveal-clothing-tooltip = Anatomy is drawn over undergarments once clothing is removed.
wg-anatomy-undergarments = Undergarments
wg-anatomy-undergarment-top = Top
wg-anatomy-undergarment-bottom = Bottom
wg-anatomy-undergarment-worn = Worn
wg-anatomy-undergarment-removed = Removed
wg-anatomy-undergarment-none = None worn
wg-anatomy-strip-consent = Others may remove my undergarments
wg-anatomy-strip-consent-tooltip = Changes this consent setting directly. Turning it off puts back anything others removed.
wg-anatomy-toggle-off = Off
wg-anatomy-toggle-on = On

## Arousal
wg-anatomy-arousal = Arousal
wg-anatomy-arousal-value = { $value } ({ $state })
wg-anatomy-arousal-state-none = none
wg-anatomy-arousal-state-partial = partial
wg-anatomy-arousal-state-full = full
wg-anatomy-arousal-snap-none = None
wg-anatomy-arousal-snap-partial = Partial
wg-anatomy-arousal-snap-full = Full

## Organ rows
wg-anatomy-row-penis = Penis: { $shape }, about { $length } cm (erect){ $sheath ->
    [sheath] , sheath
    [slit] , slit
   *[none] {""}
}
wg-anatomy-row-penis-plain = Penis
wg-anatomy-row-testicles = Testicles: external, { $size }
wg-anatomy-row-testicles-internal = Testicles: internal
wg-anatomy-row-vagina = Vulva: { $shape }
wg-anatomy-row-womb = Womb: internal
wg-anatomy-row-breasts = Breasts: { $shape }, { $cup }{ $lactating ->
    [yes] , lactating
   *[no] {""}
}
wg-anatomy-shape-unknown = unspecified
wg-anatomy-visibility = Visibility
wg-anatomy-visibility-normal = Normal
wg-anatomy-visibility-hidden = Always hidden
wg-anatomy-visibility-through = Show through clothing
wg-anatomy-status = Status: { $status }
wg-anatomy-status-exposed = exposed
wg-anatomy-status-covered = covered by { $item }
wg-anatomy-status-covered-generic = covered
wg-anatomy-status-undergarment = covered by undergarment
wg-anatomy-status-hidden = always hidden
wg-anatomy-status-through = shown through clothing

## Empty states and gates
wg-anatomy-no-organs = No external anatomy.
wg-anatomy-unavailable = Anatomy is not available for this body.
wg-anatomy-gate-consent = The anatomy panel is part of adult content. Turn on Adult content in consent settings to use it.

## Footer
wg-anatomy-save-defaults = Save as character defaults
wg-anatomy-save-defaults-tooltip = Saves the current reveal mode and visibility settings to the selected character.
wg-anatomy-save-defaults-unavailable = Only available while the selected character is the one you are playing.
wg-anatomy-consent-settings = Consent settings

## One-time notice for accounts with Adult content on
wg-anatomy-notice-title = Adult content
wg-anatomy-notice-text = Adult content now also covers the anatomy panel, anatomy sprites and anatomy examine descriptions.
    Other players can only remove your undergarments or perform anatomy surgery on you if you also turn on those separate consent toggles. They are off unless you enable them.
wg-anatomy-notice-open-consent = Open consent settings
wg-anatomy-notice-ok = OK
