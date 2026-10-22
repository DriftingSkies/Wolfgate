# MappingMode phase 2, P7 notes

The vanishing UI on the mapping screen: what broke, the fix, and the dock hardening done alongside it.

## The bug

In a session, after two pop-out OS windows were closed, every frame logged
`Caught exception while trying to draw a UI element: MainWindowRoot` with a `NullReferenceException` in
`TextureRect.Draw`, until the mapping screen unloaded. One bad control aborts the whole root's draw, so the entire UI
disappeared.

## Root cause

`WFMappingPaletteIcons.Tile` built a tile icon as `new AtlasTexture(texture, firstVariant)`. For a tile whose sprite
lives inside an RSI (`FloorPlanetDirt`, the circuit floors, `WFCavernFloorPoolbed`) `texture` came from the RSI state's
`Frame0`, which is already an `AtlasTexture` on the RSI sheet. That made an atlas of an atlas.

Engine v291 caches the GPU texture on the atlas: `AtlasTexture.ClydeTexture = texture as ClydeTexture`, which is null
when the source is another atlas. A `TextureRect` draws a texture with no sub-region through
`Clyde.RenderHandle.DrawTextureScreen(AtlasTexture, ...)`, which reads `texture.ClydeTexture!.TextureId`
(`RobustToolbox/Robust.Client/Graphics/Clyde/Clyde.RenderHandle.cs:89`). In the DebugOpt build that whole chain is
devirtualised and inlined into `TextureRect.Draw`, so the trace stops at `TextureRect.cs:151`.

Every view of a tile entry used that icon: the tile palette rows, the quick bar, the recent strip and the properties
panel. The errors began once such an icon showed in the main window, and stopped when the screen and its icons went
away. The closed OS windows only moved the panels back into the main window. They didn't cause the crash. The
crop was wrong too: an RSI frame is already one tile cell, so dividing it by `variants` drew an 8x32 sliver of dirt.

Before v291 the same icon would have failed too, with an `InvalidCastException` in `ExtractTexture`. Atlases of
atlases have never been drawable, so the fix belongs in content code. The engine is unchanged.

## Fix

- `Palette/WFMappingPaletteIcons.cs`: a tile sprite inside an RSI uses its state's `Frame0` as is (one cell, an atlas
  straight on the sheet). A raw PNG is cut to its first variant through `SubRegion`, which cuts an atlas from its own
  source at the right offset, so the icons code never builds an atlas of an atlas.
- `Screen/WFMappingDockWindow.cs`: raises `Lost` when the window leaves the UI tree and is still out once the frame's
  deferred actions run. A pop-out moves it out and back at once, which does not count.
- `Screen/WFMappingDockSlot.cs`: docks on `Lost` as well as on `OnClose`. This covers an OS window's root being torn
  down with no close event, which used to strand the panel outside every root. `Dock` already did nothing while
  docked, and it closes a window that is still open, including the stranded pop-out OS window.

I traced the pop-out paths, and they already brought the panel home: the OS close button (`Closing` →
`BaseWindow.Close` → `OnClose` → `Dock`), the OS window destroyed (`Closed` → `BaseWindow.Close`), the slot's Dock
button while popped out, and screen unload.

## Test

`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingDockPopoutTest.cs`:

- `TileIconsAreDrawable`: no tile icon is an atlas of an atlas, the dirt icon is one 32px cell, and `SubRegion` cuts
  from the source at the right offset. With the old icon code it fails on five RSI-backed tiles and the 8x32 crop.
- `DockPopout`: on the connected client, in a session, the palette is undocked and its window then goes in each of
  these ways: closed in game, popped out and closed from the OS window, popped out with the OS window destroyed as the
  real client does it (root first, then window), popped out with only its root torn down, and popped out then docked
  from the slot (twice). After each, every control under the screen is in the main root, not disposed, among its
  parent's children, the panel is back in its slot and nothing is left popped out. Leaving the session with the panel
  popped out brings it home too. Without the `Lost` hook the root-only case fails.
