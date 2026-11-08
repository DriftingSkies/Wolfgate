wf-poi-unknown-signal = Unknown signal
wf-poi-signal-identified = Signal identified: { $name }.

wf-poi-asclepiu-research = Overgrown research station
wf-poi-asclepiu-clinic = Derelict clinic
wf-poi-asclepiu-dorms = Abandoned dormitory
wf-poi-merak-mineshaft = Sand-choked mineshaft
wf-poi-merak-outpost = Abandoned salvage outpost
wf-poi-fervidus-brig = Scorched brig
wf-poi-fervidus-camp = Mercenary camp
wf-poi-thrascias-labs = Frozen laboratory
wf-poi-thrascias-virology = Iced-over virology lab
wf-poi-aerumna-factory = Collapsed cave factory
wf-poi-aerumna-mine = Chromite mine
wf-poi-carcinoma-ruin = Flesh-choked ruin
wf-poi-verda-gas-station = Roadside gas station
wf-poi-verda-house-1 = Roadside house
wf-poi-mechanic-shop = Mechanic's garage
wf-poi-roadside-inn = Roadside inn
wf-poi-planetary-hospital = Hospital
wf-poi-verda-corner-house = Run-down house
wf-poi-verda-small-house = Small house
wf-poi-abandoned-bandit-hideout = Bandit hideout
wf-poi-planetary-vault = Planetary vault
wf-poi-atalis-house = Small house
wf-poi-greenhouse = Greenhouse
wf-poi-ancient-temple = Ancient temple
wf-poi-wreck = Crashed ship
wf-poi-reaver-encampment = Reaver encampment
wf-poi-ads-control-site = ADS control site
wf-poi-crazy-camp = Camp
wf-poi-xeno-hive = Xenonid hive

wf-poi-type-mine = Mine
wf-poi-type-abandoned-outpost = Abandoned outpost
wf-poi-type-small-derelict = Small derelict
wf-poi-type-automated-outpost = Automated outpost
wf-poi-type-tradepost = Tradepost
wf-poi-type-derelict-capital = Derelict capital ship
wf-poi-type-farm = Farm
wf-poi-type-workshop = Workshop
wf-poi-type-ruins = Ruins
wf-poi-type-heavenly-shrine = Heavenly shrine
wf-poi-type-alien-artifact-site = Alien artifact site

wf-poi-derelict-capital = Crashed capital ship
wf-poi-automated-outpost = Automated outpost
wf-poi-alien-artifact-site = Dig site

wf-poi-core-examine-active = Its status lights blink steadily: the outpost's turrets still answer to it.
wf-poi-core-examine-hacked = Its status lights are dark: it commands nothing any more.
wf-poi-core-hacked = { $count ->
    [0] The core goes dark. Nothing answered to it.
    [one] The core goes dark, and a turret stands down.
   *[other] The core goes dark, and { $count } turrets stand down.
}

wf-poi-survey-nothing = The sweep finds nothing within { $range } metres.
wf-poi-survey-found = { $count ->
    [0] No new signals.
    [one] One faint signal now shows on radars.
   *[other] { $count } faint signals now show on radars.
} The nearest unknown site is { $distance } metres { $direction }.
wf-poi-clue-points = The notes describe { $site }, about { $distance } metres { $direction }. It now shows on radars.
wf-poi-clue-read = You have already worked out where these notes point.
wf-poi-clue-nothing = The notes describe places that have all been found already.

wf-poi-survey-dir-north = north
wf-poi-survey-dir-northeast = northeast
wf-poi-survey-dir-east = east
wf-poi-survey-dir-southeast = southeast
wf-poi-survey-dir-south = south
wf-poi-survey-dir-southwest = southwest
wf-poi-survey-dir-west = west
wf-poi-survey-dir-northwest = northwest
