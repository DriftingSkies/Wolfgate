ent-WGNeurowareStick = data stick
    .desc = A small data stick with an unlabelled port and a sampling pin. This one looks blank.

ent-WGNeurowareStickWhiteline = data stick (Whiteline)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WGNeurowareStickNullLoop = data stick (Null Loop)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WGNeurowareStickBurst = data stick (Burst)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WGNeurowareStickRedline = data stick (Redline)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WGNeurowareStickDamper = data stick (Damper)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WGNeurowareStickChoir = data stick (Choir.sig)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WGNeurowareStickHalo = data stick (Halo)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WGNeurowareStickIdle = data stick (Idle)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

wg-neuroware-stick-blank = The stick is blank.
wg-neuroware-stick-spent = The stick has no loads left.
wg-neuroware-stick-examine = It holds [color=violet]{ $program }[/color] at { $integrity }% integrity, with { $loads ->
    [one] one load
   *[other] { $loads } loads
} left.
wg-neuroware-no-port = { CAPITALIZE(THE($target)) } has no port for this.
wg-neuroware-load-target = { CAPITALIZE(THE($user)) } is plugging something into your maintenance jack!
wg-neuroware-copy-done = You copy the program onto the blank stick.
wg-neuroware-copy-fail = You need a loaded stick and a blank one.
wg-neuroware-encode-done = The stick reads { THE($source) } in and writes it down as { $program }.
wg-neuroware-encode-short = There is too little in { THE($source) } for the stick to read.

## Synthetic HUD lines

wg-neuroware-advice = A panel reboot clears running software.
wg-neuroware-crash-fault = POST-RUN THROTTLE ACTIVE
wg-neuroware-habit-loop = HABIT LOOP: process not found
wg-neuroware-false-fault-1 = SECTOR 0 CHECKSUM MISMATCH
wg-neuroware-false-fault-2 = COOLANT LOOP RUPTURE
wg-neuroware-false-fault-3 = MOTOR CORTEX UNRESPONSIVE
