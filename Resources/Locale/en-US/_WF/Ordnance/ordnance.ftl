ent-WFNukeKey = warhead key
    .desc = A heavy, numbered arming key on a lanyard, cut for one warhead. Without it the warhead won't arm.
ent-WFWarheadTacticalKeyed = tactical nuclear device
    .desc = A faction-procured sub-kiloton warhead on a hand cart. It won't arm without its key in the keyhole.
ent-WFOrdnanceBeaconTactical = nuclear delivery beacon
    .desc = Calls down a tactical nuclear device by drop pod. Set it up, plant it on a deck or open ground, and stand clear.
ent-WFOrdnanceTerminalBase = ordnance terminal
    .desc = Procures strategic ordnance for the faction's command. Every purchase is announced to the sector.
ent-WFOrdnanceTerminalPDV = { ent-WFOrdnanceTerminalBase }
    .desc = { ent-WFOrdnanceTerminalBase.desc }
ent-WFOrdnanceTerminalTSF = { ent-WFOrdnanceTerminalBase }
    .desc = { ent-WFOrdnanceTerminalBase.desc }
ent-WFOrdnanceTerminalUSSP = { ent-WFOrdnanceTerminalBase }
    .desc = { ent-WFOrdnanceTerminalBase.desc }
ent-WFOrdnanceTerminalUnderground = { ent-WFOrdnanceTerminalBase }
    .desc = { ent-WFOrdnanceTerminalBase.desc }
ent-WFOrdnanceTerminalMieyo = { ent-WFOrdnanceTerminalBase }
    .desc = { ent-WFOrdnanceTerminalBase.desc }

wf-ordnance-faction-pdv = The PDV
wf-ordnance-faction-tsf = The TSFMC
wf-ordnance-faction-ussp = The USSP
wf-ordnance-faction-underground = The Underground
wf-ordnance-faction-mieyo = Mieyo
wf-ordnance-faction-unknown = An unknown buyer

wf-ordnance-announcement-sender = Sector Arms Control
wf-ordnance-purchase-announcement = {$faction} has bought a {$device}. A nuclear weapon is now loose in the sector.

wf-ordnance-defcon-armed = {$count ->
    [one] A nuclear device is armed
   *[other] {$count} nuclear devices are armed
}
wf-ordnance-defcon-stock = {$count ->
    [one] A nuclear device is held in the sector
   *[other] {$count} nuclear devices are held in the sector
}

wf-ordnance-key-slot = Key

wf-ordnance-fuze-timer = timer
wf-ordnance-fuze-remote = remote, with a dead-man switch
wf-ordnance-fuze-both = timer and remote, with a dead-man switch
wf-ordnance-verb-fuze = Fuze: {$fuze}
wf-ordnance-verb-timer = Warhead timer: {$seconds} s
wf-ordnance-verb-delay = Drop delay: {$seconds} s
wf-ordnance-beacon-examine = Set up for a [color=yellow]{$fuze}[/color] fuze, a {$timer} s timer, landing {$delay} s after it is planted.
wf-ordnance-beacon-planted = Beacon planted. The pod lands in {$seconds} seconds.
wf-ordnance-beacon-inbound = Something heavy is screaming down towards the beacon.
wf-ordnance-beacon-no-ground = It needs a deck or the ground to stand on.
wf-ordnance-beacon-central = Caelestinus Central jams the beacon.
