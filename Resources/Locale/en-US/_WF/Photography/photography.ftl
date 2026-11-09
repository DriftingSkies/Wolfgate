wf-photo-camera-examine-film = It has [color=yellow]{$count}[/color] photos left.
wf-photo-camera-examine-size = It takes photos [color=yellow]{$size}x{$size}[/color] meters across.
wf-photo-camera-examine-colour = It prints in colour.
wf-photo-camera-examine-monochrome = It prints in monochrome.

wf-photo-camera-verb-settings = Camera settings
wf-photo-camera-verb-zoom = Zoom
wf-photo-camera-verb-colour = Colour
wf-photo-camera-verb-flash = Flash
wf-photo-camera-verb-labels = Label photos
wf-photo-camera-verb-size-option = {$size}x{$size}
wf-photo-camera-verb-next-size = Next size
wf-photo-camera-verb-colour-option = Colour
wf-photo-camera-verb-monochrome-option = Monochrome
wf-photo-camera-verb-on = On
wf-photo-camera-verb-off = Off
wf-photo-camera-verb-current = Current setting

wf-photo-camera-size-set = The photos will be {$size}x{$size} meters.
wf-photo-camera-monochrome-on = Printing in monochrome.
wf-photo-camera-monochrome-off = Printing in colour.
wf-photo-camera-charging = The flash is still charging!
wf-photo-camera-busy = The image is still developing!
wf-photo-camera-no-film = The film cartridge is used up. Replace it first.
wf-photo-camera-not-empty = The camera still has film in it.
wf-photo-camera-film-loaded = You replace the film cartridge. The camera can take {$count} photos.
wf-photo-camera-film-left = {$count} photos left.
wf-photo-camera-too-far = That's too far away to photograph.
wf-photo-camera-cant-see = You can't see that.
wf-photo-camera-failed = The photo didn't come out.

wf-photo-caption-area = This is a photo of an area of {$size} meters by {$size} meters.
wf-photo-caption-mob = You can also see {$name} on the photo.
wf-photo-caption-mob-hurt = You can also see {$name} on the photo, looking a bit hurt.
wf-photo-caption-holding = {CAPITALIZE(SUBJECT($mob))} {CONJUGATE-BE($mob)} holding {$items}.
wf-photo-caption-item = {INDEFINITE($item)} {$item}
wf-photo-caption-ghost = You can also see a g-g-g-g-ghooooost!

wf-photo-default-name = photo
wf-photo-renamed = photo - '{$name}'
wf-photo-customised-description = {$custom} - {$caption}
wf-photo-customise-title = Customise photo
wf-photo-customise-name = Name (optional)
wf-photo-customise-description = Description (optional)
wf-photo-customise-caption = Caption on the back (optional)
wf-photo-verb-rename = Rename photo
wf-photo-rename-title = Rename photo
wf-photo-rename-prompt = What would you like to label the photo?
wf-photo-write-title = Photo writing
wf-photo-write-prompt = What would you like to write on the back?
wf-photo-write-done = You write on the back of the photo.
wf-photo-examine-back = Written on the back: [italic]{$text}[/italic]
wf-photo-examine-closer = You need to get closer to get a good look at this photo.

wf-photo-window-loading = Developing...
wf-photo-window-blank = This photo is blank.
wf-photo-window-empty = There is no photo in the frame.
wf-photo-window-empty-title = Empty frame
wf-photo-window-back = Written on the back: {$text}

wf-photo-frame-full = The frame already holds a photo.
wf-photo-frame-cut = {CAPITALIZE(THE($user))} cuts the photo out of {THE($frame)}.
wf-photo-frame-name = picture frame
wf-photo-frame-description = Every time you look it makes you laugh.
wf-photo-frame-examine-photo = Use it to look at the photo. Wirecutters cut the photo out.
wf-photo-frame-examine-portrait = The frame and the picture are glued together, but you could slip a photo between the two.

wf-photo-portrait-monkey-name = Mr. Deempisi portrait
wf-photo-portrait-monkey-description = Under the portrait a plaque reads: 'While the meat grinder may not have spared you, fear not. Not one part of you has gone to waste... You were delicious.'
wf-photo-portrait-fruit-name = picture of a fruit
wf-photo-portrait-fruit-description = Ceci n'est pas une orange.
wf-photo-portrait-rat-name = Tom portrait
wf-photo-portrait-rat-description = Jerry the cat is still not amused.

wf-photo-gallery-title = Photo gallery
wf-photo-gallery-print = Print
wf-photo-gallery-delete = Delete
wf-photo-gallery-entry-name = Image {$number} (taken by {$user})
wf-photo-gallery-recorded = Image recorded.
wf-photo-gallery-printed = {CAPITALIZE(THE($user))} spits out a photograph from a narrow slot on its chassis.
wf-photo-gallery-no-charge = Not enough charge to print!

wf-photo-soul-stolen = You feel your essence draining away from having your picture taken!

trait-photographer-name = Photographer
trait-photographer-desc = You carry your camera and personal photo album everywhere you go, and your scrapbooks are legendary among your coworkers.

cmd-blankphoto-desc = Deletes a photo's image for good and blanks every copy of it, in the round and in saved albums.
cmd-blankphoto-help = Usage: {$command} <hash or photo/frame entity ID>. The hash is in the photo admin logs.
cmd-blankphoto-invalid-args = Expected one argument.
cmd-blankphoto-bad-target = {$target} isn't a photo hash or an entity holding a photo.
cmd-blankphoto-success = Blanked photo {$hash}: {$count} copies in the round. Saved copies blank when they next load.

ent-WFCamera = camera
    .desc = A polaroid camera.
ent-WFCameraDetective = detective's camera
    .desc = A silent polaroid camera with extra capacity for crime investigations.
ent-WFCameraPersonal = camera
    .desc = A polaroid camera.
    .suffix = Photographer
ent-WFCameraObscura = camera obscura
    .desc = A polaroid camera, some say it can see ghosts!
ent-WFCameraObscuraBadmin = camera obscura
    .desc = A polaroid camera, some say it can see ghosts! It seems to have an extra magnifier on the end.
    .suffix = Admin
ent-WFCameraFilm = film cartridge
    .desc = A camera film cartridge. Insert it into a camera to reload it.
ent-WFCameraBorg = cyborg photo camera
    .desc = A camera built into a cyborg. It keeps its photos and prints them on demand.
ent-WFBorgModulePhotography = photography cyborg module
ent-WFPhoto = photo
ent-WFPhotoAlbum = photo album
    .desc = A big book used to store photos and mementos.
ent-WFPhotoAlbumBlue = photo album
    .desc = A big book used to store photos and mementos.
    .suffix = Blue
ent-WFPhotoAlbumRed = photo album
    .desc = A big book used to store photos and mementos.
    .suffix = Red
ent-WFPhotoAlbumGreen = photo album
    .desc = A big book used to store photos and mementos.
    .suffix = Green
ent-WFPhotoAlbumPersonal = personal photo album
    .desc = A photographer's own album, filled over many shifts.
    .suffix = Personal
ent-WFBasePictureFrame = picture frame
    .desc = Every time you look it makes you laugh.
ent-WFPictureFrame = picture frame
    .desc = The perfect showcase for your favorite deathtrap memories.
ent-WFPictureFrameShowroom = distinguished crew display
    .desc = A photo frame to commemorate crewmembers that distinguished themselves in the line of duty. WARNING: unauthorized tampering will be severely punished.
ent-WFPictureFrameMonkey = Mr. Deempisi portrait
    .desc = Under the portrait a plaque reads: 'While the meat grinder may not have spared you, fear not. Not one part of you has gone to waste... You were delicious.'
ent-WFPictureFrameFruit = picture of a fruit
    .desc = Ceci n'est pas une orange.
ent-WFPictureFrameRat = Tom portrait
    .desc = Jerry the cat is still not amused.
ent-WFActionPhotoAiCapture = Take photo
    .desc = Take a photo of a spot the station cameras can see.
ent-WFActionPhotoGallery = View photos
    .desc = Look through the photos you have taken.
