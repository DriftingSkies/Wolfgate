## Uploads

wg-mapping-mode-kind-upload = upload
wg-mapping-mode-upload-button = Upload map
wg-mapping-mode-upload-button-tooltip = Upload a map file (.yml) from your computer into your library. The server checks it and keeps its own copy.
wg-mapping-mode-upload-cancel = Cancel
wg-mapping-mode-upload-open = Open

wg-mapping-mode-upload-starting = Uploading "{$name}"...
wg-mapping-mode-upload-progress = Uploading "{$name}": {$percent}%
wg-mapping-mode-upload-checking = Checking "{$name}"...
wg-mapping-mode-upload-ready = Uploaded "{$name}" as {$version}.
wg-mapping-mode-upload-ready-dropped = Uploaded "{$name}" as {$version}, without {$dropped ->
    [one] 1 entity
   *[other] {$dropped} entities
}.
wg-mapping-mode-upload-needs-confirm = This server doesn't know {$count ->
    [one] 1 prototype
   *[other] {$count} prototypes
} used by {$entities ->
    [one] 1 entity
   *[other] {$entities} entities
}: {$ids}
wg-mapping-mode-upload-more = …
wg-mapping-mode-upload-load-anyway = Load anyway
wg-mapping-mode-upload-load-anyway-tooltip = Store the map without those entities.

wg-mapping-mode-upload-done = Uploaded "{$name}" to your library as {$version}.
wg-mapping-mode-upload-done-dropped = Uploaded "{$name}" to your library as {$version}, without {$dropped ->
    [one] 1 entity
   *[other] {$dropped} entities
} of unknown prototypes.
wg-mapping-mode-upload-busy = An upload is already running.
wg-mapping-mode-upload-cooldown = Wait {$seconds} s before uploading again.
wg-mapping-mode-upload-too-large = That file is over the {$max} MiB upload limit.
wg-mapping-mode-upload-empty-file = That file is empty.
wg-mapping-mode-upload-read-failed = That file couldn't be read.
wg-mapping-mode-upload-bad-data = The upload arrived damaged and was stopped.
wg-mapping-mode-upload-failed = The upload failed.
wg-mapping-mode-upload-stalled = The upload stopped responding and was dropped.
wg-mapping-mode-upload-cancelled = The upload was cancelled.
wg-mapping-mode-upload-confirm-expired = The upload waited too long for an answer and was dropped.
wg-mapping-mode-upload-not-text = That file isn't UTF-8 text.
wg-mapping-mode-upload-bad-yaml = That file isn't valid YAML.
wg-mapping-mode-upload-not-map = That file isn't a map file.
wg-mapping-mode-upload-anchors = Map files can't use YAML anchors or aliases.
wg-mapping-mode-upload-too-deep = That file is nested too deeply.
wg-mapping-mode-upload-load-busy = The server is loading another map. Try again in a moment.
wg-mapping-mode-upload-load-failed = That map couldn't be loaded.
