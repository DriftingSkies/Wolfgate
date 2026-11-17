wg-custom-marking-creator-button = { $worn ->
    [0] Open library
    *[other] Open library ({ $worn } worn)
}
wg-custom-marking-creator-hint = Draw or import your own markings in the library. Press one below to put it on this character or take it off.
wg-custom-marking-tile-tooltip = { $name } ({ $placement })
wg-custom-marking-default-name = Custom marking

wg-custom-marking-library-title = Custom markings
wg-custom-marking-library-heading = Your marking library
wg-custom-marking-library-hint = Draw your own markings and keep them here for any of your characters. Everything you save is stored on the server, shown to other players and covered by the server rules.
wg-custom-marking-library-new = Draw new
wg-custom-marking-library-import = Import PNG
wg-custom-marking-library-import-tooltip = A 64x64 image holding the four facings (south and north on top, east and west below), or a 32x32 image of the south facing alone. A sheet exported from an animated marking works too; how long its frames show and what it erases are not in the file.
wg-custom-marking-library-counts = { $saved } of { $limit } saved, { $worn } of { $max } worn
wg-custom-marking-library-loading = Loading your library...
wg-custom-marking-library-empty = Nothing here yet. Press Draw new to make your first marking.
wg-custom-marking-library-wear = Wear
wg-custom-marking-library-take-off = Take off
wg-custom-marking-library-edit = Edit this marking
wg-custom-marking-library-export = Export this marking as a PNG file to your CustomMarkings folder
wg-custom-marking-exported = Saved {$file} to your CustomMarkings folder.
wg-custom-marking-library-delete = Delete this marking
wg-custom-marking-library-delete-confirm = Really delete?
wg-custom-marking-library-keep = Add to library
wg-custom-marking-library-keep-tooltip = This character wears a marking that isn't in your library. Add it to keep it for other characters and to edit it.
wg-custom-marking-library-stray = Not in your library

wg-custom-marking-editor-title-new = New custom marking
wg-custom-marking-editor-title-edit = Edit custom marking
wg-custom-marking-editor-undo = Undo
wg-custom-marking-editor-redo = Redo
wg-custom-marking-editor-symmetry = Mirror drawing: what you draw on one side of the body's middle is drawn on the other side too.
wg-custom-marking-editor-flip = Flip this facing left to right, about the body's middle
wg-custom-marking-editor-mirror = Copy this facing, flipped, onto the other side view
wg-custom-marking-editor-nudge-left = Move this facing one pixel left
wg-custom-marking-editor-nudge-right = Move this facing one pixel right
wg-custom-marking-editor-nudge-up = Move this facing one pixel up
wg-custom-marking-editor-nudge-down = Move this facing one pixel down
wg-custom-marking-editor-clear = Clear this facing
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
wg-custom-marking-editor-name = Name
wg-custom-marking-editor-placement = Placement
wg-custom-marking-editor-save = Save to library
wg-custom-marking-editor-cancel = Cancel
wg-custom-marking-editor-saving = Saving...
wg-custom-marking-editor-frames = Frames
wg-custom-marking-editor-frame-count = { $frame } of { $frames }
wg-custom-marking-editor-frame-previous = Previous frame
wg-custom-marking-editor-frame-next = Next frame
wg-custom-marking-editor-frame-add = Add a frame after this one, as a copy of it. A marking with more than one frame is animated.
wg-custom-marking-editor-frame-remove = Remove this frame
wg-custom-marking-editor-frame-play = Play the frames
wg-custom-marking-editor-frame-move-left = Move this frame earlier
wg-custom-marking-editor-frame-move-right = Move this frame later
wg-custom-marking-editor-frame-reverse = Put the frames in the opposite order
wg-custom-marking-editor-frame-time-all = Give every frame this frame's time
wg-custom-marking-editor-frame-ping-pong = Play back to the first frame at the end, instead of starting over. Needs three frames or more.
wg-custom-marking-editor-onion-skin = Onion skin
wg-custom-marking-editor-onion-skin-tooltip = Show the frame before this one faintly over it, where they differ.
wg-custom-marking-editor-frame-time = Seconds
wg-custom-marking-editor-frame-time-tooltip = How long this frame shows, from { $min } to { $max } seconds.
wg-custom-marking-editor-erase-too-much = Too much erased: at least { $percent }% of the body has to stay or be drawn over.
wg-custom-marking-editor-reach = A marking stays on the character: you can draw on the body and its other markings, such as a tail or ears, and up to { $margin } pixels around them. The line marks how far that goes.

wg-custom-marking-tool-pencil = Pencil: left click draws. Right click erases with any tool.
wg-custom-marking-tool-eraser = Eraser: clears the pixels you drag over.
wg-custom-marking-tool-fill = Fill: recolors the pixel you click and every same-colored pixel touching it.
wg-custom-marking-tool-bodyeraser = Erase body: left click hides the character's own sprite under the marking, so what you draw takes its place. Right click brings it back. While this is picked, the move, flip, copy and clear buttons work on what is erased.
wg-custom-marking-tool-picker = Pick color: takes the color of a pixel you drew, or of the body showing under it.

wg-custom-marking-facing-south = Front
wg-custom-marking-facing-north = Back
wg-custom-marking-facing-east = Facing right
wg-custom-marking-facing-west = Facing left

wg-custom-marking-placement-behind = Behind the body
wg-custom-marking-placement-behind-hint = Drawn behind the whole body, over a tail or wings there: use it to draw on those as seen from the front. What lies on a tail goes when clothing hides the tail.
wg-custom-marking-placement-skin = On the skin
wg-custom-marking-placement-skin-hint = Drawn on the body, under underwear and clothing. The hands and feet draw over it; use On the hands and feet for those.
wg-custom-marking-placement-hands = On the hands and feet
wg-custom-marking-placement-hands-hint = Drawn over the hands and feet, under gloves and shoes. Anything drawn elsewhere shows over the uniform.
wg-custom-marking-placement-hair = Over the hair
wg-custom-marking-placement-hair-hint = Drawn over the head, hair and ears, under hats and masks: use it for changes to the hair. Like hair, all of it is hidden whenever headgear hides the hair.
wg-custom-marking-placement-front = Over clothing
wg-custom-marking-placement-front-hint = Drawn over everything, clothing and cloaks included: use it to draw on a tail seen from behind. What lies on a tail goes when clothing hides the tail.

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
