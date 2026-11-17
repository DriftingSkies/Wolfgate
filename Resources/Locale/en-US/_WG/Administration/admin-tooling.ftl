# Wolfgate Admin Tooling

## Admin menu tab
admin-menu-wolfgate-tab = Wolfgate
wg-admin-tab-spawn-vessel = Spawn Vessel

## Vessel spawn window
wg-vessel-spawn-title = Spawn Vessel
wg-vessel-spawn-search = Search by name, ID or class...
wg-vessel-spawn-size-label = Size:{" "}
wg-vessel-spawn-category-label = Category:{" "}
wg-vessel-spawn-owner-label = Owner:{" "}
wg-vessel-spawn-owner-none = None
wg-vessel-spawn-name-placeholder = Ship name (optional)
wg-vessel-spawn-none = None
wg-vessel-spawn-button = Spawn
wg-vessel-spawn-count = Showing {$shown} of {$total} vessels
wg-vessel-spawn-none-selected = No vessel selected
wg-vessel-spawn-tooltip-none = Select a vessel to spawn it.
wg-vessel-spawn-tooltip-denied = You do not have permission to spawn vessels.

wg-vessel-spawn-detail-id = ID
wg-vessel-spawn-detail-category = Category
wg-vessel-spawn-detail-classes = Classes
wg-vessel-spawn-detail-size = Size
wg-vessel-spawn-detail-engines = Engines
wg-vessel-spawn-detail-price = Price
wg-vessel-spawn-detail-grid = Grid
wg-vessel-spawn-grid-value = {$width} x {$height} ({$count} tiles)

# Category filter and grouped-list headers
wg-vessel-spawn-category-all = All
wg-vessel-spawn-category-Salvage = Salvage
wg-vessel-spawn-category-Science = Science
wg-vessel-spawn-category-CargoEngineering = Cargo & Engineering
wg-vessel-spawn-category-Medical = Medical
wg-vessel-spawn-category-Civilian = Civilian & Service
wg-vessel-spawn-category-Expedition = Expedition
wg-vessel-spawn-category-Escort = Pursuit & Escort
wg-vessel-spawn-category-Security = Security
wg-vessel-spawn-category-Military = Military
wg-vessel-spawn-category-Antagonist = Antagonist
wg-vessel-spawn-category-Other = Other
wg-vessel-spawn-category-AdminOnly = Admin only

# Frontier defines the other shipyard class names but not this one.
shipyard-console-class-Mercenary = Mercenary

## spawnvessel command
cmd-spawnvessel-desc = Spawns a vessel prototype's grid at your current position with its late-join job slots, optionally registering it to a player (their account, and the ID card they carry as its deed) and naming it.
cmd-spawnvessel-help = Usage: {$command} <vessel ID> [owner username, or - for none] [ship name] [preset:<name>, a commanded ship's preset to apply]
cmd-spawnvessel-hint = <vessel ID>
cmd-spawnvessel-owner-hint = [owner username, or - for none]
cmd-spawnvessel-name-hint = [ship name]
cmd-spawnvessel-name-too-long = Ship names can be at most {$max} characters.
cmd-spawnvessel-invalid-args = Expected one or two arguments.
cmd-spawnvessel-no-entity = You need an entity in the world to spawn a vessel at.
cmd-spawnvessel-no-map = You are not on a map.
cmd-spawnvessel-unknown-vessel = No vessel prototype with ID "{$id}".
cmd-spawnvessel-failed = Failed to spawn vessel "{$id}". Check the server log.
cmd-spawnvessel-success = Spawned {$name} (entity {$uid}).
cmd-spawnvessel-owner-not-found = No connected player named "{$name}".
cmd-spawnvessel-owner-no-entity = {$name} has no body to hold an ID card.
cmd-spawnvessel-owner-ghost = {$name} is a ghost, and a ghost's ID card doesn't last.
cmd-spawnvessel-owner-no-id = {$name} has no ID card on them.
cmd-spawnvessel-owner-has-deed = {$name}'s ID card already holds a ship deed.
cmd-spawnvessel-owner-assigned = Deed assigned to {$name}, and the ship registered to their account.
cmd-spawnvessel-owner-registered = Registered to {$name}'s account, with no deed card: {$reason} Its access follows them into any body.
cmd-spawnvessel-owner-failed = Could not assign the deed to {$name}.

## renamevessel command
cmd-renamevessel-desc = Renames a vessel: its grid, its late-join station and any deeds for it.
cmd-renamevessel-help = Usage: {$command} <grid entity> <new name>
cmd-renamevessel-grid-hint = <grid entity>
cmd-renamevessel-name-hint = <new name>
cmd-renamevessel-invalid-args = Expected a grid entity and a name.
cmd-renamevessel-not-grid = "{$uid}" is not a grid.
cmd-renamevessel-bad-name = Ship names need 1 to {$max} characters.
cmd-renamevessel-failed = Could not rename the vessel. Check the server log.
cmd-renamevessel-success = Vessel renamed to {$name}.

## Spawn as Outfit verb and window
wg-admin-verbs-spawn-outfit = Spawn as Outfit...
wg-admin-verbs-spawn-outfit-description = Spawns a humanoid wearing a chosen outfit at this entity, optionally taking control of it.
wg-spawn-outfit-title = Spawn as Outfit
wg-spawn-outfit-search = Search by job or gear ID...
wg-spawn-outfit-category-label = Category:{" "}
wg-spawn-outfit-category-all = All
wg-spawn-outfit-category-antagonists = Antagonists
wg-spawn-outfit-category-other = Other
wg-spawn-outfit-count = Showing {$shown} of {$total} outfits
wg-spawn-outfit-none-selected = No outfit selected
wg-spawn-outfit-contents = Contents
wg-spawn-outfit-in-hand = In hand
wg-spawn-outfit-storage = Storage ({$slot})
wg-spawn-outfit-body-label = Body:{" "}
wg-spawn-outfit-body-Random = Random human
wg-spawn-outfit-body-Default = Default human (bald John Doe)
wg-spawn-outfit-body-Own = My selected character
wg-spawn-outfit-control = Take control of the spawned mob
wg-spawn-outfit-confirm = Spawn
# Ghost mode (Ctrl+click a ghost as an admin)
wg-spawn-outfit-ghost-title = Spawn Ghost as Outfit
wg-spawn-outfit-ghost-info = Spawns [bold]{$name}[/bold]'s selected character wearing this outfit at their ghost and puts them in control.
wg-spawn-outfit-ghost-confirm = Spawn {$name}
# Set mode (the Set Outfit debug verb and setoutfit with only an entity)
wg-spawn-outfit-set-title = Set Outfit
wg-spawn-outfit-set-info = Dresses [bold]{$name}[/bold] in this outfit. Everything they currently wear drops to the floor.
wg-spawn-outfit-set-confirm = Apply to {$name}

## spawnoutfit command
cmd-spawnoutfit-desc = Spawns a humanoid wearing a starting gear outfit at the target entity.
cmd-spawnoutfit-help = Usage: {$command} <target entity> <starting gear ID> [take control: true/false] [body: Random/Default/Own]
cmd-spawnoutfit-hint-target = <target entity>
cmd-spawnoutfit-hint-gear = <starting gear ID>
cmd-spawnoutfit-hint-control = [true/false]
cmd-spawnoutfit-hint-body = [Random/Default/Own]
cmd-spawnoutfit-invalid-args = Expected two to four arguments.
cmd-spawnoutfit-no-player = Only players can run this command.
cmd-spawnoutfit-invalid-target = Invalid target entity.
cmd-spawnoutfit-unknown-gear = No starting gear prototype with ID "{$id}".
cmd-spawnoutfit-invalid-control = The take-control argument must be true or false.
cmd-spawnoutfit-invalid-body = The body argument must be Random, Default or Own.
cmd-spawnoutfit-control-forbidden = Taking control needs the Fun admin flag.
cmd-spawnoutfit-success = Spawned {$mob} wearing {$gear}.

## spawnoutfitui command (client)
cmd-spawnoutfitui-desc = Opens the Spawn as Outfit picker for a target entity.
cmd-spawnoutfitui-help = Usage: {$command} <target entity>
cmd-spawnoutfitui-invalid-args = Expected a target entity ID.

## Planet control
wg-admin-tab-planet-control = Planet Control
