# Hailing

Ship-to-ship hailing from the shuttle console. The HAIL button opens a small window listing the ships in range; hailing
one rings every free console aboard it, with a chime and the console's hail lamp, and announces it over that
ship's PA (`ShipPaSystem.Announce`) so the crew can get someone to the helm. Whoever answers gets the caller
on a screen, a portrait of the real person drawn behind scanlines, with a chat line between the two helms. Either side
can close the channel, and it drops if a pilot leaves the console, a console loses power or the ships part.

A ship can be hailed if it has a powered shuttle console and this console's radar would show it: same map, inside
`wf.hailing.range`, not cloaked. A ship hiding its label is listed, and announced to the ship it hails, as an unknown
vessel. Tuning is in `HailingCVars`.

Entry points: the server `HailingSystem`, which keeps the hail as a `HailConsoleComponent` on each console involved
(only while it is hailing, ringing or connected) and sends each operator the other one past PVS range for the
screen; the client `HailWindow` and `HailScreen`, hooked into the upstream console by the
`ShuttleConsoleWindow.Hailing` and `ShuttleConsoleBoundUserInterface.Hailing` partials. Lines are admin logged as chat.

A hail never moves the layout. On the windowed console the HAIL tab key (`HailKey`, ticked by `HailKeySystem` because
the console is hidden behind the cockpit) is the lamp: its caption and tint follow the hail and it flashes while one
comes in. In the cockpit a `HailStrip` rides the caption row of the COMMUNICATIONS housing, which was empty beside
its title: one clipped line of status and the keys to hail, answer, decline, view or end. It reports no height, so
the chat below keeps all of its room.

Admins get a **Hail as God** verb on every shuttle console (`AdminHailSystem`, Fun flag). It rings that ship as
"God" with a choir instead of the chime, PA included, from no ship at all: a stand-in entity carries the admin's
end, so range, power and the helm rules don't apply to it. Whoever answers gets a glowing screen with no face, and
is never sent or told who the admin is; the admin gets the ordinary hail window (`AdminHailEui`) with the pilot on
screen. Closing that window ends the call.

NPC ships use the same path. A console with `HailConsoleComponent.Automated` set (server-only) needs no UI and no
player: it counts as manned while its operator exists, is alive and is on the console's ship, and power, range,
radar, cooldowns and the PA page apply as for a player. Start one with `HailingSystem.TryHailAutomated(console,
operator, ship)` (its `out refused` overload says why a hail never started: unreachable, busy or too soon) and answer
with `AnswerAutomated`; `Answer`, `Say` and `End` work on it as they are, and
`GetState(console)` says what a console is doing. The player on the other end sees the NPC on the screen and its
lines like any other pilot. Hailing raises by-ref broadcast events for whatever drives the NPC (`HailEvents.cs`):
`HailCallEndedEvent` when an automated console's hail or call stops (with the reason and whether it had connected),
`HailAnsweredEvent` when an automated caller's hail is answered, and `HailOptionChosenEvent` (below). They are
raised after the hail is torn down, so a listener can hail again at once, and not at all if the console is deleted.

An end can put answers in front of the other one. `HailingSystem.Offer(console, operator, options)` puts up to four
`HailOption`s (id and short label) on the other end of an open channel; the NPC's own line is the prompt. On the
chooser's side the chat line is replaced by one button per answer, in the hail window; the HAIL key reads REPLY, the
cockpit strip says so and the window comes forward. A pick (`HailChooseMessage` or `Choose`) is checked against the
offer, taken down, logged on both sides as a line said by the chooser and raised as `HailOptionChosenEvent`. Free
typing is refused while answers are up, `ClearOptions(offerer)` takes them back, and they go with the call. Admin
(God) ends can offer but are never offered to. The cockpit has no hail line of its own; its answers are the window's.

To try it alone, the admin command `hailtest` (`HailTestSystem`) brings a small test ship in near the ship you stand
on and has its pilot hail you, as an automated end. That pilot also answers your hails and sends back whatever you
say; `hailtest options` has it offer three answers once you pick up, say which you took and then take free text
again. The ship removes itself after three idle minutes or on `hailtest clear`.

Hail services are windows a caller can open over an answered hail. A `HailServiceComponent` (Name, Key, PerCaller)
marks an entity whose UI can be opened that way. `HailingSystem.OpenService` (`HailingSystem.Services.cs`) relays
that UI onto the caller's helm (`HailRelayComponent`): the helm gets the service's UI key and mirrors its state, and
forwards messages to the service as if the caller stood there. A PerCaller service gets `HailServiceOpenedEvent` and
pushes its own state to the helm instead. Relays close when the hail ends.

An NPC with `HailResponderComponent` mans the nearest powered shuttle console on its grid (`HailResponderSystem`), as
an automated end: no UI, and the call drops if it dies, leaves the ship or leaves the console. It answers a ringing
hail after `AnswerDelay` with `AnswerAutomated`, greets the caller with one of its greetings, speaks the lines
services add in `HailServiceGreetEvent`, and `Offer`s its grid's services (four at most) as answers. Picking one
opens that service on the caller's helm; the answers come back when the window is closed, and the caller can type
while it is open. `HailingSystem.SayToCaller` lets a service speak on a helm's call.

To offer a station service, put a shuttle console by an NPC with HailResponder and give the serving entity HailService.
A station with a shuttle console should get `PreventPilot` so it can't be flown.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Hailing/AdminHailEndpointComponent.cs`](AdminHailEndpointComponent.cs)
- [`Content.Server/_WF/Hailing/AdminHailEui.cs`](AdminHailEui.cs)
- [`Content.Server/_WF/Hailing/AdminHailSystem.cs`](AdminHailSystem.cs)
- [`Content.Server/_WF/Hailing/HailEvents.cs`](HailEvents.cs)
- [`Content.Server/_WF/Hailing/HailingSystem.cs`](HailingSystem.cs)
- [`Content.Server/_WF/Hailing/HailingSystem.Services.cs`](HailingSystem.Services.cs)
- [`Content.Server/_WF/Hailing/HailRelayComponent.cs`](HailRelayComponent.cs)
- [`Content.Server/_WF/Hailing/HailResponderComponent.cs`](HailResponderComponent.cs)
- [`Content.Server/_WF/Hailing/HailResponderSystem.cs`](HailResponderSystem.cs)
- [`Content.Server/_WF/Hailing/HailServiceEvents.cs`](HailServiceEvents.cs)
- [`Content.Server/_WF/Hailing/HailTestCommand.cs`](HailTestCommand.cs)
- [`Content.Server/_WF/Hailing/HailTestShipComponent.cs`](HailTestShipComponent.cs)
- [`Content.Server/_WF/Hailing/HailTestSystem.cs`](HailTestSystem.cs)

### Shared

- [`Content.Shared/_WF/Hailing/AdminHailEui.cs`](../../../Content.Shared/_WF/Hailing/AdminHailEui.cs)
- [`Content.Shared/_WF/Hailing/HailConsoleComponent.cs`](../../../Content.Shared/_WF/Hailing/HailConsoleComponent.cs)
- [`Content.Shared/_WF/Hailing/HailingCVars.cs`](../../../Content.Shared/_WF/Hailing/HailingCVars.cs)
- [`Content.Shared/_WF/Hailing/HailMessages.cs`](../../../Content.Shared/_WF/Hailing/HailMessages.cs)
- [`Content.Shared/_WF/Hailing/HailServiceComponent.cs`](../../../Content.Shared/_WF/Hailing/HailServiceComponent.cs)

### Client

- [`Content.Client/_WF/Hailing/AdminHailEui.cs`](../../../Content.Client/_WF/Hailing/AdminHailEui.cs)
- [`Content.Client/_WF/Hailing/HailKey.cs`](../../../Content.Client/_WF/Hailing/HailKey.cs)
- [`Content.Client/_WF/Hailing/HailKeySystem.cs`](../../../Content.Client/_WF/Hailing/HailKeySystem.cs)
- [`Content.Client/_WF/Hailing/HailScreen.cs`](../../../Content.Client/_WF/Hailing/HailScreen.cs)
- [`Content.Client/_WF/Hailing/HailStrip.cs`](../../../Content.Client/_WF/Hailing/HailStrip.cs)
- [`Content.Client/_WF/Hailing/HailWindow.xaml`](../../../Content.Client/_WF/Hailing/HailWindow.xaml)
- [`Content.Client/_WF/Hailing/HailWindow.xaml.cs`](../../../Content.Client/_WF/Hailing/HailWindow.xaml.cs)
- [`Content.Client/_WF/Hailing/ShuttleConsoleBoundUserInterface.Hailing.cs`](../../../Content.Client/_WF/Hailing/ShuttleConsoleBoundUserInterface.Hailing.cs)
- [`Content.Client/_WF/Hailing/ShuttleConsoleWindow.Hailing.cs`](../../../Content.Client/_WF/Hailing/ShuttleConsoleWindow.Hailing.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Hailing/HailingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Hailing/HailingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Hailing/HailingTest.Npc.cs`](../../../Content.IntegrationTests/Tests/_WF/Hailing/HailingTest.Npc.cs)

### Localization

- [`Resources/Locale/en-US/_WF/Hailing/hailing.ftl`](../../../Resources/Locale/en-US/_WF/Hailing/hailing.ftl)

### Audio

- [`Resources/Audio/_WF/Hailing/attributions.yml`](../../../Resources/Audio/_WF/Hailing/attributions.yml)
- [`Resources/Audio/_WF/Hailing/hail_calling.ogg`](../../../Resources/Audio/_WF/Hailing/hail_calling.ogg)
- [`Resources/Audio/_WF/Hailing/hail_close.ogg`](../../../Resources/Audio/_WF/Hailing/hail_close.ogg)
- [`Resources/Audio/_WF/Hailing/hail_god.ogg`](../../../Resources/Audio/_WF/Hailing/hail_god.ogg)
- [`Resources/Audio/_WF/Hailing/hail_message.ogg`](../../../Resources/Audio/_WF/Hailing/hail_message.ogg)
- [`Resources/Audio/_WF/Hailing/hail_open.ogg`](../../../Resources/Audio/_WF/Hailing/hail_open.ogg)
- [`Resources/Audio/_WF/Hailing/hail_ring.ogg`](../../../Resources/Audio/_WF/Hailing/hail_ring.ogg)

### Tools

- [`Tools/_WF/Hailing/gen_hail_sounds.py`](../../../Tools/_WF/Hailing/gen_hail_sounds.py)

## Non-modular edits

- [`Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs`](../../../Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs): the hail window and its requests
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml)
  - xmlns:hail for the hail key
  - opens the hail window and lights up with the console's hail
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs)
  - hail button and banner
  - point the banner and window at this console

<!-- WOLFGATE-GENERATED END -->
