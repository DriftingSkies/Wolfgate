# Anatomy examine lines (GenitalExamineSystem). Clinical wording; every argument is a localised word or a number.
# Selectors: $shape is "none" when the shape has no examine word; $aroused, $lactating and $plural are "yes" or "no".

## Another body: one line per exposed organ. The womb and internal testicles are never described.
wg-genitals-examine-penis = { CAPITALIZE(POSS-ADJ($target)) } penis is exposed: { $shape }, approximately { $length } cm when erect, { $state }.
wg-genitals-examine-penis-noshape = { CAPITALIZE(POSS-ADJ($target)) } penis is exposed: approximately { $length } cm when erect, { $state }.
wg-genitals-examine-penis-sheathed = { CAPITALIZE(POSS-ADJ($target)) } genital { $sheath } is exposed; the penis is { $state }.
wg-genitals-examine-testicles = { CAPITALIZE(POSS-ADJ($target)) } testicles are exposed: { $size }.
wg-genitals-examine-vagina = { CAPITALIZE(POSS-ADJ($target)) } vulva is exposed{ $shape ->
    [none] {""}
   *[other] : { $shape }
}{ $aroused ->
    [yes] , visibly aroused
   *[no] {""}
}.
wg-genitals-examine-breasts = { CAPITALIZE(POSS-ADJ($target)) } breasts are exposed: { $shape }, { $cup }{ $lactating ->
    [yes] , lactating
   *[no] {""}
}.
wg-genitals-examine-breasts-noshape = { CAPITALIZE(POSS-ADJ($target)) } breasts are exposed: { $cup }{ $lactating ->
    [yes] , lactating
   *[no] {""}
}.

## Self-examine: the same lines with "Your", plus organs that others cannot see.
wg-genitals-examine-self-penis = Your penis is exposed: { $shape }, approximately { $length } cm when erect, { $state }.
wg-genitals-examine-self-penis-noshape = Your penis is exposed: approximately { $length } cm when erect, { $state }.
wg-genitals-examine-self-penis-sheathed = Your genital { $sheath } is exposed; the penis is { $state }.
wg-genitals-examine-self-testicles = Your testicles are exposed: { $size }.
wg-genitals-examine-self-vagina = Your vulva is exposed{ $shape ->
    [none] {""}
   *[other] : { $shape }
}{ $aroused ->
    [yes] , visibly aroused
   *[no] {""}
}.
wg-genitals-examine-self-breasts = Your breasts are exposed: { $shape }, { $cup }{ $lactating ->
    [yes] , lactating
   *[no] {""}
}.
wg-genitals-examine-self-breasts-noshape = Your breasts are exposed: { $cup }{ $lactating ->
    [yes] , lactating
   *[no] {""}
}.
wg-genitals-examine-self-covered = Your { $organ } { $plural ->
    [yes] are
   *[no] is
} covered.
wg-genitals-examine-self-hidden = Your { $organ } { $plural ->
    [yes] are
   *[no] is
} hidden from others by your visibility setting.
wg-genitals-examine-organ-penis = penis
wg-genitals-examine-organ-testicles = testicles
wg-genitals-examine-organ-vagina = vulva
wg-genitals-examine-organ-breasts = breasts

## Arousal, sheath, cup and size words.
wg-genitals-state-flaccid = flaccid
wg-genitals-state-partial = partially erect
wg-genitals-state-erect = erect
wg-genitals-sheath-state-retracted = retracted
wg-genitals-sheath-state-partial = partly extended
wg-genitals-sheath-state-full = extended and erect
wg-genitals-sheath-word-sheath = sheath
wg-genitals-sheath-word-slit = slit
wg-genitals-cup-letter = cup { $letter }
wg-genitals-cup-oversize = oversize grade { $grade }
wg-genitals-testicles-size-1 = small
wg-genitals-testicles-size-2 = average-sized
wg-genitals-testicles-size-3 = large
wg-genitals-testicles-size-4 = very large
wg-genitals-testicles-size-5 = exceptionally large

## Loose organs, described by the server only. Otherwise the item is only "organ tissue".
wg-genitals-organ-examine-penis = It is a penis: { $shape }, approximately { $length } cm when erect.
wg-genitals-organ-examine-penis-noshape = It is a penis, approximately { $length } cm when erect.
wg-genitals-organ-examine-penis-plain = It is a penis.
wg-genitals-organ-examine-testicles = It is a pair of testicles: { $size }.
wg-genitals-organ-examine-testicles-plain = It is a pair of testicles.
wg-genitals-organ-examine-vagina = It is a vulva and vagina{ $shape ->
    [none] {""}
   *[other] : { $shape }
}.
wg-genitals-organ-examine-womb = It is a womb.
wg-genitals-organ-examine-breasts = It is breast tissue: { $shape }, { $cup }.
wg-genitals-organ-examine-breasts-noshape = It is breast tissue: { $cup }.
