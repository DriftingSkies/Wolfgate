# R5 notes: decal palettes, empty downloads, Save to

Round R5 of owner feedback: three reports, what was found and what changed.

## 1. "The palette selector for the decal placer is missing in mapping mode"

- The upstream decal placer window (`DecalPlacerWindow`, F8) has a Palette button that opens `PaletteColorPicker`: an
  option button of the `ColorPalettePrototype`s (hidden ones left out) and a list of their colours. The mapping
  screen's Decals tab (`WFMappingDecalPalette`) already had the colour sliders, Use colour, Auto, Snap, Cleanable,
  rotation and z-index, all wired to `WFMappingBuildModeSystem.DecalSettings` and the decal entry's colour. The
  colour palettes were the one thing missing.
- Added under the sliders: a Palette option button filled from `ColorPalettePrototype` (not hidden, by name) and a
  grid of swatches of the chosen palette, with the colour's name and hex as tooltip. A swatch sets the sliders and
  turns Use colour on (picking a palette colour means to use it; the upstream window leaves Use colour as it is), then
  goes the same way as moving the sliders: the decal entry is placed again with the new colour.
- The palette shown isn't remembered; each new mapping screen starts on the first.
- The upstream window is still reachable in mapping mode (F8, a marked edit lets mappers open it). It worked on its
  own, but closing it turns the decal system's placement off behind the build mode's back, and the build mode only
  turned placement on when its own flag said it was off; so a decal picked in the palette stopped placing after the
  window was closed, and picking it again did nothing until another kind of entry was chosen. `SetDecalActive(true)`
  now always reaches the decal system.

## 2. "People on Linux are having trouble downloading maps, empty YML files"

The whole path was traced: save (`TrySaveLevels` -> `MapLoaderSystem.TrySaveMap(ResPath)`, `TrySaveGrid(ResPath)`,
the vessel file through `WriteAllText`), scan, the server read, the transfer and the client write. Every writer is
synchronous and disposes its stream before the next step; paths are `ResPath` throughout (the engine's
`WritableDirProvider` turns them into OS paths); map folders are matched ignoring case and saves reuse the existing
spelling, so a Linux server doesn't split one map over two folders; listing order doesn't matter (levels, grids and
vessels are keyed by depth); the client writes each file once, whole, after its last chunk, with `FileMode.Create`
inside a `using`. No Linux-only path to an empty file was found by reading, and none could be run on Linux here.

What let an empty file through as a success, whatever made it empty:

- Server (`WFMappingDownloadSystem.NextFile`): the size came from `stream.Length`. A length of 0 gave an empty
  array, `ReadExactly` read nothing without complaint, the chunk count was clamped to 1 and one empty chunk went out.
  A zero-byte library file (left by a save killed between the engine opening the file and writing it, since
  `TrySaveMap(ResPath)` opens and truncates the file before it serializes) was listed and downloaded as such.
- Shared (`WFMappingDownloadAssembler`): an empty file was a valid transfer (its own unit test said so), and the
  client had no size to check the chunks against.
- Client (`WFMappingClientSystem.Download`): it wrote the bytes and reported "Downloaded" without checking the file.
- Client: a download that failed on its first chunk deleted the files of the previous, finished download, because
  the written-files list was only cleared when the next download started. Missing files, not empty ones, but fixed.

Changes:

- The server reads each file to its end (`ReadCapped`, 32 MiB cap) instead of trusting `Length`; that also removes
  the dependence on the in-memory test folder's `Length` quirk (R1). An empty file stops the download with
  `wf-mapping-mode-download-empty`, naming the file and saying to save the map again, and logs a warning.
- Every chunk carries `TotalBytes`. The assembler refuses a transfer of 0 or over 32 MiB bytes, a chunk that would go
  past the size, and a set of chunks that falls short of it.
- The client reads each written file back, counting bytes rather than asking for `Length`, and drops the download
  with `wf-mapping-mode-download-verify-failed` when the count differs. Save to checks the dialog's stream after
  flushing it.
- The written-files list is cleared when a download finishes.

## 3. "Can we allow people to select the folder where these maps download?"

- The engine has no folder picker. `IFileDialogManager.SaveFile(filters, truncate, access, share)` opens the OS save
  dialog and returns an open stream (and whether the file existed); it takes no starting folder or file name (the
  SDL3 dialog is opened without a location), and content can't open a path itself: the sandbox allows writing only
  into the data folder or into a stream the dialog returns. So a folder can't be picked once for every download; each
  file needs its own dialog.
- "Downloads go to" (Data folder or Save to...) sits under the library in the lobby, in the Library tab, and beside
  Download as ship in the Save tab (which now has its own download row). Every picker shows the client cvar
  `wf.mapping_mode.download_save_to` (archived, so kept between games) and changes it. Data folder is the default and
  works as before.
- Save to: the download runs as usual, but the files are held in memory until the last arrives, so a slow dialog
  can't stall the transfer into the server's 30 s timeout. Then one save dialog opens per file, `.yml` filtered, in
  download order; the row says which file it asks for and what it is ("the map", "one level", "the ship's grid", "the
  ship's vessel prototype") and "file N of M". A cancelled dialog skips that file. The result line says how many were
  saved. No Open folder button, since the game doesn't know where they went.
- Limits: the dialog doesn't suggest the file name, so the player types it; whether `.yml` is added when they leave
  it out is up to the OS dialog. A new download can't start while dialogs are still pending (the busy message).
  Files are held in memory (32 MiB each at most).

## Verified

- Builds of Content.Server, Content.Client, Content.Tests and Content.IntegrationTests in DebugOpt with nullable
  warnings as errors.
- `Content.Tests` filtered to `_WF.MappingMode`: 229 passed, none skipped (the assembler tests now cover the
  announced size, empty files and short or long chunk sets).
- `Content.IntegrationTests` filtered to `_WF.MappingMode`: 26 passed, none skipped. The new
  `WFMappingDownloadTest` saves a session through the real client, downloads it as a map and as a ship and compares
  the client's files with the server's grid and vessel files byte for byte (non-empty), runs Save to with the
  headless client's absent dialog, and checks an empty library file is refused with its message and nothing written.
- Headless client from `bin/Content.Client`: assemblies verified, no sandbox violation.

## Not exercised

- No Linux machine: the empty-file report wasn't reproduced. The fixes make an empty or short file fail loudly on
  either end instead of arriving as a success.
- The save dialog can't open in a headless client, so the integration test covers Save to only up to every dialog
  being cancelled (nothing lands in the data folder, the skipped result is shown). Writing to a dialog's stream wasn't
  driven in a real client.
- The swatches and the upstream window fix weren't clicked in a real client.
