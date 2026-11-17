wg-round-setup-row-recruiting = Recruiting
wg-round-setup-row-recruiting-tooltip = This crew is looking for members.

wg-round-setup-scenario-formed = Formed Crew
wg-round-setup-formed-description = A crew formed mid-round around characters already playing. Nobody respawns or changes gear. Pick up a ship from a member's deed under Your ship.
wg-round-setup-formed-create = Form a crew
wg-round-setup-formed-create-help = You're already in the round, so the crew forms around your current character. Add openings on the Crew page, then invite players or post for crew.
wg-round-setup-formed-no-ship = This crew has no ship to spawn on yet.

wg-round-setup-live-join-help = You join with the character you're playing now. Your ID, job and gear stay as they are; you get the crew radio (:k) and crew ship access.
wg-round-setup-live-unavailable = Characters already in the round can only join Formed crews and launched Ship crews.

wg-round-setup-recruiting = Recruiting
wg-round-setup-recruiting-help = Recruiting crews sit at the top of every crew list with a pulsing border.
wg-round-setup-recruiting-toggle = Looking for crew
wg-round-setup-recruiting-hidden = Not shown yet. Recruiting needs Public or Request access, late joining on, and a free slot in an opening.

wg-round-setup-posting = Post for crew
wg-round-setup-posting-help = Send an in-character open invitation to every player who isn't in a crew. Turns recruiting on. Once every 10 minutes.
wg-round-setup-posting-placeholder = e.g. Salvage run to the debris field, need two hands and a medic.
wg-round-setup-posting-send = Post
wg-round-setup-posting-wait = You posted recently.
wg-round-setup-posting-cooldown = You can post again in { $minutes } { $minutes ->
    [one] minute
   *[other] minutes
}.
wg-round-setup-posting-invalid = Write a posting of up to 200 characters.
wg-round-setup-posting-too-soon = You posted recently. Wait before posting again.
wg-round-setup-posting-closed = Nobody can join right now. Use Public or Request access, allow late joining and add a free opening first.
wg-round-setup-posting-sent = Posting sent.
wg-round-setup-posting-chat = [color=#7FD3C4][bold]Crew Board:[/bold] { $crew } ({ $openings } open), posted by { $host }:[/color] { $message }

wg-round-setup-posting-card-title = Crew posting
wg-round-setup-posting-card-crew = { $crew } · { $difficulty } · { $openings } open
wg-round-setup-posting-card-message = { $host }: "{ $message }"
wg-round-setup-posting-card-view = View
wg-round-setup-posting-card-join-help = Open this crew in Crew Setup to pick a role and join.
wg-round-setup-posting-card-request-help = Open this crew in Crew Setup to pick a role and ask to join.
wg-round-setup-posting-card-dismiss = Dismiss
wg-round-setup-posting-card-mute = Mute
wg-round-setup-posting-card-mute-help = Stop receiving crew postings. Set wg.round_setup.show_postings to true to get them back.

cmd-crewunpost-desc = Withdraws a crew's posting and stops it recruiting.
cmd-crewunpost-help = Usage: crewunpost <crew id>. The id is in the admin log entry for the posting.
cmd-crewunpost-invalid = Give one crew id.
cmd-crewunpost-missing = No crew with id { $id }.
cmd-crewunpost-done = Withdrew crew { $id }'s posting.
