# Wolfgate admin internet sounds

## Radio popup
wg-internet-sound-popup-title = Admin Radio
wg-internet-sound-popup-played = Played by {$admin}
wg-internet-sound-popup-played-stealth = Now playing
wg-internet-sound-popup-loading = Tuning in... (from {$admin})
wg-internet-sound-popup-loading-stealth = Tuning in...
wg-internet-sound-popup-volume = Volume
wg-internet-sound-popup-stop = Mute

## Admin window
wg-admin-tab-internet-sound = Play Internet Sound
wg-internet-sound-admin-title = Play Internet Sound
wg-internet-sound-admin-info = Plays a YouTube, SoundCloud or other link to every connected player, or out of one ship's PA speakers.
wg-internet-sound-admin-url = https://...
wg-internet-sound-admin-play = Play
wg-internet-sound-admin-stealth-play = Stealth Play
wg-internet-sound-admin-stealth-play-tooltip = Plays without showing anyone, other admins included, who played it. The admin logs still name you.
wg-internet-sound-admin-stealth-requester = an unnamed admin
wg-internet-sound-admin-stop = Stop for everyone
wg-internet-sound-admin-pa = Over ship PA
wg-internet-sound-admin-grid = Grid entity id
wg-internet-sound-admin-pa-hint = The track plays from the ship's speakers, so it carries, fades with distance and distorts with damage.
wg-internet-sound-admin-need-grid = Enter the grid entity id of the ship to play it on.

## Status messages
wg-internet-sound-server-name = The server
wg-internet-sound-disabled = Internet sounds are disabled on this server.
wg-internet-sound-invalid-url = Use an HTTPS link on the standard port (443).
wg-internet-sound-fetching = Fetching {$url}...
wg-internet-sound-sending = Sending "{$title}" ({$size} MB) to {$count} players. It starts once they all have it.
wg-internet-sound-playing = Playing "{$title}" to everyone.
wg-internet-sound-playing-pa = Playing "{$title}" over the PA of {$ship}.
wg-internet-sound-stopped = Internet sound stopped for everyone.
wg-internet-sound-busy = "{$title}" is still playing. Stop it before playing something else.
wg-internet-sound-busy-fetching = A sound is still being fetched. Stop it before playing something else.
wg-internet-sound-busy-sending = "{$title}" is still being sent out. Stop it before playing something else.
wg-internet-sound-no-ship = That isn't a ship, or it's already gone.
wg-internet-sound-no-source = The speaker is gone.
wg-internet-sound-error-playlist-empty = Couldn't read that playlist. {$detail}
wg-internet-sound-no-speakers = {$ship} has no working PA speakers.
wg-internet-sound-too-many = Too many sounds are already in play. Wait for one to finish.
wg-internet-sound-admin-entry = "{$title}" from {$admin}
wg-internet-sound-admin-entry-pa = {$ship}: "{$title}" from {$admin}
wg-internet-sound-admin-fetching = Fetching a sound...
wg-internet-sound-admin-sending = Sending the sound to players...
wg-internet-sound-admin-idle = Nothing playing.
wg-internet-sound-error-ytdlp-missing = yt-dlp wasn't found ({$detail}). Install it or set wg.internet_sound.ytdlp_path.
wg-internet-sound-error-ffmpeg-missing = ffmpeg wasn't found ({$detail}). Install it or set wg.internet_sound.ffmpeg_path.
wg-internet-sound-error-download = yt-dlp couldn't fetch that link: {$detail}
wg-internet-sound-error-download-too-large = The source download exceeded the {$detail} MB input limit.
wg-internet-sound-error-host = {$detail} is a private, local or unresolvable address, which isn't allowed.
wg-internet-sound-error-https = The link and all download destinations must use HTTPS on port 443. Unencrypted redirects and streams are not allowed.
wg-internet-sound-error-rejected = That link is a livestream or longer than the {$detail} second limit.
wg-internet-sound-error-transcode = ffmpeg couldn't convert the audio: {$detail}. It needs libvorbis support.
wg-internet-sound-error-too-large = The converted audio is {$detail} MB, over the size limit.
wg-internet-sound-error-unplayable = The converted audio came out in a form the game can't play. Check the server log.
wg-internet-sound-error-timeout = Fetching took too long and was cancelled.
wg-internet-sound-error-unknown = Something went wrong fetching the sound. Check the server log.

## Commands
cmd-playinternetsound-desc = Plays a YouTube, SoundCloud or other yt-dlp supported link to every connected player.
cmd-playinternetsound-help = Usage: {$command} <link> [stealth]
    With stealth true, nobody is shown who played it; only the admin logs record it.
cmd-playinternetsound-hint = <link>
cmd-playinternetsound-hint-stealth = [stealth]
cmd-playinternetsound-invalid-args = Expected a link and an optional stealth flag.
cmd-playinternetsound-invalid-stealth = {$value} isn't true or false.
cmd-playinternetsoundpa-desc = Plays a yt-dlp supported link out of one ship's PA speakers.
cmd-playinternetsoundpa-help = Usage: {$command} <grid> <link> [stealth]
cmd-playinternetsoundpa-hint-grid = <grid>
cmd-playinternetsoundpa-invalid-args = Expected a grid entity id, a link and an optional stealth flag.
cmd-playinternetsoundpa-invalid-grid = {$grid} isn't a grid.
cmd-stopinternetsound-desc = Stops the current internet sound for everyone.
cmd-stopinternetsound-help = Usage: {$command}
