# Wolfgate ERT Builder

## Window
wg-admin-tab-ert = ERT Builder
wg-ert-title = ERT Builder
wg-ert-info = Spawns a ship and one ghost role per member aboard it. Players keep their own character if its species is allowed; otherwise they get a random allowed species.
wg-ert-section-team = Team
wg-ert-team-name = Name
wg-ert-team-name-placeholder = e.g. Rescue Team
wg-ert-team-name-default = Emergency Response Team
wg-ert-briefing = Briefing
wg-ert-briefing-placeholder = Shown to ghosts when they pick the role.
wg-ert-members = Members
wg-ert-leader = First member is the leader
wg-ert-section-outfits = Outfits
wg-ert-member-outfit = Members
wg-ert-leader-outfit = Leader
wg-ert-outfit-search = Search outfits...
wg-ert-same-as-members = Same as members
wg-ert-section-ids = ID cards
wg-ert-leader-title = Leader title
wg-ert-member-title = Member title
wg-ert-keep-access = Keep the outfit ID's own access
wg-ert-access-hint = Extra access for every ID:
wg-ert-section-species = Allowed species
wg-ert-species-hint = Leave all unticked to allow any species.
wg-ert-generic-humans = Make everyone a default bald male human with a random name
wg-ert-section-ship = Ship
wg-ert-ship-search = Search ships...
wg-ert-no-ship = No ship (spawn at my position)
wg-ert-spawn = Spawn ERT

## Server messages
wg-ert-default-leader-title = {$team} Leader
wg-ert-default-member-title = {$team} Responder
wg-ert-default-briefing = You are part of the {$team}. Follow your leader and the admins' instructions.
wg-ert-spawned = Spawned the {$team} with {$count} ghost roles and asked {$ghosts} ghosts to sign up.
wg-ert-error-no-name = Give the team a name.
wg-ert-error-members = Members must be between 1 and {$max}.
wg-ert-error-outfit = Unknown outfit "{$id}".
wg-ert-error-vessel = Unknown ship "{$id}".
wg-ert-error-vessel-failed = Couldn't spawn the ship. Check the server log.
wg-ert-error-no-entity = You need a body or ghost in the world to spawn a team at.
wg-ert-error-no-map = You aren't on a map.

## Ghost sign-up prompt
wg-ert-prompt-title = Emergency Response
wg-ert-prompt-places = {$count} places. Signing up enters you into a short draw.
wg-ert-prompt-sign-up = Sign up
wg-ert-prompt-sign-up-leader = Sign up as leader
wg-ert-prompt-all-roles = All ghost roles
wg-ert-signup-joined = You're in the draw for {$title}.
wg-ert-signup-already = You've already signed up for this team.
wg-ert-signup-full = No member places are left.
wg-ert-signup-no-leader = The leader place is already taken.
wg-ert-signup-not-ghost = Only ghosts can sign up.
wg-ert-signup-closed = This team has no places left.

## Commands
cmd-ertbuilder-desc = Opens the ERT Builder.
cmd-ertbuilder-help = Usage: {$command}
cmd-ertbuilder-no-player = Only players can open the ERT Builder.
cmd-ertbuilderui-desc = Opens the ERT Builder window.
cmd-ertbuilderui-help = Usage: {$command}
