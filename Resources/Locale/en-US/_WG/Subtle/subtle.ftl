wg-subtle-verb = Subtle emote
wg-subtle-verb-message = Do something that only the people right next to you notice.
wg-subtle-dialog-title = Subtle emote
wg-subtle-dialog-prompt = Action
wg-subtle-cannot-emote = You can't do that right now.

# WOLFGATE(ChatFormatting): unescaped message for emphasis BBCode
wg-subtle-wrap-message = [italic][color=#c9a9d6]{ PROPER($entity) ->
    *[false] The {$entityName} {$message}[/color][/italic]
     [true] {CAPITALIZE($entityName)} {$message}[/color][/italic]
    }

## chat channel
hud-chatbox-select-channel-Subtle = Subtle
hud-chatbox-channel-Subtle = Subtle

## subtle command
cmd-subtle-desc = Performs an action that only the players right next to you see.
cmd-subtle-help = Usage: {$command} <action>
cmd-subtle-hint = <action>

## subtle LOOC
wg-sooc-wrap-message = SOOC: [bold]{$entityName}:[/bold] {$message}
wg-sooc-wrap-message-admin = SOOC: [bold]{$entityName} ({$player}):[/bold] {$message}
hud-chatbox-select-channel-SubtleOOC = SOOC
hud-chatbox-channel-SubtleOOC = SOOC
cmd-sooc-desc = Sends LOOC that only the players right next to you and admins see.
cmd-sooc-help = Usage: {$command} <message>
cmd-sooc-hint = <message>
