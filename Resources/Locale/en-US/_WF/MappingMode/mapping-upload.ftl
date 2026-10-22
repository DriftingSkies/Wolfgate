## Uploads

wf-mapping-mode-kind-upload = upload
wf-mapping-mode-upload-button = Upload map
wf-mapping-mode-upload-button-tooltip = Upload a map file (.yml) from your computer into your library. The server checks it and keeps its own copy.
wf-mapping-mode-upload-cancel = Cancel
wf-mapping-mode-upload-open = Open

wf-mapping-mode-upload-starting = Uploading "{$name}"...
wf-mapping-mode-upload-progress = Uploading "{$name}": {$percent}%
wf-mapping-mode-upload-checking = Checking "{$name}"...
wf-mapping-mode-upload-ready = Uploaded "{$name}" as {$version}.
wf-mapping-mode-upload-ready-dropped = Uploaded "{$name}" as {$version}, without {$dropped ->
    [one] 1 entity
   *[other] {$dropped} entities
}.
wf-mapping-mode-upload-needs-confirm = This server doesn't know {$count ->
    [one] 1 prototype
   *[other] {$count} prototypes
} used by {$entities ->
    [one] 1 entity
   *[other] {$entities} entities
}: {$ids}
wf-mapping-mode-upload-more = …
wf-mapping-mode-upload-load-anyway = Load anyway
wf-mapping-mode-upload-load-anyway-tooltip = Store the map without those entities.

wf-mapping-mode-upload-done = Uploaded "{$name}" to your library as {$version}.
wf-mapping-mode-upload-done-dropped = Uploaded "{$name}" to your library as {$version}, without {$dropped ->
    [one] 1 entity
   *[other] {$dropped} entities
} of unknown prototypes.
wf-mapping-mode-upload-busy = An upload is already running.
wf-mapping-mode-upload-cooldown = Wait {$seconds} s before uploading again.
wf-mapping-mode-upload-too-large = That file is over the {$max} MiB upload limit.
wf-mapping-mode-upload-empty-file = That file is empty.
wf-mapping-mode-upload-read-failed = That file couldn't be read.
wf-mapping-mode-upload-bad-data = The upload arrived damaged and was stopped.
wf-mapping-mode-upload-failed = The upload failed.
wf-mapping-mode-upload-stalled = The upload stopped responding and was dropped.
wf-mapping-mode-upload-cancelled = The upload was cancelled.
wf-mapping-mode-upload-confirm-expired = The upload waited too long for an answer and was dropped.
wf-mapping-mode-upload-not-text = That file isn't UTF-8 text.
wf-mapping-mode-upload-bad-yaml = That file isn't valid YAML.
wf-mapping-mode-upload-not-map = That file isn't a map file.
wf-mapping-mode-upload-anchors = Map files can't use YAML anchors or aliases.
wf-mapping-mode-upload-too-deep = That file is nested too deeply.
wf-mapping-mode-upload-load-busy = The server is loading another map. Try again in a moment.
wf-mapping-mode-upload-load-failed = That map couldn't be loaded.
