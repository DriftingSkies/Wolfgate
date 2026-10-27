wf-custom-marking-creator-button = { $worn ->
    [0] Open library
    *[other] Open library ({ $worn } worn)
}
wf-custom-marking-creator-hint = Draw or import your own markings in the library. Press one below to put it on this character or take it off.
wf-custom-marking-tile-tooltip = { $name } ({ $placement })
wf-custom-marking-default-name = Custom marking

wf-custom-marking-library-title = Custom markings
wf-custom-marking-library-heading = Your marking library
wf-custom-marking-library-hint = Draw your own markings and keep them here for any of your characters. Everything you save is stored on the server, shown to other players and covered by the server rules.
wf-custom-marking-library-new = Draw new
wf-custom-marking-library-import = Import PNG
wf-custom-marking-library-import-tooltip = A 64x64 image holding the four facings (south and north on top, east and west below), or a 32x32 image of the south facing alone. A sheet exported from an animated marking works too; how long its frames show and what it erases are not in the file.
wf-custom-marking-library-counts = { $saved } of { $limit } saved, { $worn } of { $max } worn
wf-custom-marking-library-loading = Loading your library...
wf-custom-marking-library-empty = Nothing here yet. Press Draw new to make your first marking.
wf-custom-marking-library-wear = Wear
wf-custom-marking-library-take-off = Take off
wf-custom-marking-library-edit = Edit this marking
wf-custom-marking-library-export = Export this marking as a PNG file to your CustomMarkings folder
wf-custom-marking-exported = Saved {$file} to your CustomMarkings folder.
wf-custom-marking-library-delete = Delete this marking
wf-custom-marking-library-delete-confirm = Really delete?
wf-custom-marking-library-keep = Add to library
wf-custom-marking-library-keep-tooltip = This character wears a marking that isn't in your library. Add it to keep it for other characters and to edit it.
wf-custom-marking-library-stray = Not in your library

wf-custom-marking-editor-title-new = New custom marking
wf-custom-marking-editor-title-edit = Edit custom marking
wf-custom-marking-editor-undo = Undo
wf-custom-marking-editor-redo = Redo
wf-custom-marking-editor-symmetry = Mirror drawing: what you draw on one side of the body's middle is drawn on the other side too.
wf-custom-marking-editor-flip = Flip this facing left to right, about the body's middle
wf-custom-marking-editor-mirror = Copy this facing, flipped, onto the other side view
wf-custom-marking-editor-nudge-left = Move this facing one pixel left
wf-custom-marking-editor-nudge-right = Move this facing one pixel right
wf-custom-marking-editor-nudge-up = Move this facing one pixel up
wf-custom-marking-editor-nudge-down = Move this facing one pixel down
wf-custom-marking-editor-clear = Clear this facing
wf-custom-marking-editor-facings = Facing
wf-custom-marking-editor-show-body = Show body
wf-custom-marking-editor-show-clothes = Show clothes
wf-custom-marking-editor-show-grid = Show grid
wf-custom-marking-editor-show-hair = Show hair
wf-custom-marking-editor-show-hair-tooltip = Shows or hides the character's hair and beard here, to see and draw on what is under them. The character keeps them.
wf-custom-marking-editor-opacity = Opacity
wf-custom-marking-editor-opacity-tooltip = Opacity in percent, from 5 to 100.
wf-custom-marking-editor-colour-skin = Your character's skin color
wf-custom-marking-editor-colour-hair = Your character's hair color
wf-custom-marking-editor-colour-facial-hair = Your character's facial hair color
wf-custom-marking-editor-colour-eyes = Your character's eye color
wf-custom-marking-editor-name = Name
wf-custom-marking-editor-placement = Placement
wf-custom-marking-editor-save = Save to library
wf-custom-marking-editor-cancel = Cancel
wf-custom-marking-editor-saving = Saving...
wf-custom-marking-editor-frames = Frames
wf-custom-marking-editor-frame-count = { $frame } of { $frames }
wf-custom-marking-editor-frame-previous = Previous frame
wf-custom-marking-editor-frame-next = Next frame
wf-custom-marking-editor-frame-add = Add a frame after this one, as a copy of it. A marking with more than one frame is animated.
wf-custom-marking-editor-frame-remove = Remove this frame
wf-custom-marking-editor-frame-play = Play the frames
wf-custom-marking-editor-frame-move-left = Move this frame earlier
wf-custom-marking-editor-frame-move-right = Move this frame later
wf-custom-marking-editor-frame-reverse = Put the frames in the opposite order
wf-custom-marking-editor-frame-time-all = Give every frame this frame's time
wf-custom-marking-editor-frame-ping-pong = Play back to the first frame at the end, instead of starting over. Needs three frames or more.
wf-custom-marking-editor-onion-skin = Onion skin
wf-custom-marking-editor-onion-skin-tooltip = Show the frame before this one faintly over it, where they differ.
wf-custom-marking-editor-frame-time = Seconds
wf-custom-marking-editor-frame-time-tooltip = How long this frame shows, from { $min } to { $max } seconds.
wf-custom-marking-editor-erase-too-much = Too much erased: at least { $percent }% of the body has to stay or be drawn over.
wf-custom-marking-editor-reach = A marking stays on the character: you can draw on the body and its other markings, such as a tail or ears, and up to { $margin } pixels around them. The line marks how far that goes.

wf-custom-marking-tool-pencil = Pencil: left click draws. Right click erases with any tool.
wf-custom-marking-tool-eraser = Eraser: clears the pixels you drag over.
wf-custom-marking-tool-fill = Fill: recolors the pixel you click and every same-colored pixel touching it.
wf-custom-marking-tool-bodyeraser = Erase body: left click hides the character's own sprite under the marking, so what you draw takes its place. Right click brings it back. While this is picked, the move, flip, copy and clear buttons work on what is erased.
wf-custom-marking-tool-picker = Pick color: takes the color of a pixel you drew, or of the body showing under it.

wf-custom-marking-facing-south = Front
wf-custom-marking-facing-north = Back
wf-custom-marking-facing-east = Facing right
wf-custom-marking-facing-west = Facing left

wf-custom-marking-placement-behind = Behind the body
wf-custom-marking-placement-behind-hint = Drawn behind the whole body, over a tail or wings there: use it to draw on those as seen from the front. What lies on a tail goes when clothing hides the tail.
wf-custom-marking-placement-skin = On the skin
wf-custom-marking-placement-skin-hint = Drawn on the body, under underwear and clothing. The hands and feet draw over it; use On the hands and feet for those.
wf-custom-marking-placement-hands = On the hands and feet
wf-custom-marking-placement-hands-hint = Drawn over the hands and feet, under gloves and shoes. Anything drawn elsewhere shows over the uniform.
wf-custom-marking-placement-hair = Over the hair
wf-custom-marking-placement-hair-hint = Drawn over the head, hair and ears, under hats and masks: use it for changes to the hair. Like hair, all of it is hidden whenever headgear hides the hair.
wf-custom-marking-placement-front = Over clothing
wf-custom-marking-placement-front-hint = Drawn over everything, clothing and cloaks included: use it to draw on a tail seen from behind. What lies on a tail goes when clothing hides the tail.

wf-custom-marking-error-disabled = Custom markings are turned off on this server.
wf-custom-marking-error-cooldown = Wait a moment before saving again.
wf-custom-marking-error-invalid = That marking couldn't be read.
wf-custom-marking-error-blank = Draw something first.
wf-custom-marking-error-failed = The marking couldn't be saved. Try again.
wf-custom-marking-error-missing = That marking is no longer in your library.
wf-custom-marking-error-full = Your library is full. Delete a marking to make room.
wf-custom-marking-error-blocked = That art has been blocked by an admin.
wf-custom-marking-error-daily = You have saved as many new drawings as the server allows in a day. Try again tomorrow.
wf-custom-marking-error-import = That file isn't a 64x64 or 32x32 PNG, or a sheet exported from here.
wf-custom-marking-error-export = The file couldn't be written.

wf-custom-marking-admin-remove = Remove custom markings
wf-custom-marking-admin-block = Block custom markings

cmd-custommarkingblock-desc = Blocks custom marking art by hash, taking it off every character and refusing it from now on, or lifts a block.
cmd-custommarkingblock-help = Usage: { $command } <hash> [blocked: true/false]
cmd-custommarkingblock-invalid-args = Expected an art hash from the admin log, and optionally true or false.
cmd-custommarkingblock-unknown = No art has that hash.
cmd-custommarkingblock-blocked = Art blocked.
cmd-custommarkingblock-unblocked = Art unblocked.

wf-custom-marking-close-title = Unsaved changes
wf-custom-marking-close-text = This marking has unsaved changes. Save them before closing?
wf-custom-marking-close-save = Save
wf-custom-marking-close-discard = Close without saving
wf-custom-marking-close-keep = Keep editing
