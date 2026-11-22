wg-custom-marking-creator-button = { $worn ->
    [0] Open library
    *[other] Open library ({ $worn } worn)
}
wg-custom-marking-creator-hint = Click a marking to put it on or take it off, right click to edit it. The library imports and exports them.
wg-custom-marking-tile-tooltip = { $name } ({ $placement })
wg-custom-marking-tile-edit = Right click to edit.
wg-custom-marking-tile-new-tooltip = Draw a new marking.
wg-custom-marking-default-name = Custom marking

wg-custom-marking-library-title = Custom markings
wg-custom-marking-library-heading = Your marking library
wg-custom-marking-library-hint = Draw your own markings and keep them here. A new marking belongs to the character you drew it for; press All characters to offer it to every character. Everything you save is stored on the server, shown to other players and covered by the server rules.
wg-custom-marking-library-new = Draw new
wg-custom-marking-library-new-tooltip = Draw a new marking.
wg-custom-marking-library-import = Import PNG
wg-custom-marking-library-import-tooltip = A 64x64 image holding the four facings (south and north on top, east and west below), or a 32x32 image of the south facing alone. A sheet exported from an animated marking works too; how long its frames show and what it erases are not in the file.
wg-custom-marking-library-counts = { $saved } of { $limit } saved, { $worn } of { $max } worn{ $elsewhere ->
    [0] {""}
    *[other] , { $elsewhere } kept for other characters
}
wg-custom-marking-library-loading = Loading your library...
wg-custom-marking-library-empty = Nothing here yet. Press Draw new to make your first marking.
wg-custom-marking-library-wear = Wear
wg-custom-marking-library-take-off = Take off
wg-custom-marking-library-edit = Edit this marking
wg-custom-marking-library-export = Export this marking as a PNG file to your CustomMarkings folder
wg-custom-marking-exported = Saved {$file} to your CustomMarkings folder.
wg-custom-marking-library-open-folder = Open folder
wg-custom-marking-library-delete = Delete this marking
wg-custom-marking-library-delete-confirm = Really delete?
wg-custom-marking-library-keep = Add to library
wg-custom-marking-library-keep-tooltip = This character wears a marking that isn't in your library here. Add it to keep it for this character and to edit it.
wg-custom-marking-library-stray = Not in your library
wg-custom-marking-library-stray-subtitle = { $placement } · Not in your library
wg-custom-marking-library-subtitle-all = { $placement } · All characters
wg-custom-marking-library-subtitle-character = { $placement } · This character only
wg-custom-marking-library-all-characters = All characters
wg-custom-marking-library-all-characters-tooltip = Pressed, every one of your characters can wear this marking. Up, only this character sees it here.
wg-custom-marking-scope-all = Offered to all your characters.
wg-custom-marking-scope-character = Kept for this character only.

wg-custom-marking-reason-loading = Still loading.
wg-custom-marking-reason-library-full = Your library is full ({ $limit } markings). Delete one to make room.
wg-custom-marking-reason-worn-full = This character already wears { $max } markings. Take one off first.
wg-custom-marking-reason-unsaved-character = Save this character first to keep a marking for it alone.

wg-custom-marking-editor-title-new = New custom marking
wg-custom-marking-editor-title-edit = Edit custom marking
wg-custom-marking-editor-group-draw = Draw
wg-custom-marking-editor-group-history = History
wg-custom-marking-editor-group-facing = Facing
wg-custom-marking-editor-undo = Undo (Ctrl+Z)
wg-custom-marking-editor-redo = Redo (Ctrl+Y)
wg-custom-marking-editor-symmetry = Mirror drawing (M): what you draw on one side of the body's middle is drawn on the other side too.
wg-custom-marking-editor-side-mirror = Side mirror (S): what you draw on one side view is drawn on the other, flipped, so the left and right of the body match.
wg-custom-marking-editor-nudge = Move this facing's { $layer ->
    [erase] erased body
    *[drawing] drawing
} one pixel { $direction ->
    [left] left
    [right] right
    [up] up
    *[down] down
}
wg-custom-marking-editor-flip = Flip this facing's { $layer ->
    [erase] erased body
    *[drawing] drawing
} left to right, about the body's middle
wg-custom-marking-editor-mirror = Copy this facing's { $layer ->
    [erase] erased body
    *[drawing] drawing
}, flipped, onto the other side view
wg-custom-marking-editor-clear = { $layer ->
    [erase] Bring back all of this facing's body
    [glow] Stop all of this facing glowing
    *[drawing] Clear this facing
}
wg-custom-marking-editor-glow-moves = Glow stays with its pixels: move the drawing to move it.
wg-custom-marking-editor-mirror-sides = Only the side views have another side to copy to.
wg-custom-marking-editor-facings = Facing
wg-custom-marking-editor-show-body = Show body
wg-custom-marking-editor-show-clothes = Show clothes
wg-custom-marking-editor-show-grid = Show grid
wg-custom-marking-editor-show-hair = Show hair
wg-custom-marking-editor-show-hair-tooltip = Shows or hides the character's hair and beard here, to see and draw on what is under them. The character keeps them.
wg-custom-marking-editor-opacity = Opacity
wg-custom-marking-editor-opacity-tooltip = Opacity in percent, from 5 to 100.
wg-custom-marking-editor-colour-skin = Your character's skin color
wg-custom-marking-editor-colour-hair = Your character's hair color
wg-custom-marking-editor-colour-facial-hair = Your character's facial hair color
wg-custom-marking-editor-colour-eyes = Your character's eye color
wg-custom-marking-editor-used-colours = In this marking
wg-custom-marking-editor-used-colours-none = Colors you draw with show up here.
wg-custom-marking-editor-used-colour = { $hex } at { $opacity }% opacity
wg-custom-marking-editor-name = Name
wg-custom-marking-editor-placement = Placement
wg-custom-marking-editor-save = Save to library
wg-custom-marking-editor-cancel = Cancel
wg-custom-marking-editor-saving = Saving...
wg-custom-marking-editor-frames = Frames
wg-custom-marking-editor-frame-count = { $frame } of { $frames }
wg-custom-marking-editor-frame-strip = Click a frame to draw on it, or drag it to move it. [ and ] go to the previous and next frame.
wg-custom-marking-editor-frame-seconds = { $seconds }s
wg-custom-marking-editor-frame-add = Add a frame after this one, as a copy of it. A marking with more than one frame is animated.
wg-custom-marking-editor-frame-remove = Remove this frame
wg-custom-marking-editor-frame-play = Play the frames (Space)
wg-custom-marking-editor-frame-reverse = Put the frames in the opposite order
wg-custom-marking-editor-frame-time-all = Give every frame this frame's time
wg-custom-marking-editor-frame-ping-pong = Play back to the first frame at the end, instead of starting over. Needs three frames or more.
wg-custom-marking-editor-frame-needs-two = Needs two frames or more.
wg-custom-marking-editor-frame-max = A marking can have at most { $max } frames.
wg-custom-marking-editor-onion-skin = Onion skin
wg-custom-marking-editor-onion-skin-tooltip = Show the frame before this one faintly over it, where they differ.
wg-custom-marking-editor-frame-time = Seconds
wg-custom-marking-editor-frame-time-tooltip = How long this frame shows, from { $min } to { $max } seconds.
wg-custom-marking-editor-erase-too-much = Too much erased: at least { $percent }% of the body has to stay or be drawn over.
wg-custom-marking-editor-reach = A marking stays on the character: you can draw on the body and its other markings, such as a tail or ears, and up to { $margin } pixels around them. The line marks how far that goes.

wg-custom-marking-layer-drawing = Drawing
wg-custom-marking-layer-drawing-tooltip = Draw the marking itself (1).
wg-custom-marking-layer-erase = Erase body
wg-custom-marking-layer-erase-tooltip = Hide the character's own sprite under the marking, so what you draw takes its place (2). At least { $percent }% of the body has to stay or be drawn over.
wg-custom-marking-layer-glow = Glow
wg-custom-marking-layer-glow-tooltip = Make drawn pixels glow, so they stay bright in the dark (3). Glowing pixels keep their glow when recolored.

wg-custom-marking-tool-pencil = { $layer ->
    [erase] Pencil (B): erases the body
    [glow] Pencil (B): makes pixels glow
    *[drawing] Pencil (B): draws
}
wg-custom-marking-tool-eraser = { $layer ->
    [erase] Eraser (E): brings the body back
    [glow] Eraser (E): stops pixels glowing
    *[drawing] Eraser (E): clears pixels
}
wg-custom-marking-tool-fill = { $layer ->
    [glow] Fill (F): makes a same-colored patch glow
    *[other] Fill (F): recolors a same-colored patch
}
wg-custom-marking-tool-fill-erase = Erasing the body has no colors to fill.
wg-custom-marking-tool-picker = Pick color (I): takes the color of a pixel you drew, or of the body under it

wg-custom-marking-hint-pencil = { $layer ->
    [erase] Left click erases the body, right click brings it back.
    [glow] Left click makes drawn pixels glow, right click stops them.
    *[drawing] Left click draws, right click clears.
}
wg-custom-marking-hint-eraser = { $layer ->
    [erase] Left click brings the body back.
    [glow] Left click stops pixels glowing.
    *[drawing] Left click clears pixels.
}
wg-custom-marking-hint-fill = { $layer ->
    [glow] Left click makes a same-colored patch glow, right click stops it.
    *[other] Left click fills a same-colored patch, right click clears it.
}
wg-custom-marking-hint-picker = Click to take a color, then draw with it.
wg-custom-marking-hint-keys = Shift+click draws a line, Alt+click picks a color.

wg-custom-marking-facing-south = Front
wg-custom-marking-facing-north = Back
wg-custom-marking-facing-east = Facing right
wg-custom-marking-facing-west = Facing left

wg-custom-marking-placement-behind = Behind the body
wg-custom-marking-placement-behind-hint = Drawn behind the whole body, over a tail or wings there: use it to draw on those as seen from the front. What lies on a tail goes when clothing hides the tail.
wg-custom-marking-placement-behind-short = Behind the body
wg-custom-marking-placement-skin = On the skin
wg-custom-marking-placement-skin-hint = Drawn on the body, under underwear and clothing. The hands and feet draw over it; use On the hands and feet for those.
wg-custom-marking-placement-skin-short = Under clothing
wg-custom-marking-placement-hands = On the hands and feet
wg-custom-marking-placement-hands-hint = Drawn over the hands and feet, under gloves and shoes. Anything drawn elsewhere shows over the uniform.
wg-custom-marking-placement-hands-short = Over hands and feet
wg-custom-marking-placement-hair = Over the hair
wg-custom-marking-placement-hair-hint = Drawn over the head, hair and ears, under hats and masks: use it for changes to the hair. Like hair, all of it is hidden whenever headgear hides the hair.
wg-custom-marking-placement-hair-short = Hidden by headgear
wg-custom-marking-placement-front = Over clothing
wg-custom-marking-placement-front-hint = Drawn over everything, clothing and cloaks included: use it to draw on a tail seen from behind. What lies on a tail goes when clothing hides the tail.
wg-custom-marking-placement-front-short = Over everything
wg-custom-marking-placement-undergarmenttop = Underwear (Top)
wg-custom-marking-placement-undergarmenttop-hint = Treated as an undershirt, rendering under clothing.
wg-custom-marking-placement-undergarmenttop-short = Undershirt layer
wg-custom-marking-placement-undergarmentbottom = Underwear (Button)
wg-custom-marking-placement-undergarmentbottom-hint = Treated as underwear, rendering under clothing. Will hide anatomy when visible, if anatomy is configured as such.
wg-custom-marking-placement-undergarmentbottom-short = Underwear layer

wg-custom-marking-error-disabled = Custom markings are turned off on this server.
wg-custom-marking-error-cooldown = Wait a moment before saving again.
wg-custom-marking-error-invalid = That marking couldn't be read.
wg-custom-marking-error-blank = Draw something first.
wg-custom-marking-error-failed = The marking couldn't be saved. Try again.
wg-custom-marking-error-missing = That marking is no longer in your library.
wg-custom-marking-error-full = Your library is full. Delete a marking to make room.
wg-custom-marking-error-blocked = That art has been blocked by an admin.
wg-custom-marking-error-daily = You have saved as many new drawings as the server allows in a day. Try again tomorrow.
wg-custom-marking-error-import = That file isn't a 64x64 or 32x32 PNG, or a sheet exported from here.
wg-custom-marking-error-export = The file couldn't be written.

wg-custom-marking-admin-remove = Remove custom markings
wg-custom-marking-admin-block = Block custom markings

cmd-custommarkingblock-desc = Blocks custom marking art by hash, taking it off every character and refusing it from now on, or lifts a block.
cmd-custommarkingblock-help = Usage: { $command } <hash> [blocked: true/false]
cmd-custommarkingblock-invalid-args = Expected an art hash from the admin log, and optionally true or false.
cmd-custommarkingblock-unknown = No art has that hash.
cmd-custommarkingblock-blocked = Art blocked.
cmd-custommarkingblock-unblocked = Art unblocked.

wg-custom-marking-close-title = Unsaved changes
wg-custom-marking-close-text = This marking has unsaved changes. Save them before closing?
wg-custom-marking-close-save = Save
wg-custom-marking-close-discard = Close without saving
wg-custom-marking-close-keep = Keep editing
