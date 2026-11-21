wg-farm-reason-growing = [color=#6FCF6F]It is growing.[/color]
wg-farm-reason-night = [color=#E0B04F]It rests through the night.[/color]
wg-farm-reason-dry = [color=#E05A4F]It needs water to grow.[/color]
wg-farm-reason-cold = [color=#E05A4F]It is too cold outside for this crop. A roofed greenhouse with a grow lamp would let it grow.[/color]
wg-farm-reason-hot = [color=#E05A4F]It is too hot outside for this crop. A roofed greenhouse with a grow lamp would let it grow.[/color]
wg-farm-quality = It is of {$quality ->
    [silver] [color=#C0C0C0]silver[/color]
    [gold] [color=#FFD700]gold[/color]
    [iridium] [color=#B57EDC]iridium[/color]
   *[other] normal
} quality.

wg-farm-fertilized = You work {THE($item)} into the soil.
wg-farm-fertilizer-already = The soil already holds fertilizer as good as this.
wg-farm-fertilizer-examine = Its soil is fertilized{$quality ->
    [0] {""}
   *[other] {" "}for quality (+{$quality})
}{$speed ->
    [0] {""}
   *[other] {" "}for speed (+{$speed}%)
}{$retain ->
    [0] {""}
   *[other] {" "}with retaining soil
}.

wg-process-busy = It is already working on something. Wait until it is done.
wg-process-queue-full = Its queue is full. Wait until it works through some of it.
wg-process-queued = You set it aside to go next. {$count} {$count ->
    [one] item waits
   *[other] items wait
} their turn.
wg-process-queue = {$count} more {$count ->
    [one] item waits
   *[other] items wait
} their turn.
wg-process-no-seeds = There are no seeds to be had from that.
wg-process-started = You put {$item} in.
wg-process-done = {CAPITALIZE(THE($machine))} finishes its batch.
wg-process-empty = It is empty. Put something in to start a batch.
wg-process-working = It is working on {$item}: {$percent}% done.
wg-process-working-auto = It is working: {$percent}% done.
wg-process-idle-plants = [color=#E0B04F]It needs at least two crops growing within five tiles.[/color]

wg-order-none = No orders are posted right now. Check back soon.
wg-order-line = {$name}: {$filled}/{$count} delivered, {$price} spesos each and {$bonus} more when filled. {$minutes} minutes left.
wg-order-delivered = Delivered {$count} {$name} for {$pay} spesos. {$left} still wanted.
wg-order-filled = Delivered {$count} {$name} and filled the order: {$pay} spesos paid.
wg-order-no-account = The buyer pays into a bank account, and you don't have one.
wg-order-name-fruit = fruit
wg-order-name-vegetables = vegetables
wg-order-name-wheat = wheat
wg-order-name-eggs = eggs
wg-order-name-wine = bottles of wine
wg-order-name-beer = bottles of beer
wg-order-name-preserves = jars of preserves or pickles
wg-order-name-cheese = wheels of cheese
wg-order-name-honey = jars of honey
wg-order-name-cloth = cloth
wg-order-name-fertilizer = bags of fertilizer
wg-order-name-ledgers = encrypted ledgers
wg-order-name-mushrooms = mushrooms

wg-blueprint-category-farming = Farming
wg-blueprint-group-farm-plots = Fields and watering
wg-blueprint-group-farm-machines = Farm machines
wg-blueprint-group-farm-industry = Industry

wg-farm-reason-pecked = [color=#E05A4F]A bird has been pecking at it. A scarecrow would keep them off.[/color]
wg-farm-pecked = A bird swoops down and pecks at the crop.
wg-process-needs = It needs {$item} loaded first.
wg-process-stocked = It now holds {$count} {$item}.
wg-process-stock-full = It can't hold any more of that.
wg-process-stock = It holds {$count} {$item}.
wg-stack-glass-bottle = glass bottle

wg-process-loaded = It holds {$count} of the {$batch} it needs.
wg-process-loading-other = It is part-way through packing something else. Finish that crate first, or empty it out.
wg-process-unload-verb = Empty it out
wg-process-unloaded = You take back what was loaded into it.
wg-process-batch-started = That fills it. It gets to work.
wg-order-name-fine-dishes = cakes, pies or quiches
wg-order-name-home-cooking = home-cooked dishes or bread
wg-order-name-caviar = caviar or pearls
wg-water-collector-running = It is gathering water at {$percent}% of its best.
wg-water-collector-idle-rain = It is dry. It only fills while it rains on it.
wg-water-collector-idle-vapour = It isn't gathering anything. It needs power and air above freezing.
wg-water-collector-idle-snow = It isn't gathering anything. It needs power, and snow or frost.
wg-order-name-fish = fresh fish
wg-order-name-shellfish = shellfish
wg-process-idle-anchor = [color=#E0B04F]It needs to be anchored to work.[/color]
wg-process-idle-power = [color=#E0B04F]It has no power.[/color]
cmd-wghomesteadtest-desc = Builds a test ground with every farming, cooking, fishing, foraging and hunting feature, and sends you there.
cmd-wghomesteadtest-help = Usage: wghomesteadtest [all|kitchen|farm|machines|fishing|hunting] [surface]
cmd-wghomesteadtest-hint = section to build
cmd-wghomesteadtest-no-body = You need to be in a body to go to the test ground.
cmd-wghomesteadtest-bad-section = Unknown section. Try one of: {$sections}.
cmd-wghomesteadtest-done = Built the {$section} homestead test ground on {$surface}. Machines need no power here.
wg-order-name-pelts = pelts
wg-order-name-jerky = bundles of jerky
cmd-wghomesteadtest-surface-hint = planet surface (default WGSurfaceAsclepiu)
cmd-wghomesteadtest-bad-surface = There is no planet surface called {$surface}.
cmd-wghomesteadtest-no-world = The test world couldn't be built. Planet networks may be off (wg.planet_networks).
cmd-wghomesteadtest-world-name = Homestead test world
cmd-wghomesteadtest-waiting = Building the {$surface} test world. You'll be sent there once it has finished generating (up to two minutes).

guide-entry-wg-farming = Farming
