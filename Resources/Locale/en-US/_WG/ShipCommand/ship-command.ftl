cmd-wg_shipzone-desc = Lists or switches the zones of a commanded ship.
cmd-wg_shipzone-help = Usage: {$command} list <grid|here> | set <grid|here> <zone|all> <public|staff|locked|sealed>
cmd-wg_shipzone-line = {$id} ({$name}): {$mode}, {$doors} doors
cmd-wg_shipzone-none = This ship has no zones.
cmd-wg_shipzone-set = {$count ->
        [one] 1 zone switched to {$mode}.
       *[other] {$count} zones switched to {$mode}.
    }
cmd-wg_shipzone-bad-mode = The mode must be public, staff, locked or sealed.
cmd-wg_shipzone-bad-zone = This ship has no zone {$zone}.
cmd-wg_shipzone-hint-sub = Subcommand
cmd-wg_ship-bad-grid = That is not an entity id.
cmd-wg_ship-not-on-grid = You are not standing on a grid.
cmd-wg_ship-not-commanded = That entity is not a commanded ship, or has no zones.
cmd-wg_ship-hint-grid = Grid entity id, or here
cmd-wg_shipcrew-desc = Lists and staffs the crew roster of a commanded ship.
cmd-wg_shipcrew-help = Usage: {$command} list <grid|here> | staff <grid|here> [slot|all] | down|up <grid|here> <slot> | auto <grid|here> on|off
cmd-wg_shipcrew-auto = Automatic restaffing: {$auto}, at most {$cap} an hour, {$delay} seconds after a loss.
cmd-wg_shipcrew-line = {$slot} ({$role}): {$state ->
        [held] held by {$who}
        [player] held by {$who}, a player
        [down] stood down
       *[empty] empty
    }
cmd-wg_shipcrew-staffed = {$count ->
        [one] 1 post staffed.
       *[other] {$count} posts staffed.
    }
cmd-wg_shipcrew-auto-set = Automatic restaffing is now {$auto ->
        [true] on
       *[false] off
    }.
cmd-wg_shipcrew-no-slot = There is no post {$slot}.
cmd-wg_shipcrew-hint-sub = Subcommand
cmd-wg_shipcrew-hint-auto = on or off
cmd-wg_shipcrew-hint-slot = Post id, or all
cmd-wg_shipprice-desc = Lists and edits the price rules of a commanded ship.
cmd-wg_shipprice-help = Usage: {$command} list <grid|here> | add <grid|here> <name> <everyone|staff|officers|company:<id>> <services,...|all> <multiplier> | toggle <grid|here> <name|owner|admins> | remove <grid|here> <name>
cmd-wg_shipprice-free = Owner served free: {$owner}. Admins served free: {$admins}.
cmd-wg_shipprice-line = {$name}: {$audience}, {$services}, x{$multiplier}, enabled {$enabled}
cmd-wg_shipprice-bad-rule = The audience must be everyone, staff, officers or company:<id>; services a comma list or all; the multiplier from 0 to 10.
cmd-wg_shipprice-added = Rule {$name} set.
cmd-wg_shipprice-toggled = {$name} is now {$enabled ->
        [true] on
       *[false] off
    }.
cmd-wg_shipprice-removed = Rule {$name} removed.
cmd-wg_shipprice-no-rule = There is no rule {$name}.
cmd-wg_shipprice-hint-sub = Subcommand
cmd-wg_shipprice-hint-name = Rule name
cmd-wg_shipprice-hint-audience = Who it is for
cmd-wg_shipprice-hint-services = Services, comma separated, or all
cmd-wg_shipprice-hint-multiplier = Multiplier: 0 free, 1 usual, above 1 dearer
cmd-wg_shipzone-hint-zone = Zone id, or all
cmd-wg_shipzone-hint-mode = Mode
cmd-wg_ship-not-yours = Only the ship's owner, or an admin while the owner allows it, can change its settings.
cmd-wg_shipcrew-stood = Post {$slot} is now {$down ->
        [true] stood down
       *[false] stood up
    }.
cmd-wg_shipjobs-desc = Lists, opens and closes the player jobs of a commanded ship.
cmd-wg_shipjobs-help = Usage: {$command} list <grid|here> | open <grid|here> <job> <count> | close <grid|here> <job|all> | release <grid|here> <name>
cmd-wg_shipjobs-line = {$job}: {$open} open; held by {$held}
cmd-wg_shipjobs-set = {$job} now has {$count} open slots.
cmd-wg_shipjobs-closed-all = Every job is closed.
cmd-wg_shipjobs-no-job = This ship has no job {$job}.
cmd-wg_shipjobs-no-holder = Nobody called {$who} holds one of this ship's jobs.
cmd-wg_shipjobs-released = {$who} was released from their job; the post goes back to the roster.
cmd-wg_shipjobs-hint-sub = Subcommand
cmd-wg_shipjobs-hint-job = Job id, or all
cmd-wg_shipjobs-hint-who = The holder's character name
cmd-wg_shipjobs-hint-count = Open slots, 0 to 10
cmd-wg_shippreset-desc = Lists, saves, loads and deletes your own presets for a commanded ship.
cmd-wg_shippreset-help = Usage: {$command} list <grid|here> | save <grid|here> <name> | load <grid|here> <name> | delete <grid|here> <name>
cmd-wg_shippreset-players-only = Presets belong to players; run this from the game.
cmd-wg_shippreset-no-vessel = This ship has no vessel design to keep presets under.
cmd-wg_shippreset-bad-name = A preset name is letters, digits, dashes and underscores, up to {$max} long.
cmd-wg_shippreset-none = You have no presets for {$vessel}.
cmd-wg_shippreset-line = {$name} (saved {$saved})
cmd-wg_shippreset-saved = Saved preset {$name} for {$vessel}.
cmd-wg_shippreset-loaded = Loaded preset {$name}.
cmd-wg_shippreset-deleted = Deleted preset {$name}.
cmd-wg_shippreset-missing = You have no preset called {$name} for this ship.
cmd-wg_shippreset-full = You already keep {$max} presets for {$vessel}, as many as you can. Delete one first, or save over one by its name.
cmd-wg_shippreset-failed = The preset couldn't be read or written; see the server log.
cmd-wg_shippreset-hint-sub = Subcommand
cmd-wg_shippreset-hint-name = Preset name
wg-shipcommand-preset-applied = Applied ship preset {$name}.
wg-shipcommand-preset-missing = No ship preset called {$name} was found; the ship keeps its mapped settings.
wg-shipcommand-relieved-1 = Relieved by {$name}. The post's yours.
wg-shipcommand-relieved-2 = {$name}, you have the post. I'm off watch.
wg-shipcommand-relieved-3 = All yours, {$name}. Mind it well.
wg-shipcommand-stood-down = Standing down. Off watch.
wg-shipcommand-wires-staff-only = Only this ship's staff may open this panel.
wg-shipcommand-emag-staff-only = The emag sparks against this ship's security and does nothing.
wg-shipcommand-bottomless-no-eject = This fabricator's stock is for printing only. It won't come out as sheets.
wg-shipcommand-bottomless-print-examine = Printed from a bottomless fabricator's stock. No buyer will touch it.
