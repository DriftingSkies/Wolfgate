# Synthetics: chassis limbs put back on by hand (WGSyntheticLimbSystem).
wg-synthetic-limb-reattaching = You start coupling { THE($limb) } back on.
wg-synthetic-limb-reattaching-other = { CAPITALIZE(THE($user)) } starts coupling { THE($limb) } onto you.
wg-synthetic-limb-reattached = { CAPITALIZE(THE($limb)) } clicks into place, loosely.
wg-synthetic-limb-not-chassis = That will not couple to a chassis.
wg-synthetic-limb-no-socket = There is no free socket for that.
wg-synthetic-limb-examine-loose = [color=yellow]{ CAPITALIZE(POSS-ADJ($target)) } { $part } sits loose in its socket. Cable coil on it will recouple it.[/color]
wg-synthetic-limb-coupling-tightened = { CAPITALIZE(THE($limb)) }'s coupling tightens. { $passes ->
    [one] One more pass of cable.
   *[other] About { $passes } more passes of cable.
}
wg-synthetic-limb-coupling-seated = { CAPITALIZE(THE($limb)) } seats firmly in its socket.

# The wound a hand reattach leaves (wounds.yml).
wg-synthetics-wound-name-loose-coupling = loose coupling
wg-synthetics-wound-stage-loose = loose
wg-synthetics-look-loose-coupling = sitting crooked and loose in its socket
wg-synthetics-look-loose-coupling-short = loose in socket

# The synthetic readout (hud_lines.yml).
wg-synthetic-line-loose = LOOSE COUPLING
wg-synthetic-advice-loose = RECOUPLE: CABLE COIL
