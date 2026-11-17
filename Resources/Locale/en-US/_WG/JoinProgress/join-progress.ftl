join-progress-stage-Connecting = CONNECTING
join-progress-stage-Handshake = SETTING UP CONNECTION
join-progress-stage-Downloading = DOWNLOADING SERVER FILES
join-progress-stage-EnteringGame = ENTERING THE GAME

join-progress-download = { $received } OF { $total } AT { $speed }/S

join-progress-stuck = THIS STEP SHOULD ONLY TAKE A FEW SECONDS. YOUR FIREWALL OR ANTIVIRUS IS PROBABLY BLOCKING THE FILE CONNECTION TO THIS SERVER. ALLOW THE GAME THROUGH IT, THEN RECONNECT.
join-progress-reconnect = RECONNECT
join-progress-copy = COPY DETAILS
join-progress-reconnect-reason = Reconnecting after a stuck join.

join-progress-details-header = Join details for { $address }:
join-progress-details-stage = Step { $step }, { $stage }: { $seconds } s
join-progress-details-files = Files: { $count }, { $size }, over { $transport }

join-console-title = WOLFGATE // CONNECTION
join-console-exit = EXIT
join-console-link = LINK  PING { $ping } MS | RX { $received } | TX { $sent } | FILES { $transport }
join-console-transport-unknown = UNKNOWN
join-console-transport-game = GAME LINK
join-console-transport-socket = FILE SOCKET

join-console-channel-row = { $tag } FAST FILE CHANNEL: { $result }
join-console-channel-GameConnection = OFF ON THIS SERVER
join-console-channel-WebSocket = CONNECTED
join-console-channel-WebSocketFailed = FAILED, USING THE SLOWER GAME LINK
join-console-log-channel-WebSocket = FAST FILE CHANNEL CONNECTED
join-console-log-channel-WebSocketFailed = FAST FILE CHANNEL FAILED, DOWNLOADS USE THE GAME LINK
join-progress-details-channel = Fast file channel: { $result }

join-console-tag-Pending = [    ]
join-console-tag-Active = [ >> ]
join-console-tag-Stuck = [WAIT]
join-console-tag-Done = [ OK ]
join-console-tag-Failed = [FAIL]
join-console-row = { $tag } { $step }/{ $total } { $stage }
join-console-seconds = { $seconds } S

join-console-bar = {"       "}{ $bar } { $percent }%
join-console-bar-scan = {"       "}{ $bar }
join-console-detail = {"       "}> { $text }
join-console-hint-Handshake = STRING TABLE, SERIALIZER, FILE CHANNEL
join-console-hint-Downloading = WAITING ON SERVER FILES AND SESSION
join-console-hint-EnteringGame = SERVER IS LOADING YOUR CHARACTER

join-console-overall = TOTAL { $bar } { $seconds } S

join-console-log = [{ $offset }] { $text }
join-console-log-Handshake = LOGIN ACCEPTED
join-console-log-Downloading = HANDSHAKE DONE
join-console-log-EnteringGame = SESSION READY
join-console-log-files = SERVER HAS { $count ->
    [one] 1 FILE
   *[other] { $count } FILES
}, { $size }
join-console-log-stuck = NO ANSWER ON THE FILE CHANNEL
join-console-log-reconnect = RECONNECTING
join-console-log-closed = CONNECTION CLOSED
join-console-log-failed = CONNECTION FAILED
