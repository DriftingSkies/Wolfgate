# The ventilator, and what a body on one (or sealed in an autodoc) reads as.

wolfmed-ventilator-slot-tank = Gas tank

wolfmed-ventilator-verb-attach = Fit the mask to { $target }
wolfmed-ventilator-verb-detach = Take the mask off

wolfmed-ventilator-attach-begin = { CAPITALIZE(THE($user)) } begins fitting the mask of { THE($ventilator) } to { THE($target) }...
wolfmed-ventilator-attached = { CAPITALIZE(THE($ventilator)) } is breathing for { THE($target) }.
wolfmed-ventilator-detached = The mask of { THE($ventilator) } comes off { THE($target) }.
wolfmed-ventilator-pulled-off = The mask of { THE($ventilator) } is pulled off { THE($target) }!
wolfmed-ventilator-cannot = { CAPITALIZE(THE($target)) } has nothing to ventilate.
wolfmed-ventilator-too-far = { CAPITALIZE(THE($target)) } is too far from { THE($ventilator) }.
wolfmed-ventilator-alarm = { CAPITALIZE(THE($ventilator)) } beeps loudly: no gas!

wolfmed-ventilator-examine-tank = It has { INDEFINITE($tank) } { $tank } fitted, at { $pressure } kPa.
wolfmed-ventilator-examine-no-tank = It has no gas tank fitted.
wolfmed-ventilator-examine-not-connected = The mask hangs on the stand.
wolfmed-ventilator-examine-breathing = It is breathing for { THE($target) }.
wolfmed-ventilator-examine-dry = [color=red]The mask is on { THE($target) }, and no gas is coming.[/color]

# The analyzer's vitals line, examine and the patient's own card.
wolfmed-vitals-breathing-ventilated-none = Ventilated
wolfmed-vitals-breathing-ventilated-lungs = Ventilated: no working lungs
wolfmed-vitals-breathing-ventilated-lungsdamaged = Ventilated: lungs damaged
wolfmed-vitals-breathing-ventilated-sedation = Ventilated: sedation
wolfmed-vitals-breathing-ventilated-noair = Ventilated: no air
wolfmed-look-ventilated-self = A machine is breathing for you.
wolfmed-look-ventilated-other = A machine is breathing for { THE($target) }.
wolfmed-card-breathing-ventilated = A machine is breathing for you

# "Do first" for a body with no lungs. A restarted heart stops again within a minute with nothing breathing for it.
wolfmed-vitals-aid-nolungs = ventilator, autodoc or CPR, then new lungs
wolfmed-vitals-aid-nolungs-arrest = CPR, ventilator on, then the defibrillator
wolfmed-vitals-aid-nolungs-after = new lungs
wolfmed-vitals-aid-nolungs-ventilated = new lungs, keep them ventilated
