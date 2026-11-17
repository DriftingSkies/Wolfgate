ent-WGNukeKey = warhead key
    .desc = A heavy, numbered arming key on a lanyard, cut for one warhead. Without it the warhead won't arm.
ent-WGWarheadTacticalKeyed = tactical nuclear device
    .desc = A faction-procured sub-kiloton warhead on a hand cart. It won't arm without its key in the keyhole. A screwed-down panel covers two fuze wires.
ent-WGOrdnanceBeaconTactical = nuclear delivery beacon
    .desc = Calls down a tactical nuclear device by drop pod. Set it up, plant it on a deck or open ground, and stand clear.
ent-WGOrdnanceTerminalBase = ordnance terminal
    .desc = Procures strategic ordnance for the faction's command. Every purchase is announced to the sector.
ent-WGOrdnanceTerminalPDV = { ent-WGOrdnanceTerminalBase }
    .desc = { ent-WGOrdnanceTerminalBase.desc }
ent-WGOrdnanceTerminalTSF = { ent-WGOrdnanceTerminalBase }
    .desc = { ent-WGOrdnanceTerminalBase.desc }
ent-WGOrdnanceTerminalUSSP = { ent-WGOrdnanceTerminalBase }
    .desc = { ent-WGOrdnanceTerminalBase.desc }
ent-WGOrdnanceTerminalUnderground = { ent-WGOrdnanceTerminalBase }
    .desc = { ent-WGOrdnanceTerminalBase.desc }
ent-WGOrdnanceTerminalMieyo = { ent-WGOrdnanceTerminalBase }
    .desc = { ent-WGOrdnanceTerminalBase.desc }

wg-ordnance-faction-pdv = The PDV
wg-ordnance-faction-tsf = The TSFMC
wg-ordnance-faction-ussp = The USSP
wg-ordnance-faction-underground = The Underground
wg-ordnance-faction-mieyo = Mieyo
wg-ordnance-faction-unknown = An unknown buyer

wg-ordnance-announcement-sender = Sector Arms Control
wg-ordnance-purchase-announcement = {$faction} has bought a {$device}. A nuclear weapon is now loose in the sector.

wg-ordnance-defcon-armed = {$count ->
    [one] A nuclear device is armed
   *[other] {$count} nuclear devices are armed
}
wg-ordnance-defcon-stock = {$count ->
    [one] A nuclear device is held in the sector
   *[other] {$count} nuclear devices are held in the sector
}

wg-ordnance-key-slot = Key

wg-ordnance-fuze-timer = timer
wg-ordnance-fuze-remote = remote, with a dead-man switch
wg-ordnance-fuze-both = timer and remote, with a dead-man switch
wg-ordnance-verb-fuze = Fuze: {$fuze}
wg-ordnance-verb-timer = Warhead timer: {$seconds} s
wg-ordnance-verb-delay = Drop delay: {$seconds} s
wg-ordnance-beacon-examine = Set up for a [color=yellow]{$fuze}[/color] fuze, a {$timer} s timer, landing {$delay} s after it is planted.
wg-ordnance-beacon-planted = Beacon planted. The pod lands in {$seconds} seconds.
wg-ordnance-beacon-inbound = Something heavy is screaming down towards the beacon.
wg-ordnance-beacon-no-ground = It needs a deck or the ground to stand on.
wg-ordnance-beacon-central = Caelestinus Central jams the beacon.
