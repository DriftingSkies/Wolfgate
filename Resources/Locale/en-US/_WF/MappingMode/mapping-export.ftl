## Export wizard

wf-mapping-export-title = Export
wf-mapping-export-button = Export...
wf-mapping-export-button-tooltip = Check the map, fill in the ship details and download one zip laid out like the repository.
wf-mapping-export-library-button = Export
wf-mapping-export-library-tooltip = Export this version: a ship or a map as one zip laid out like the repository, or just the grid file.
wf-mapping-export-library-latest-tooltip = Export the newest version: a ship or a map as one zip laid out like the repository, or just the grid file.
wf-mapping-export-library-download-tooltip = Download the plain level file to your computer.
wf-mapping-export-step = Step {$step} of 3: {$name}
wf-mapping-export-step-check = Check
wf-mapping-export-step-details = Details
wf-mapping-export-step-package = Package
wf-mapping-export-target = {$name}, {$version}
wf-mapping-export-target-newest = newest version
wf-mapping-export-back = Back
wf-mapping-export-next = Next
wf-mapping-export-kind = Export as
wf-mapping-export-kind-ship = Ship
wf-mapping-export-kind-ship-tooltip = One grid on one level, packaged as a shipyard vessel: grid file, vessel prototype and README.
wf-mapping-export-kind-map = Map
wf-mapping-export-kind-map-tooltip = Every level as a map file, with a gameMap or zMap prototype and README.
wf-mapping-export-kind-grid = Grid
wf-mapping-export-kind-grid-tooltip = Just the grid file, for POIs and other grids that aren't ships. No prototype, no zip.
wf-mapping-export-recheck = Check again
wf-mapping-export-checking = Checking...
wf-mapping-export-check-too-soon = The server runs one check every three seconds; checking again shortly...
wf-mapping-export-check-timeout = The check didn't answer. Press Check again.
wf-mapping-export-check-none = No issues found.
wf-mapping-export-check-summary = {$errors ->
    [one] 1 error
   *[other] {$errors} errors
}, {$warnings ->
    [one] 1 warning
   *[other] {$warnings} warnings
}, {$notes ->
    [one] 1 note
   *[other] {$notes} notes
} over {$grids ->
    [one] 1 grid
   *[other] {$grids} grids
}, {$tiles} tiles, {$entities} entities on {$levels ->
    [one] 1 level
   *[other] {$levels} levels
}. Appraised at {$appraisal}.
wf-mapping-export-check-no-session = You aren't in this map's session, so nothing is checked. Open the map to check it before exporting.
wf-mapping-export-check-errors = A ship can't be exported while an error stands. Fix them and check again, or export as a map instead.
wf-mapping-export-check-ok-ship = No errors stand: the ship can be exported.
wf-mapping-export-check-ok-map = Warnings don't stop a map export.
wf-mapping-export-check-ok-grid = Nothing stops a grid export; the issues are for your information.
wf-mapping-export-issue-goto = Go to
wf-mapping-export-severity-error = Error
wf-mapping-export-severity-warning = Warning
wf-mapping-export-severity-note = Note
wf-mapping-export-details-name = Ship name
wf-mapping-export-details-name-hint = Leave empty to use the map name. Letters, digits, spaces, dashes and underscores.
wf-mapping-export-details-preview = Id {$id}, files {$file}.yml
wf-mapping-export-details-description = Description
wf-mapping-export-details-description-count = {$count} of {$max} characters
wf-mapping-export-details-price = Price
wf-mapping-export-details-price-hint = Appraised at {$appraisal}; the price must be at least {$floor}. Suggested {$suggested}. Empty or 0 takes the suggestion.
wf-mapping-export-details-price-hint-unknown = The appraisal is only known in the map's session; empty or 0 takes the server's suggestion.
wf-mapping-export-details-price-bad = The price must be a whole number between 0 and {$max}.
wf-mapping-export-details-category = Size
wf-mapping-export-details-auto = Auto
wf-mapping-export-details-classes = Classes
wf-mapping-export-details-classes-hint = Tick none to take the classes found on the grid.
wf-mapping-export-details-group = Shipyard group
wf-mapping-export-details-engines = Engines
wf-mapping-export-details-engines-auto = From the grid
wf-mapping-export-details-access = Access
wf-mapping-export-details-access-hint = The access level needed to buy it, such as Command. Letters, digits and underscores; empty is none.
wf-mapping-export-details-require-crew = Requires crew
wf-mapping-export-details-map = A map or grid export takes no details: a map's levels are packaged with a gameMap or zMap prototype, a grid is the bare file.
wf-mapping-export-package-grid = The download is the one grid file, {$file}. No prototype, no zip.
wf-mapping-export-package-ship = The zip holds {$grid}, {$proto} and README.txt. Unzip it on the repository root.
wf-mapping-export-package-map = The zip holds {$levels ->
    [one] 1 level file
   *[other] {$levels} level files
} under {$folder}, {$proto} and README.txt. Unzip it on the repository root.
wf-mapping-export-package-save-first = The session has unsaved changes: a version is saved first, then exported.
wf-mapping-export-package-save-to-hint = The save dialog doesn't add .zip: type the whole name, {$file}.
wf-mapping-export-package-save-to-hint-grid = The save dialog doesn't add .yml: type the whole name, {$file}.
wf-mapping-export-package-button = Download zip
wf-mapping-export-package-button-save-to = Save zip to...
wf-mapping-export-package-button-grid = Download grid
wf-mapping-export-package-button-save-to-grid = Save grid to...
wf-mapping-export-package-saving = Saving a version...
wf-mapping-export-package-save-timeout = The save didn't finish; try again.
wf-mapping-export-package-requested = Packaging...
wf-mapping-export-package-done = The zip is downloaded.
wf-mapping-export-package-done-grid = The grid file is downloaded.
wf-mapping-export-package-open-folder = Open folder

## Server replies

wf-mapping-export-too-soon = Wait a few seconds between exports.
wf-mapping-export-failed = The export failed.
wf-mapping-export-errors = {$count ->
    [one] 1 error stands
   *[other] {$count} errors stand
} in the check; fix them or export as a map.
wf-mapping-export-goto-gone = That entity is gone.

## README.txt and the prototype header

wf-mapping-export-readme-ship-intro = Ship "{$name}", version {$version}, exported from Wolfgate mapping mode.
wf-mapping-export-readme-map-intro = Map "{$name}", version {$version} ({$levels ->
    [one] 1 level
   *[other] {$levels} levels
}), exported from Wolfgate mapping mode.
wf-mapping-export-readme-unzip = Unzip this file on the repository root; every file lands where the game expects it:
wf-mapping-export-readme-file-grid = {$path}: the ship's grid.
wf-mapping-export-readme-file-prototype = {$path}: the vessel {$id} and the gameMap that gives it a station.
wf-mapping-export-readme-file-level = {$path}: level {$depth}.
wf-mapping-export-readme-file-gamemap = {$path}: the gameMap {$id} that loads the level with a plain station.
wf-mapping-export-readme-file-zmap = {$path}: the zMap {$id} that stacks the levels.
wf-mapping-export-readme-file-readme = README.txt: this file.
wf-mapping-export-readme-check-guessed = These vessel fields were guessed from the grid, not set in the wizard: {$fields}. Check them.
wf-mapping-export-readme-check-price = The price is {$price}; the grid appraised at {$appraisal}. The shipyard test wants the price at or over the appraisal.
wf-mapping-export-readme-check-no-scan = This version kept no scan of its grid, so the guesses used an empty reading. Open and save the map again for better ones.
wf-mapping-export-readme-check-taken = The id {$id} already exists in the game. Rename it in the prototype and the file names.
wf-mapping-export-readme-check-test = Run the shipyard tests (Content.IntegrationTests, VesselShipyardTest) before opening a pull request.
wf-mapping-export-readme-check-station = The gameMap gives the map a plain station; change the station prototype and jobs to what it needs.
wf-mapping-export-readme-check-zmap = The zMap lists the levels bottom-up; the ground level (depth 0) is index {$ground}. Give the zMap its atmosphere and light components if it needs them.
wf-mapping-export-readme-issues = Issues report ({$scope} check, {$count ->
    [one] 1 issue
   *[other] {$count} issues
}) at export time:
wf-mapping-export-readme-unchecked = No check ran: the export was made outside the map's session, or from a version older than what the session holds now.
wf-mapping-export-readme-no-issues = No issues.
wf-mapping-export-readme-severity-error = Errors:
wf-mapping-export-readme-severity-warning = Warnings:
wf-mapping-export-readme-severity-note = Notes:
wf-mapping-export-proto-header = Made in Wolfgate mapping mode from "{$name}", version {$version}. Check every field before it goes in the game.

# The mapping screen's menu bar button, and the guard the check, the save, Go to and the export share
wf-mapping-screen-bar-export = Export: check the map, fill in the ship details and download one zip
wf-mapping-export-while-testing = Nothing can be checked, saved or exported while testing. Go back to mapping first.
