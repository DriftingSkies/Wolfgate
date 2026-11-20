## Canvases

wg-canvas-examine-blank = It is blank. Hold a crayon or a paint palette and use it on the canvas to paint.
wg-canvas-examine-wip = It is a painting in progress.
wg-canvas-examine-finished = It is [color=yellow]"{$title}"[/color], signed [color=yellow]{$artist}[/color].
wg-canvas-examine-print = It is a print from the art catalogue.
wg-canvas-no-tool = You need a crayon or a paint palette in hand to paint.
wg-canvas-untitled = Untitled
wg-canvas-anonymous = Anonymous
wg-canvas-finished-name = painting "{$title}"

wg-canvas-window-title = Canvas
wg-canvas-tool-pencil = Brush
wg-canvas-tool-fill = Fill
wg-canvas-tool-picker = Pick colour
wg-canvas-no-tool-held = Hold a crayon or a paint palette to paint.
wg-canvas-crayon-held = Painting with a crayon.
wg-canvas-palette-held = Painting with a palette.
wg-canvas-title-placeholder = Title
wg-canvas-anonymous-checkbox = Anonymous
wg-canvas-finish = Finish
wg-canvas-finish-confirm = Can't be undone!
wg-canvas-signature = "{$title}", by {$artist}
wg-canvas-help = Left click paints, right click erases. A finished painting can be framed or scanned with the art PDA app.
wg-canvas-section-tools = Tools
wg-canvas-section-colour = Colour
wg-canvas-section-palette = Palette
wg-canvas-section-recent = Recent
wg-canvas-grid = Show pixel grid
wg-canvas-save-colour = Save colour
wg-canvas-palette-count = {$count}/{$max}
wg-canvas-palette-empty = Save colours here to keep them on the palette.
wg-canvas-swatch-saved-tooltip = {$hex}. Right click removes it from the palette.

wg-painting-view-title = Painting
wg-painting-view-name = "{$title}"
wg-painting-view-artist = by {$artist}

## Frames and easels

wg-painting-frame-name = framed painting "{$title}"
wg-painting-frame-full = There's already a painting in this frame.
wg-painting-frame-unfinished = Finish the painting before framing it.
wg-painting-frame-wrong-size = That canvas doesn't fit this frame.
wg-painting-frame-cut = {CAPITALIZE(THE($user))} cuts the painting out of {THE($frame)}.
wg-painting-frame-examine-small = It takes one of the small canvases.
wg-painting-frame-examine-large = It takes one of the large canvases.
wg-painting-frame-examine-view = Click it with an empty hand for a closer look.
wg-painting-frame-examine-gallery = It hangs a painting from the art catalogue every shift.

wg-easel-slot-canvas = Canvas
wg-painting-construction-frame-kit = a painting frame kit
wg-painting-construction-frame-large-kit = a large painting frame kit
wg-painting-construction-frame-space = It can't overlap another painting frame.
wg-painting-construction-large-frame-space = It spans this wall tile and the one east of it, on a wall facing north or south, and can't overlap another painting frame.

## Art app

wg-art-app-program-name = Art Catalogue
wg-art-app-tab-catalogue = Catalogue
wg-art-app-tab-mine = My paintings
wg-art-app-search = Search titles and artists
wg-art-app-page = Page {$page} of {$pages}
wg-art-app-scan-hint = Use the PDA on a finished canvas, with this app open, to scan it into the catalogue.
wg-art-app-unavailable = The catalogue is loading. Try again shortly.
wg-art-app-none = No paintings found.
wg-art-app-none-mine = You have no paintings in the catalogue. Scan a finished painting to add it.
wg-art-app-entry = by {$artist}, {$width}x{$height}
wg-art-app-order = Order ({$price} thlr)
wg-art-app-order-confirm = Pay {$price} thlr?
wg-art-app-remove = Remove
wg-art-app-remove-confirm = Remove it?

wg-art-app-scan-stored = {CAPITALIZE(THE($canvas))} is scanned into the art catalogue.
wg-art-app-scan-unfinished = Finish the painting before scanning it.
wg-art-app-scan-blank = There's nothing painted to scan.
wg-art-app-scan-known = That painting is already in the catalogue.
wg-art-app-scan-blocked = The catalogue refuses that painting.
wg-art-app-scan-dailylimit = You've scanned as many paintings as the catalogue takes from one person today.
wg-art-app-scan-unavailable = The catalogue can't be reached right now.

wg-art-app-removed = The painting is taken out of the catalogue.
wg-art-app-remove-failed = That painting couldn't be taken out of the catalogue.

wg-art-app-order-gone = That painting is no longer in the catalogue.
wg-art-app-order-no-mail = There is no mail service to deliver a print.
wg-art-app-order-no-recipient = Mail can't find you. Carry an ID card with your name on it.
wg-art-app-order-no-teleporter = No mail teleporter can reach you.
wg-art-app-order-funds = You can't afford a print. It costs {$price} thlr.
wg-art-app-ordered = A print of "{$title}" is ordered for {$price} thlr. It comes with the next mail delivery.

## Admin command

cmd-paintingblock-desc = Blocks a painting from the art catalogue, or lifts a block.
cmd-paintingblock-help = Usage: paintingblock <hash> [true/false]. The hash is in the painting's admin log entries.
cmd-paintingblock-invalid-args = Give a painting's hash, and optionally true to block or false to unblock.
cmd-paintingblock-unknown = No painting has that hash.
cmd-paintingblock-blocked = The painting is blocked. Copies in the round are deleted.
cmd-paintingblock-unblocked = The painting is unblocked.

## Admin painting browser

wg-painting-admin-title = Painting browser
wg-painting-admin-search = Search every value: title, artist, player, hash, size, date...
wg-painting-admin-help = Every word must match. Narrow a word to one field with title:, artist:, author:, scanner:, hash:, size: or date: (date:2026-11). Quote a phrase to keep it whole.
wg-painting-admin-filter-all = All
wg-painting-admin-filter-visible = Visible
wg-painting-admin-filter-blocked = Blocked
wg-painting-admin-count = {$shown} of {$total} shown
wg-painting-admin-loading = The catalogue is loading.
wg-painting-admin-none = No painting matches.
wg-painting-admin-select = Select a painting to see it.
wg-painting-admin-preview-loading = Loading the painting...
wg-painting-admin-no-player = none
wg-painting-admin-column-title = Title
wg-painting-admin-column-artist = Artist
wg-painting-admin-column-author = Author
wg-painting-admin-column-size = Size
wg-painting-admin-column-date = Scanned
wg-painting-admin-column-status = Status
wg-painting-admin-status-visible = Visible
wg-painting-admin-status-blocked = Blocked
wg-painting-admin-field-title = Title
wg-painting-admin-field-artist = Signature
wg-painting-admin-field-author = Painted by (player)
wg-painting-admin-field-scanner = Scanned by (player)
wg-painting-admin-field-size = Size
wg-painting-admin-field-date = Scanned on
wg-painting-admin-field-hash = Hash
wg-painting-admin-field-status = Status
wg-painting-admin-block = Block
wg-painting-admin-unblock = Unblock
wg-painting-admin-delete = Delete from database
wg-painting-admin-delete-confirm = Delete it and every copy?

cmd-paintingbrowser-desc = Opens the admin painting browser, to search, block and delete stored paintings.
cmd-paintingbrowser-help = Usage: paintingbrowser
