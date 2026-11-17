# Wolfgate admin: offer a player a second chance in their body

## Verb
wg-admin-verbs-second-chance = Offer Second Chance
wg-admin-verbs-second-chance-description = Asks this body's player whether they want to come back. If they say yes they are put back in it and fully healed.

## Server messages
wg-second-chance-sent = Offered {$player} a second chance as {$name}.
wg-second-chance-error-deleted = That entity no longer exists.
wg-second-chance-error-no-player = No connected player with a body was found for {$entity}.
wg-second-chance-error-occupied = {$entity} is being played by someone else.
wg-second-chance-accepted = {$player} accepted their second chance.
wg-second-chance-declined = {$player} declined their second chance.
wg-second-chance-failed = {$player} accepted their second chance, but the body is gone or taken.

## Player prompt
wg-second-chance-prompt-title = Second Chance
wg-second-chance-prompt-message = An admin is offering you a second chance. Return to [bold]{$name}[/bold], fully healed?
wg-second-chance-prompt-yes = Yes
wg-second-chance-prompt-no = No

## secondchance command
cmd-secondchance-desc = Asks a body's player whether they want to return to it. If they say yes they are put back in it and fully healed.
cmd-secondchance-help = Usage: {$command} <entity>
    The entity is the body, or the player's ghost.
cmd-secondchance-hint-entity = <entity>
cmd-secondchance-invalid-args = Expected one entity.
cmd-secondchance-invalid-entity = {$entity} is not a valid entity.
