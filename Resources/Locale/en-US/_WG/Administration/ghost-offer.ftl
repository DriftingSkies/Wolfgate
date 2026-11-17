# Wolfgate admin: offer a mob or ghost role to every ghost

## Verb
wg-admin-verbs-offer-to-ghosts = Offer to Ghosts
wg-admin-verbs-offer-to-ghosts-description = Makes this a ghost role if it isn't one and asks every ghost whether they want it.

## Server messages
wg-ghost-offer-default-description = An admin is offering control of {$name}.
wg-ghost-offer-sent = Offered {$name} to {$count} {$count ->
    [one] ghost
    *[other] ghosts
}.
wg-ghost-offer-error-deleted = That entity no longer exists.
wg-ghost-offer-error-has-player = {$entity} already has a player.
wg-ghost-offer-error-unavailable = The ghost role on {$entity} is taken or not available.

## Ghost sign-up prompt
wg-ghost-offer-prompt-title = Ghost Role Offer
wg-ghost-offer-prompt-raffle = Signing up enters you into a short draw.
wg-ghost-offer-prompt-first-come = The first ghost to sign up takes it.
wg-ghost-offer-prompt-sign-up = Sign up
wg-ghost-offer-prompt-follow = Follow
wg-ghost-offer-prompt-all-roles = All ghost roles
wg-ghost-offer-signup-joined = You're in the draw for {$name}.
wg-ghost-offer-signup-taken = You took {$name}.
wg-ghost-offer-signup-already = You've already signed up.
wg-ghost-offer-signup-failed = You can't sign up for {$name}.
wg-ghost-offer-signup-not-ghost = Only ghosts can sign up.
wg-ghost-offer-signup-closed = This offer has closed.

## offertoghosts command
cmd-offertoghosts-desc = Makes an entity a ghost role if it isn't one and asks every current ghost whether they want it.
cmd-offertoghosts-help = Usage: {$command} <entity> [name] [description] [rules]
    Blank texts keep the ghost role's own, or the entity's name and a default description for a new one.
cmd-offertoghosts-hint-entity = <entity>
cmd-offertoghosts-hint-name = [name]
cmd-offertoghosts-hint-description = [description]
cmd-offertoghosts-hint-rules = [rules]
cmd-offertoghosts-invalid-args = Expected an entity and up to three texts.
cmd-offertoghosts-invalid-entity = {$entity} is not a valid entity.
