# MameArcade

The retro arcade cabinet (`WFMameArcade`, sold flat-packed by the Wolfgate vendor) runs the player's own MAME ROM
sets on their client, alone or with a second player at the same cabinet. It runs Neo Geo and Capcom CPS2 games.

**ROMs.** Players import zips through the window, or drop them into `wolfgate_mame/roms` in their user data (the
window's Open ROM folder). Nothing is downloaded or shared: the server only ever sees a set name and which BIOS it
uses. Each game needs its MAME set, and Neo Geo games `neogeo.zip` for the BIOS; chips are found by CRC and size
across every zip, so split, merged and non-merged sets all work. Battery RAM and save states live beside them.

**Window.** `MameArcadeLibraryView` is the start screen: what is happening on the cabinet (an open 2-player game to
join, or a game to watch), the player's games with a search and a ready-only filter, and the chosen game's details,
missing files and Play and Host buttons. Other versions of a game the player can already run share most of its chips,
so an incomplete set is only listed if the player has a chip no ready game uses. It warns when Neo Geo games have no
BIOS and lists zips that hold no game it runs. Under the game, `MameArcadeControlDeck` draws both players' joysticks,
the board's buttons (A B C D, or CPS2's three punches over three kicks), Start and Coin, lit as they are pressed,
with the keys printed on the local player's side.

**Emulation.** `Engine/` is the MAME.NET core (GPL-3.0, see `Engine/NOTICE.txt` for what changed). MAME.NET loads
pre-built region files rather than MAME's chip dumps, so `NeoGeo/NeoGeoLoader.cs` assembles them the way MAME's ROM
macros do, from the layouts in `NeoGeo/NeoGeoSets.cs`. That table is generated from MAME's `neogeo.cpp` by
`Tools/_WF/MameArcade/gen_neogeo_sets.py`, and lists only the games MAME.NET emulates whose cartridges hold plain,
unencrypted data. `MameGames` lists both boards' games behind `IMameGame` and starts the right board.
`MameArcadeSession` runs the emulator on a worker thread and paces it to the game's refresh rate; `MameArcadeAudio`
plays its sound as overlapping clips, since content can't stream audio.

**CPS2.** `Cps2/Cps2Sets.cs` is generated from MAME's `cps2.cpp` by `Tools/_WF/MameArcade/gen_cps2_sets.py`: the
standard games for one or two players (213 sets), leaving out Phoenix and bootleg boards, games for three or four
players and special controls. CPS2 boards encrypt opcode fetches, so `Cps2Crypt` (a port of MAME's `cps2crypt.cpp`,
BSD-3-Clause) decrypts the program into the `maincpuop.rom` region MAME.NET reads opcodes from, beside the raw program
for data reads. The key comes from the set's `.key` file, or from the table MAME 0.150 kept, since zips made before
MAME shipped key files have none. Older dumps MAME marks `BAD_DUMP` but used to accept still load, and the 2-player
fingerprint names which dump each chip is. `Cps2Loader` reproduces MAME's ROM interleaves and `cps2_gfx_decode`
(unshuffle, then pack each bitplane group into pens). MAME.NET draws CPS2 0x100 lines down its frame buffer, which
`MameMachine` allows for. CPS2 keeps no EEPROM between sessions, so a game's settings are its defaults.

**Two players.** One player hosts from the window and another joins at the same cabinet if their set and BIOS match.
Both clients boot the game from power-on and run in lockstep (`MameNetplay`): each frame's controls are picked a few
frames ahead and sent to the other client, and a frame runs once both are known. Inputs travel as a raw net message
(`MsgMameArcadeInput`) that `WFMameArcadeSystem` passes on at once; UI messages would wait for their game tick at the
server and again at the other client, which lockstep feels on every frame. The server sets the starting delay from
both players' pings. Each input message carries how early the sender gets the partner's inputs; player 1 picks one
delay for both from that and sends it along, and whichever client runs ahead holds back the odd frame until both
have the same slack, so neither player ends up with all the lag. The clients compare a checksum of work RAM every two seconds; if a partner leaves or
the games drift apart, the other plays on alone. The server drops a seat's inputs past about four times the normal
rate, and each client ignores inputs and checksums for frames far beyond any real partner's lead.

**Bystanders.** The cabinet shows one game: player 1's in a 2-player game, or else the first player to start one
alone (`WFMameArcadeComponent.Player`). That client streams its sound to the server as 24 kHz mono ADPCM, about
12 KiB a second (`MameAudioCodec`), and the server relays it to everyone within 10 tiles except the players; their
`MameArcadeNearbySystem` plays it from the cabinet. Anyone else with the window open can press Watch. While someone
watches, the player also sends the picture (`MameVideoCodec`): 15-bit colour, only the 8x8 tiles that changed,
deflated, with a keyframe for each new watcher, every 10 seconds, and whenever the server drops a picture packet
over the rate, and both players' controls for the watcher's
control deck. A busy Neo Geo screen is about 11 KiB, so the
frame rate (30 at most) drops to fit `wf.mame_arcade.video_rate` (KiB a second, 0 turns watching off) rather than
lagging, with frames spaced evenly. Each packet carries when its frame was taken, and the watcher shows it about
100 ms later, so frames that bunch up on the way still show at the pace they were taken. Up to
`wf.mame_arcade.max_watchers` can watch a cabinet. Ghosts and anyone else the cabinet takes no buttons from can only watch: the UI drops their
messages, so the server puts them in the audience itself whenever a game is on, and their window shows nothing else.
A player who goes into crit or dies has the window closed on them, which leaves their game or the audience. Both streams are raw net messages (`MsgMameArcadeStream`) the
server checks for sender and rate but never decodes. Bystanders need no ROMs.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/MameArcade/WFMameArcadeSystem.cs`](WFMameArcadeSystem.cs)

### Shared

- [`Content.Shared/_WF/MameArcade/MameArcadeMessages.cs`](../../../Content.Shared/_WF/MameArcade/MameArcadeMessages.cs)
- [`Content.Shared/_WF/MameArcade/MsgMameArcadeInput.cs`](../../../Content.Shared/_WF/MameArcade/MsgMameArcadeInput.cs)
- [`Content.Shared/_WF/MameArcade/MsgMameArcadeStream.cs`](../../../Content.Shared/_WF/MameArcade/MsgMameArcadeStream.cs)
- [`Content.Shared/_WF/MameArcade/WFMameArcadeComponent.cs`](../../../Content.Shared/_WF/MameArcade/WFMameArcadeComponent.cs)
- [`Content.Shared/_WF/MameArcade/WFMameArcadeCVars.cs`](../../../Content.Shared/_WF/MameArcade/WFMameArcadeCVars.cs)

### Client

- [`Content.Client/_WF/MameArcade/Cps2/Cps2Crypt.cs`](../../../Content.Client/_WF/MameArcade/Cps2/Cps2Crypt.cs)
- [`Content.Client/_WF/MameArcade/Cps2/Cps2Loader.cs`](../../../Content.Client/_WF/MameArcade/Cps2/Cps2Loader.cs)
- [`Content.Client/_WF/MameArcade/Cps2/Cps2Set.cs`](../../../Content.Client/_WF/MameArcade/Cps2/Cps2Set.cs)
- [`Content.Client/_WF/MameArcade/Cps2/Cps2Sets.cs`](../../../Content.Client/_WF/MameArcade/Cps2/Cps2Sets.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6502/Ill02.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6502/Ill02.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6502/M6502.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6502/M6502.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6502/M6502op.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6502/M6502op.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6502/Ops02.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6502/Ops02.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6800/M6800.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6800/M6800.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6800/M6800op.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6800/M6800op.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m68000/Disassembler.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m68000/Disassembler.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m68000/Instructions/BitArithemetic.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m68000/Instructions/BitArithemetic.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m68000/Instructions/DataMovement.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m68000/Instructions/DataMovement.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m68000/Instructions/IntegerMath.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m68000/Instructions/IntegerMath.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m68000/Instructions/ProgramFlow.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m68000/Instructions/ProgramFlow.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m68000/Instructions/Supervisor.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m68000/Instructions/Supervisor.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m68000/MC68000.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m68000/MC68000.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m68000/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m68000/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m68000/OpcodeTable.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m68000/OpcodeTable.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m68000/Tables.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m68000/Tables.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6805/M6805.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6805/M6805.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6805/M6805op.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6805/M6805op.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6809/Disassembler.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6809/Disassembler.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6809/M6809.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6809/M6809.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/m6809/M6809op.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/m6809/M6809op.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/nec/Nec.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/nec/Nec.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/nec/NecEa.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/nec/NecEa.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/nec/NecInstr.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/nec/NecInstr.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/nec/NecModrm.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/nec/NecModrm.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/z80/Disassembler.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/z80/Disassembler.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/z80/Execute.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/z80/Execute.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/z80/Interrupts.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/z80/Interrupts.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/z80/Registers.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/z80/Registers.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/z80/Tables.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/z80/Tables.cs)
- [`Content.Client/_WF/MameArcade/Engine/cpu/z80/Z80A.cs`](../../../Content.Client/_WF/MameArcade/Engine/cpu/z80/Z80A.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Attotime.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Attotime.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/BinaryWriter.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/BinaryWriter.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Cpuexec.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Cpuexec.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Cpuint.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Cpuint.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Crosshair.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Crosshair.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Eeprom.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Eeprom.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Generic.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Generic.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Inptport.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Inptport.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Keyboard.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Keyboard.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/KeyStruct.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/KeyStruct.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Machine.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Machine.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Mame.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Mame.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Mouse.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Mouse.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Palette.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Palette.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Pd4900a.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Pd4900a.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/RomInfo.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/RomInfo.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Timer.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Timer.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/UI.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/UI.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Watchdog.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Watchdog.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Window.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Window.cs)
- [`Content.Client/_WF/MameArcade/Engine/emu/Wintime.cs`](../../../Content.Client/_WF/MameArcade/Engine/emu/Wintime.cs)
- [`Content.Client/_WF/MameArcade/Engine/LICENSE.txt`](../../../Content.Client/_WF/MameArcade/Engine/LICENSE.txt)
- [`Content.Client/_WF/MameArcade/Engine/mame/capcom/Capcom.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/capcom/Capcom.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/capcom/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/capcom/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/capcom/Gng.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/capcom/Gng.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/capcom/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/capcom/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/capcom/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/capcom/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/capcom/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/capcom/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/capcom/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/capcom/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/capcom/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/capcom/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/cps/CPS.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/cps/CPS.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/cps/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/cps/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/cps/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/cps/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/cps/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/cps/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/cps/Memory2.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/cps/Memory2.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/cps/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/cps/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/cps/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/cps/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/cps/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/cps/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/dataeast/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/dataeast/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/dataeast/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/dataeast/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/dataeast/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/dataeast/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/dataeast/Pcktgal.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/dataeast/Pcktgal.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/dataeast/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/dataeast/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/dataeast/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/dataeast/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/dataeast/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/dataeast/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/igs011/IGS011.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/igs011/IGS011.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/igs011/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/igs011/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/igs011/Machine.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/igs011/Machine.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/igs011/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/igs011/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/igs011/Memory2.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/igs011/Memory2.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/igs011/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/igs011/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/igs011/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/igs011/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/konami68000/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/konami68000/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/konami68000/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/konami68000/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/konami68000/Konami68000.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/konami68000/Konami68000.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/konami68000/Konamiic.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/konami68000/Konamiic.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/konami68000/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/konami68000/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/konami68000/Memory2.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/konami68000/Memory2.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/konami68000/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/konami68000/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/konami68000/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/konami68000/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/konami68000/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/konami68000/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m72/Audio.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m72/Audio.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m72/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m72/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m72/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m72/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m72/M72.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m72/M72.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m72/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m72/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m72/Memory2.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m72/Memory2.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m72/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m72/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m72/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m72/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m72/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m72/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m92/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m92/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m92/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m92/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m92/M92.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m92/M92.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m92/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m92/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m92/Memory2.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m92/Memory2.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m92/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m92/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m92/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m92/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/m92/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/m92/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/namcos1/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/namcos1/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/namcos1/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/namcos1/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/namcos1/Machine.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/namcos1/Machine.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/namcos1/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/namcos1/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/namcos1/Memory2.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/namcos1/Memory2.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/namcos1/Namcos1.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/namcos1/Namcos1.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/namcos1/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/namcos1/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/namcos1/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/namcos1/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/namcos1/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/namcos1/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/neogeo/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/neogeo/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/neogeo/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/neogeo/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/neogeo/Neogeo.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/neogeo/Neogeo.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/neogeo/Neoprot.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/neogeo/Neoprot.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/neogeo/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/neogeo/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/neogeo/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/neogeo/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/pgm/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/pgm/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/pgm/Machine.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/pgm/Machine.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/pgm/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/pgm/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/pgm/Memory2.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/pgm/Memory2.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/pgm/Pgm.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/pgm/Pgm.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/pgm/Pgmprot.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/pgm/Pgmprot.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/pgm/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/pgm/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/pgm/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/pgm/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/pgm/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/pgm/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/suna8/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/suna8/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/suna8/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/suna8/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/suna8/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/suna8/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/suna8/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/suna8/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/suna8/SunA8.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/suna8/SunA8.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/suna8/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/suna8/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taito/Bublbobl.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taito/Bublbobl.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taito/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taito/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taito/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taito/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taito/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taito/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taito/Opwolf.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taito/Opwolf.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taito/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taito/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taito/Taito.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taito/Taito.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taito/Taitoic.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taito/Taitoic.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taito/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taito/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taito/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taito/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taitob/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taitob/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taitob/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taitob/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taitob/Mb87078.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taitob/Mb87078.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taitob/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taitob/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taitob/Memory2.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taitob/Memory2.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taitob/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taitob/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taitob/Taitob.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taitob/Taitob.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taitob/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taitob/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/taitob/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/taitob/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/tehkan/Drawgfx.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/tehkan/Drawgfx.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/tehkan/Input.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/tehkan/Input.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/tehkan/Memory.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/tehkan/Memory.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/tehkan/Pbaction.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/tehkan/Pbaction.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/tehkan/State.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/tehkan/State.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/tehkan/Tilemap.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/tehkan/Tilemap.cs)
- [`Content.Client/_WF/MameArcade/Engine/mame/tehkan/Video.cs`](../../../Content.Client/_WF/MameArcade/Engine/mame/tehkan/Video.cs)
- [`Content.Client/_WF/MameArcade/Engine/NOTICE.txt`](../../../Content.Client/_WF/MameArcade/Engine/NOTICE.txt)
- [`Content.Client/_WF/MameArcade/Engine/sound/AY8910.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/AY8910.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/DAC.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/DAC.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/FM.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/FM.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/FMOpl.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/FMOpl.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/ICS2115.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/ICS2115.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/Iremga20.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/Iremga20.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/K007232.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/K007232.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/K053260.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/K053260.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/K054539.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/K054539.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/MSM5205.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/MSM5205.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/Namco.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/Namco.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/OKI6295.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/OKI6295.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/QSound.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/QSound.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/Sample.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/Sample.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/Sound.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/Sound.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/Streams.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/Streams.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/Taitosnd.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/Taitosnd.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/Upd7759.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/Upd7759.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/YM2151.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/YM2151.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/YM2203.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/YM2203.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/YM2413.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/YM2413.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/YM2610.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/YM2610.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/YM3812.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/YM3812.cs)
- [`Content.Client/_WF/MameArcade/Engine/sound/YMDeltat.cs`](../../../Content.Client/_WF/MameArcade/Engine/sound/YMDeltat.cs)
- [`Content.Client/_WF/MameArcade/MameArcadeAudio.cs`](../../../Content.Client/_WF/MameArcade/MameArcadeAudio.cs)
- [`Content.Client/_WF/MameArcade/MameArcadeBoundUserInterface.cs`](../../../Content.Client/_WF/MameArcade/MameArcadeBoundUserInterface.cs)
- [`Content.Client/_WF/MameArcade/MameArcadeControlDeck.cs`](../../../Content.Client/_WF/MameArcade/MameArcadeControlDeck.cs)
- [`Content.Client/_WF/MameArcade/MameArcadeControls.cs`](../../../Content.Client/_WF/MameArcade/MameArcadeControls.cs)
- [`Content.Client/_WF/MameArcade/MameArcadeLibraryView.cs`](../../../Content.Client/_WF/MameArcade/MameArcadeLibraryView.cs)
- [`Content.Client/_WF/MameArcade/MameArcadeNearbySystem.cs`](../../../Content.Client/_WF/MameArcade/MameArcadeNearbySystem.cs)
- [`Content.Client/_WF/MameArcade/MameArcadeNetManager.cs`](../../../Content.Client/_WF/MameArcade/MameArcadeNetManager.cs)
- [`Content.Client/_WF/MameArcade/MameArcadeScreen.cs`](../../../Content.Client/_WF/MameArcade/MameArcadeScreen.cs)
- [`Content.Client/_WF/MameArcade/MameArcadeSession.cs`](../../../Content.Client/_WF/MameArcade/MameArcadeSession.cs)
- [`Content.Client/_WF/MameArcade/MameArcadeWindow.cs`](../../../Content.Client/_WF/MameArcade/MameArcadeWindow.cs)
- [`Content.Client/_WF/MameArcade/MameAudioCodec.cs`](../../../Content.Client/_WF/MameArcade/MameAudioCodec.cs)
- [`Content.Client/_WF/MameArcade/MameGames.cs`](../../../Content.Client/_WF/MameArcade/MameGames.cs)
- [`Content.Client/_WF/MameArcade/MameMachine.cs`](../../../Content.Client/_WF/MameArcade/MameMachine.cs)
- [`Content.Client/_WF/MameArcade/MameNetplay.cs`](../../../Content.Client/_WF/MameArcade/MameNetplay.cs)
- [`Content.Client/_WF/MameArcade/MameRomLibrary.cs`](../../../Content.Client/_WF/MameArcade/MameRomLibrary.cs)
- [`Content.Client/_WF/MameArcade/MameVideoCodec.cs`](../../../Content.Client/_WF/MameArcade/MameVideoCodec.cs)
- [`Content.Client/_WF/MameArcade/MameZip.cs`](../../../Content.Client/_WF/MameArcade/MameZip.cs)
- [`Content.Client/_WF/MameArcade/NeoGeo/NeoGeoLoader.cs`](../../../Content.Client/_WF/MameArcade/NeoGeo/NeoGeoLoader.cs)
- [`Content.Client/_WF/MameArcade/NeoGeo/NeoGeoSet.cs`](../../../Content.Client/_WF/MameArcade/NeoGeo/NeoGeoSet.cs)
- [`Content.Client/_WF/MameArcade/NeoGeo/NeoGeoSets.cs`](../../../Content.Client/_WF/MameArcade/NeoGeo/NeoGeoSets.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/MameArcade/WFMameArcadeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MameArcade/WFMameArcadeTest.cs)

### Unit tests

- [`Content.Tests/_WF/MameArcade/Cps2LoaderTest.cs`](../../../Content.Tests/_WF/MameArcade/Cps2LoaderTest.cs)
- [`Content.Tests/_WF/MameArcade/MameMachineTest.cs`](../../../Content.Tests/_WF/MameArcade/MameMachineTest.cs)
- [`Content.Tests/_WF/MameArcade/MameNetplayTest.cs`](../../../Content.Tests/_WF/MameArcade/MameNetplayTest.cs)
- [`Content.Tests/_WF/MameArcade/MameStreamCodecTest.cs`](../../../Content.Tests/_WF/MameArcade/MameStreamCodecTest.cs)
- [`Content.Tests/_WF/MameArcade/MameZipTest.cs`](../../../Content.Tests/_WF/MameArcade/MameZipTest.cs)
- [`Content.Tests/_WF/MameArcade/NeoGeoLoaderTest.cs`](../../../Content.Tests/_WF/MameArcade/NeoGeoLoaderTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/MameArcade/mame_arcade.yml`](../../../Resources/Prototypes/_WF/MameArcade/mame_arcade.yml)

### Localization

- [`Resources/Locale/en-US/_WF/MameArcade/mame-arcade.ftl`](../../../Resources/Locale/en-US/_WF/MameArcade/mame-arcade.ftl)

### Tools

- [`Tools/_WF/MameArcade/gen_cps2_sets.py`](../../../Tools/_WF/MameArcade/gen_cps2_sets.py)
- [`Tools/_WF/MameArcade/gen_neogeo_sets.py`](../../../Tools/_WF/MameArcade/gen_neogeo_sets.py)

## Non-modular edits

- [`Content.Client/Entry/EntryPoint.cs`](../../../Content.Client/Entry/EntryPoint.cs): register the 2-player input message
- [`Content.Client/IoC/ClientContentIoC.cs`](../../../Content.Client/IoC/ClientContentIoC.cs): relays 2-player inputs

<!-- WOLFGATE-GENERATED END -->
