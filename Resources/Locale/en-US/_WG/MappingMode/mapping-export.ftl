## Export wizard

wg-mapping-export-title = Export
wg-mapping-export-button = Export...
wg-mapping-export-button-tooltip = Check the map, fill in the ship details and download one zip laid out like the repository.
wg-mapping-export-library-button = Export
wg-mapping-export-library-tooltip = Export this version: a ship or a map as one zip laid out like the repository, or just the grid file.
wg-mapping-export-library-latest-tooltip = Export the newest version: a ship or a map as one zip laid out like the repository, or just the grid file.
wg-mapping-export-library-download-tooltip = Download the plain level file to your computer.
wg-mapping-export-step = Step {$step} of 3: {$name}
wg-mapping-export-step-check = Check
wg-mapping-export-step-details = Details
wg-mapping-export-step-package = Package
wg-mapping-export-target = {$name}, {$version}
wg-mapping-export-target-newest = newest version
wg-mapping-export-back = Back
wg-mapping-export-next = Next
wg-mapping-export-kind = Export as
wg-mapping-export-kind-ship = Ship
wg-mapping-export-kind-ship-tooltip = One grid on one level, packaged as a shipyard vessel: grid file, vessel prototype and README.
wg-mapping-export-kind-map = Map
wg-mapping-export-kind-map-tooltip = Every level as a map file, with a gameMap or zMap prototype and README.
wg-mapping-export-kind-grid = Grid
wg-mapping-export-kind-grid-tooltip = Just the grid file, for POIs and other grids that aren't ships. No prototype, no zip.
wg-mapping-export-recheck = Check again
wg-mapping-export-checking = Checking...
wg-mapping-export-check-too-soon = The server runs one check every three seconds; checking again shortly...
wg-mapping-export-check-timeout = The check didn't answer. Press Check again.
wg-mapping-export-check-none = No issues found.
wg-mapping-export-check-summary = {$errors ->
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
wg-mapping-export-check-no-session = You aren't in this map's session, so nothing is checked. Open the map to check it before exporting.
wg-mapping-export-check-errors = A ship can't be exported while an error stands. Fix them and check again, or export as a map instead.
wg-mapping-export-check-ok-ship = No errors stand: the ship can be exported.
wg-mapping-export-check-ok-map = Only a map check failure stops a map export.
wg-mapping-export-check-ok-grid = Only a map check failure stops a grid export. A grid is checked as a point of interest.
wg-mapping-export-check-lint = The repository's map check refuses what's marked below. Fix it and check again.
wg-mapping-export-issue-goto = Go to
wg-mapping-export-severity-error = Error
wg-mapping-export-severity-warning = Warning
wg-mapping-export-severity-note = Note
wg-mapping-export-details-name = Ship name
wg-mapping-export-details-name-hint = Leave empty to use the map name. Letters, digits, spaces, dashes and underscores.
wg-mapping-export-details-preview = Id {$id}, files {$file}.yml
wg-mapping-export-details-description = Description
wg-mapping-export-details-description-count = {$count} of {$max} characters
wg-mapping-export-details-price = Price
wg-mapping-export-details-price-hint = Appraised at {$appraisal}; the price must be at least {$floor}. Suggested {$suggested}. Empty or 0 takes the suggestion.
wg-mapping-export-details-price-hint-unknown = The appraisal is only known in the map's session; empty or 0 takes the server's suggestion.
wg-mapping-export-details-price-bad = The price must be a whole number between 0 and {$max}.
wg-mapping-export-details-category = Size
wg-mapping-export-details-auto = Auto
wg-mapping-export-details-classes = Classes
wg-mapping-export-details-classes-hint = Tick none to take the classes found on the grid.
wg-mapping-export-details-group = Shipyard group
wg-mapping-export-details-engines = Engines
wg-mapping-export-details-engines-auto = From the grid
wg-mapping-export-details-access = Access
wg-mapping-export-details-access-hint = The access level needed to buy it, such as Command. Letters, digits and underscores; empty is none.
wg-mapping-export-details-require-crew = Requires crew
wg-mapping-export-details-map = A map or grid export takes no details: a map's levels are packaged with a gameMap or zMap prototype, a grid is the bare file.
wg-mapping-export-package-grid = The download is the one grid file, {$file}. No prototype, no zip.
wg-mapping-export-package-ship = The zip holds {$grid}, {$proto} and README.txt. Unzip it on the repository root.
wg-mapping-export-package-map = The zip holds {$levels ->
    [one] 1 level file
   *[other] {$levels} level files
} under {$folder}, {$proto} and README.txt. Unzip it on the repository root.
wg-mapping-export-package-save-first = The session has unsaved changes: a version is saved first, then exported.
wg-mapping-export-package-save-to-hint = The save dialog doesn't add .zip: type the whole name, {$file}.
wg-mapping-export-package-save-to-hint-grid = The save dialog doesn't add .yml: type the whole name, {$file}.
wg-mapping-export-package-button = Download zip
wg-mapping-export-package-button-save-to = Save zip to...
wg-mapping-export-package-button-grid = Download grid
wg-mapping-export-package-button-save-to-grid = Save grid to...
wg-mapping-export-package-saving = Saving a version...
wg-mapping-export-package-save-timeout = The save didn't finish; try again.
wg-mapping-export-package-requested = Packaging...
wg-mapping-export-package-done = The zip is downloaded.
wg-mapping-export-package-done-grid = The grid file is downloaded.
wg-mapping-export-package-open-folder = Open folder

## Server replies

wg-mapping-export-too-soon = Wait a few seconds between exports.
wg-mapping-export-failed = The export failed.
wg-mapping-export-errors = {$count ->
    [one] 1 error stands
   *[other] {$count} errors stand
} in the check; fix them or export as a map.
wg-mapping-export-lint = The map check refuses {$count ->
    [one] 1 prototype
   *[other] {$count} prototypes
}: {$names}.
wg-mapping-export-goto-gone = That entity is gone.

## README.txt and the prototype header

wg-mapping-export-readme-ship-intro = Ship "{$name}", version {$version}, exported from Wolfgate mapping mode.
wg-mapping-export-readme-map-intro = Map "{$name}", version {$version} ({$levels ->
    [one] 1 level
   *[other] {$levels} levels
}), exported from Wolfgate mapping mode.
wg-mapping-export-readme-unzip = Unzip this file on the repository root; every file lands where the game expects it:
wg-mapping-export-readme-file-grid = {$path}: the ship's grid.
wg-mapping-export-readme-file-prototype = {$path}: the vessel {$id} and the gameMap that gives it a station.
wg-mapping-export-readme-file-level = {$path}: level {$depth}.
wg-mapping-export-readme-file-gamemap = {$path}: the gameMap {$id} that loads the level with a plain station.
wg-mapping-export-readme-file-zmap = {$path}: the zMap {$id} that stacks the levels.
wg-mapping-export-readme-file-readme = README.txt: this file.
wg-mapping-export-readme-check-guessed = These vessel fields were guessed from the grid, not set in the wizard: {$fields}. Check them.
wg-mapping-export-readme-check-price = The price is {$price}; the grid appraised at {$appraisal}. The shipyard test wants the price at or over the appraisal.
wg-mapping-export-readme-check-no-scan = This version kept no scan of its grid, so the guesses used an empty reading. Open and save the map again for better ones.
wg-mapping-export-readme-check-taken = The id {$id} already exists in the game. Rename it in the prototype and the file names.
wg-mapping-export-readme-check-test = Run the shipyard tests (Content.IntegrationTests, VesselShipyardTest) before opening a pull request.
wg-mapping-export-readme-check-station = The gameMap gives the map a plain station; change the station prototype and jobs to what it needs.
wg-mapping-export-readme-check-zmap = The zMap lists the levels bottom-up; the ground level (depth 0) is index {$ground}. Give the zMap its atmosphere and light components if it needs them.
wg-mapping-export-readme-check-lint = The saved files passed the repository's map check for this kind of export.
wg-mapping-export-readme-issues = Issues report ({$scope} check, {$count ->
    [one] 1 issue
   *[other] {$count} issues
}) at export time:
wg-mapping-export-readme-unchecked = The session wasn't checked: the export was made outside the map's session, or from a version older than what the session holds now.
wg-mapping-export-readme-no-issues = No issues.
wg-mapping-export-readme-severity-error = Errors:
wg-mapping-export-readme-severity-warning = Warnings:
wg-mapping-export-readme-severity-note = Notes:
wg-mapping-export-proto-header = Made in Wolfgate mapping mode from "{$name}", version {$version}. Check every field before it goes in the game.

# The mapping screen's menu bar button, and the guard the check, the save, Go to and the export share
wg-mapping-screen-bar-export = Export: check the map, fill in the ship details and download one zip
wg-mapping-export-while-testing = Nothing can be checked, saved or exported while testing. Go back to mapping first.
